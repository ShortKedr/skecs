const devices = new Map();
const kernels = new Map();
const wordBytes = Int32Array.BYTES_PER_ELEMENT;
const maxInt = 0x7fffffff;
let nextHandle = 1;

function allocateHandle() {
    if (nextHandle > maxInt) throw new Error('WebGPU handle limit reached');
    return nextHandle++;
}

function requireInteger(value, minimum, maximum, label) {
    if (!Number.isInteger(value) || value < minimum || value > maximum)
        throw new Error(`Invalid ${label}`);
}

function requireDevice(owner) {
    if (owner.lost) throw new Error(`WebGPU device lost: ${owner.lost.message || owner.lost.reason}`);
}

function destroyUnusedDevice(owner) {
    if (owner.closing && owner.references === 0) owner.device.destroy();
}

function releaseReference(owner) {
    owner.references--;
    destroyUnusedDevice(owner);
}

function withErrorScopes(device, submit) {
    device.pushErrorScope('internal');
    device.pushErrorScope('out-of-memory');
    device.pushErrorScope('validation');
    let command;
    try {
        command = submit();
    } catch (failure) {
        command = Promise.reject(failure);
    }
    // Pop synchronously before another kernel can submit to the same GPUDevice.
    const diagnostics = Promise.all([device.popErrorScope(), device.popErrorScope(), device.popErrorScope()]);
    return Promise.all([command, diagnostics]).then(([result, errors]) => {
        const failure = errors.find(error => error !== null);
        if (failure) throw new Error(`WebGPU ${failure.constructor.name}: ${failure.message}`);
        return result;
    });
}

export async function openDevice() {
    if (!globalThis.isSecureContext || !globalThis.navigator?.gpu)
        throw new Error('WebGPU requires a secure browser context with navigator.gpu');
    const adapter = await navigator.gpu.requestAdapter();
    if (!adapter) throw new Error('No WebGPU adapter was selected');
    const info = adapter.info;
    const isFallbackAdapter = info?.isFallbackAdapter ?? adapter.isFallbackAdapter;
    if (isFallbackAdapter !== false) throw new Error('WebGPU adapter does not report non-fallback execution');

    const deviceHandle = allocateHandle();
    const device = await adapter.requestDevice();
    try {
        const limits = device.limits;
        const maxStorageBytes = Math.min(limits.maxStorageBufferBindingSize, limits.maxBufferSize, maxInt);
        const maxStorageBufferRange = maxStorageBytes - maxStorageBytes % wordBytes;
        if (limits.maxStorageBuffersPerShaderStage < 3 || limits.maxBindingsPerBindGroup < 3 || maxStorageBufferRange < wordBytes)
            throw new Error('WebGPU device cannot bind three shader storage buffers');
        const owner = { device, references: 0, closing: false, lost: null, maxStorageBufferRange };
        device.lost.then(failure => { owner.lost = failure; });
        devices.set(deviceHandle, owner);
        return JSON.stringify({
            deviceHandle,
            vendor: info?.vendor || '',
            architecture: info?.architecture || '',
            renderer: info?.description || info?.architecture || '',
            device: info?.device || '',
            description: info?.description || '',
            isFallbackAdapter,
            maxGroupsX: limits.maxComputeWorkgroupsPerDimension,
            maxWorkgroupX: limits.maxComputeWorkgroupSizeX,
            maxWorkgroupInvocations: limits.maxComputeInvocationsPerWorkgroup,
            maxStorageBufferRange,
            maxStorageBindings: limits.maxBindingsPerBindGroup,
        });
    } catch (failure) {
        device.destroy();
        throw failure;
    }
}

export async function prepareKernel(deviceHandle, source, entryPoint, workgroupSize, inputBinding, outputBinding, faultBinding) {
    const owner = devices.get(deviceHandle);
    if (!owner) throw new Error('Invalid WebGPU device handle');
    requireDevice(owner);
    const { device } = owner;
    requireInteger(workgroupSize, 1, Math.min(device.limits.maxComputeWorkgroupSizeX,
        device.limits.maxComputeInvocationsPerWorkgroup), 'WebGPU workgroup size');
    const bindings = [inputBinding, outputBinding, faultBinding];
    for (const binding of bindings)
        requireInteger(binding, 0, device.limits.maxBindingsPerBindGroup - 1, 'WebGPU storage binding');
    if (new Set(bindings).size !== bindings.length) throw new Error('Repeated WebGPU storage binding');
    if (typeof source !== 'string' || !source || source.includes('\0') || entryPoint !== 'main')
        throw new Error('WebGPU requires WGSL source with entry point main');

    owner.references++;
    try {
        const module = await withErrorScopes(device, () => device.createShaderModule({ code: source }));
        const { messages } = await module.getCompilationInfo();
        const diagnostic = messages.find(message => message.type === 'error');
        if (diagnostic)
            throw new Error(`WGSL compilation failed at ${diagnostic.lineNum}:${diagnostic.linePos}: ${diagnostic.message}`);
        const pipeline = await withErrorScopes(device, () => {
            const layout = device.createBindGroupLayout({ entries: bindings.map((binding, index) => ({
                binding,
                visibility: GPUShaderStage.COMPUTE,
                buffer: { type: index === 0 ? 'read-only-storage' : 'storage', minBindingSize: wordBytes },
            })) });
            return device.createComputePipelineAsync({
                layout: device.createPipelineLayout({ bindGroupLayouts: [layout] }),
                compute: { module, entryPoint },
            });
        });
        requireDevice(owner);
        if (owner.closing) throw new Error('WebGPU device was released during kernel preparation');
        const handle = allocateHandle();
        kernels.set(handle, { owner, pipeline, bindings, buffers: null, busy: false, closing: false, failed: false });
        return handle;
    } catch (failure) {
        releaseReference(owner);
        throw failure;
    }
}

function destroyBuffers(buffers) {
    if (!buffers) return;
    for (const buffer of buffers.storage) buffer.destroy();
    for (const buffer of buffers.readback) buffer.destroy();
}

function ensureBuffers(kernel, sizes) {
    if (kernel.buffers && sizes.every((size, index) => kernel.buffers.sizes[index] === size)) return kernel.buffers;
    destroyBuffers(kernel.buffers);
    kernel.buffers = null;
    const { device } = kernel.owner;
    const allocated = { sizes, storage: [], readback: [], group: null };
    try {
        for (const [index, size] of sizes.entries()) {
            const usage = GPUBufferUsage.STORAGE | GPUBufferUsage.COPY_DST | (index === 0 ? 0 : GPUBufferUsage.COPY_SRC);
            allocated.storage.push(device.createBuffer({ size, usage }));
            if (index !== 0)
                allocated.readback.push(device.createBuffer({ size, usage: GPUBufferUsage.MAP_READ | GPUBufferUsage.COPY_DST }));
        }
        allocated.group = device.createBindGroup({
            layout: kernel.pipeline.getBindGroupLayout(0),
            entries: allocated.storage.map((buffer, index) => ({ binding: kernel.bindings[index], resource: { buffer } })),
        });
        kernel.buffers = allocated;
        return allocated;
    } catch (failure) {
        destroyBuffers(allocated);
        throw failure;
    }
}

function releaseKernelResources(kernel) {
    destroyBuffers(kernel.buffers);
    kernel.buffers = null;
    releaseReference(kernel.owner);
}

export async function dispatchKernel(handle, groups, input, outputLength, faultLength) {
    const kernel = kernels.get(handle);
    if (!kernel) throw new Error('Invalid WebGPU kernel handle');
    if (kernel.busy) throw new Error('WebGPU kernel already has an in-flight dispatch');
    if (kernel.failed) throw new Error('WebGPU kernel has a failed dispatch');
    const { owner } = kernel;
    requireDevice(owner);
    const { device } = owner;
    requireInteger(groups, 1, device.limits.maxComputeWorkgroupsPerDimension, 'WebGPU dispatch groups');
    if (!(input instanceof Int32Array || input instanceof Uint32Array))
        throw new Error('WebGPU dispatch input must be a packed 32-bit typed array');
    const maxWords = owner.maxStorageBufferRange / wordBytes;
    requireInteger(input.length, 1, maxWords, 'WebGPU input length');
    requireInteger(outputLength, 1, maxWords, 'WebGPU output length');
    requireInteger(faultLength, 1, maxWords, 'WebGPU fault length');
    requireInteger(outputLength + faultLength, 1, maxInt / wordBytes, 'WebGPU combined readback length');

    kernel.busy = true;
    try {
        const buffers = await withErrorScopes(device, () => {
            const current = ensureBuffers(kernel, [input.byteLength, outputLength * wordBytes, faultLength * wordBytes]);
            const [inputBuffer, output, faults] = current.storage;
            device.queue.writeBuffer(inputBuffer, 0, input);
            const encoder = device.createCommandEncoder();
            encoder.clearBuffer(output);
            encoder.clearBuffer(faults);
            const pass = encoder.beginComputePass();
            pass.setPipeline(kernel.pipeline);
            pass.setBindGroup(0, current.group);
            pass.dispatchWorkgroups(groups);
            pass.end();
            encoder.copyBufferToBuffer(output, 0, current.readback[0], 0, current.sizes[1]);
            encoder.copyBufferToBuffer(faults, 0, current.readback[1], 0, current.sizes[2]);
            device.queue.submit([encoder.finish()]);
            return current;
        });
        await Promise.all(buffers.readback.map(buffer => buffer.mapAsync(GPUMapMode.READ)));
        requireDevice(owner);
        const result = new Int32Array(outputLength + faultLength);
        try {
            result.set(new Int32Array(buffers.readback[0].getMappedRange()), 0);
            result.set(new Int32Array(buffers.readback[1].getMappedRange()), outputLength);
        } finally {
            for (const buffer of buffers.readback) buffer.unmap();
        }
        return result;
    } catch (failure) {
        kernel.failed = true;
        destroyBuffers(kernel.buffers);
        kernel.buffers = null;
        throw failure;
    } finally {
        kernel.busy = false;
        if (kernel.closing) releaseKernelResources(kernel);
    }
}

export function readKernelResult(result) {
    if (!(result instanceof Int32Array)) throw new Error('Invalid WebGPU readback payload');
    return result;
}

export function releaseKernel(handle) {
    const kernel = kernels.get(handle);
    if (!kernel) return;
    kernels.delete(handle);
    kernel.closing = true;
    if (!kernel.busy) releaseKernelResources(kernel);
}

export function releaseDevice(handle) {
    const owner = devices.get(handle);
    if (!owner) return;
    devices.delete(handle);
    owner.closing = true;
    destroyUnusedDevice(owner);
}
