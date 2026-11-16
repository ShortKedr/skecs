#include "D3D12Dispatch.h"

#include <cstring>
#include <cstdio>
#include <map>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <tuple>
#include <wrl/client.h>

namespace ga
{
namespace
{
using Microsoft::WRL::ComPtr;

void check(HRESULT status, const char* operation)
{
    if (FAILED(status))
    {
        char message[160];
        std::snprintf(message, sizeof(message), "%s failed with HRESULT 0x%08lX", operation, static_cast<unsigned long>(status));
        throw std::runtime_error(message);
    }
}

ComPtr<ID3D12Resource> makeBuffer(ID3D12Device* device, D3D12_HEAP_TYPE heap, D3D12_RESOURCE_FLAGS flags,
    D3D12_RESOURCE_STATES state, uint64_t bytes)
{
    D3D12_HEAP_PROPERTIES properties{heap, D3D12_CPU_PAGE_PROPERTY_UNKNOWN, D3D12_MEMORY_POOL_UNKNOWN, 1, 1};
    D3D12_RESOURCE_DESC descriptor{};
    descriptor.Dimension = D3D12_RESOURCE_DIMENSION_BUFFER;
    descriptor.Width = bytes;
    descriptor.Height = 1;
    descriptor.DepthOrArraySize = 1;
    descriptor.MipLevels = 1;
    descriptor.SampleDesc.Count = 1;
    descriptor.Layout = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
    descriptor.Flags = flags;
    ComPtr<ID3D12Resource> resource;
    check(device->CreateCommittedResource(&properties, D3D12_HEAP_FLAG_NONE, &descriptor, state, nullptr, IID_PPV_ARGS(&resource)),
        "CreateCommittedResource");
    return resource;
}

void transition(ID3D12GraphicsCommandList* list, ID3D12Resource* resource,
    D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after)
{
    D3D12_RESOURCE_BARRIER barrier{};
    barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
    barrier.Transition = {resource, D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES, before, after};
    list->ResourceBarrier(1, &barrier);
}

struct Buffer
{
    ComPtr<ID3D12Resource> upload, gpu, readback;
};

using Register = std::tuple<D3D12_DESCRIPTOR_RANGE_TYPE, uint32_t, uint32_t>;
using BindingLayout = std::tuple<D3D12_DESCRIPTOR_RANGE_TYPE, uint32_t, uint32_t, uint32_t, uint32_t>;

BindingLayout layout(const D3D12Binding& binding)
{
    return {binding.type, binding.space, binding.index, binding.arrayElement, binding.stride};
}

std::vector<D3D12_DESCRIPTOR_RANGE> descriptorRanges(std::span<const D3D12Binding> bindings)
{
    std::map<Register, D3D12_DESCRIPTOR_RANGE> groups;
    std::map<Register, bool> occupied;
    for (uint32_t offset = 0; offset < bindings.size(); ++offset)
    {
        const auto& binding = bindings[offset];
        auto [it, added] = groups.try_emplace({binding.type, binding.space, binding.index},
            D3D12_DESCRIPTOR_RANGE{binding.type, 0, binding.index, binding.space, offset});
        auto& range = it->second;
        if (range.NumDescriptors != binding.arrayElement || range.OffsetInDescriptorsFromTableStart + binding.arrayElement != offset
            || !occupied.emplace(Register{binding.type, binding.space, binding.index + binding.arrayElement}, true).second)
            throw std::runtime_error("D3D12 descriptor arrays must be contiguous and may not overlap");
        ++range.NumDescriptors;
    }
    std::vector<D3D12_DESCRIPTOR_RANGE> output;
    for (const auto& [key, range] : groups)
        output.push_back(range);
    return output;
}

struct CloseEvent
{
    void operator()(void* handle) const { CloseHandle(handle); }
};
}

struct D3D12Kernel::State
{
    std::vector<BindingLayout> layouts;
    ComPtr<ID3D12Device> device;
    ComPtr<ID3D12RootSignature> root;
    ComPtr<ID3D12PipelineState> pipeline;
    ComPtr<ID3D12CommandQueue> queue;
    std::mutex executionLock;
};

D3D12Kernel::D3D12Kernel(IDXGIAdapter1* adapter, std::span<const uint32_t> dxil,
    std::span<const D3D12Binding> bindings) : state(std::make_unique<State>())
{
    if (!adapter || dxil.size() < 8 || dxil[0] != 0x43425844 || bindings.empty())
        throw std::runtime_error("D3D12 kernel requires an adapter, shader container and descriptor layout");
    for (const auto& binding : bindings)
        state->layouts.push_back(layout(binding));
    auto ranges = descriptorRanges(bindings);
    D3D12_ROOT_PARAMETER parameter{};
    parameter.ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
    parameter.DescriptorTable = {static_cast<UINT>(ranges.size()), ranges.data()};
    parameter.ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;
    D3D12_ROOT_SIGNATURE_DESC signature{1, &parameter, 0, nullptr, D3D12_ROOT_SIGNATURE_FLAG_NONE};
    ComPtr<ID3DBlob> signatureBlob, signatureErrors;
    auto status = D3D12SerializeRootSignature(&signature, D3D_ROOT_SIGNATURE_VERSION_1, &signatureBlob, &signatureErrors);
    if (FAILED(status) && signatureErrors)
        throw std::runtime_error(std::string(static_cast<const char*>(signatureErrors->GetBufferPointer()), signatureErrors->GetBufferSize()));
    check(status, "D3D12SerializeRootSignature");

    auto& [layouts, device, root, pipeline, queue, executionLock] = *state;
    check(D3D12CreateDevice(adapter, D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&device)), "D3D12CreateDevice");
    check(device->CreateRootSignature(0, signatureBlob->GetBufferPointer(), signatureBlob->GetBufferSize(), IID_PPV_ARGS(&root)),
        "CreateRootSignature");
    D3D12_COMPUTE_PIPELINE_STATE_DESC pipelineState{};
    pipelineState.pRootSignature = root.Get();
    pipelineState.CS = {dxil.data(), dxil.size_bytes()};
    check(device->CreateComputePipelineState(&pipelineState, IID_PPV_ARGS(&pipeline)), "CreateComputePipelineState");
    D3D12_COMMAND_QUEUE_DESC queueDesc{D3D12_COMMAND_LIST_TYPE_COMPUTE, D3D12_COMMAND_QUEUE_PRIORITY_NORMAL,
        D3D12_COMMAND_QUEUE_FLAG_NONE, 0};
    check(device->CreateCommandQueue(&queueDesc, IID_PPV_ARGS(&queue)), "CreateCommandQueue");
}

D3D12Kernel::~D3D12Kernel() = default;

void D3D12Kernel::execute(uint32_t groupCount, std::span<D3D12Binding> bindings)
{
    if (!groupCount || groupCount > D3D12_CS_DISPATCH_MAX_THREAD_GROUPS_PER_DIMENSION)
        throw std::runtime_error("D3D12 compute dispatch group count is out of range");
    auto& [layouts, device, root, pipeline, queue, executionLock] = *state;
    if (bindings.size() != layouts.size())
        throw std::runtime_error("D3D12 dispatch descriptor count changed after pipeline creation");
    for (size_t index = 0; index < bindings.size(); ++index)
        if (layouts[index] != layout(bindings[index]))
            throw std::runtime_error("D3D12 dispatch descriptor layout changed after pipeline creation");
    std::lock_guard lock(executionLock);
    check(device->GetDeviceRemovedReason(), "D3D12 device before dispatch");

    ComPtr<ID3D12CommandAllocator> allocator;
    check(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_COMPUTE, IID_PPV_ARGS(&allocator)), "CreateCommandAllocator");
    ComPtr<ID3D12GraphicsCommandList> list;
    check(device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_COMPUTE, allocator.Get(), pipeline.Get(), IID_PPV_ARGS(&list)), "CreateCommandList");

    D3D12_DESCRIPTOR_HEAP_DESC heapDesc{D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, static_cast<UINT>(bindings.size()),
        D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE, 0};
    ComPtr<ID3D12DescriptorHeap> heap;
    check(device->CreateDescriptorHeap(&heapDesc, IID_PPV_ARGS(&heap)), "CreateDescriptorHeap");
    auto cpuHandle = heap->GetCPUDescriptorHandleForHeapStart();
    auto descriptorIncrement = device->GetDescriptorHandleIncrementSize(heapDesc.Type);
    std::vector<Buffer> buffers(bindings.size());
    for (size_t index = 0; index < bindings.size(); ++index, cpuHandle.ptr += descriptorIncrement)
    {
        const auto& binding = bindings[index];
        auto& buffer = buffers[index];
        const auto bytes = binding.words.size() * sizeof(uint32_t);
        if (!bytes || !binding.stride || bytes % binding.stride != 0)
            throw std::runtime_error("D3D12 buffer has invalid layout");
        constexpr uint64_t alignment = D3D12_CONSTANT_BUFFER_DATA_PLACEMENT_ALIGNMENT;
        const auto allocated = binding.type == D3D12_DESCRIPTOR_RANGE_TYPE_CBV ? (bytes + alignment - 1) & ~(alignment - 1) : bytes;
        buffer.upload = makeBuffer(device.Get(), D3D12_HEAP_TYPE_UPLOAD, D3D12_RESOURCE_FLAG_NONE,
            D3D12_RESOURCE_STATE_GENERIC_READ, allocated);
        void* mapped;
        D3D12_RANGE readNone{0, 0};
        check(buffer.upload->Map(0, &readNone, &mapped), "Map upload");
        std::memset(mapped, 0, allocated);
        std::memcpy(mapped, binding.words.data(), bytes);
        D3D12_RANGE uploadWritten{0, allocated};
        buffer.upload->Unmap(0, &uploadWritten);

        if (binding.type == D3D12_DESCRIPTOR_RANGE_TYPE_CBV)
        {
            D3D12_CONSTANT_BUFFER_VIEW_DESC view{buffer.upload->GetGPUVirtualAddress(), static_cast<UINT>(allocated)};
            device->CreateConstantBufferView(&view, cpuHandle);
        }
        else if (binding.type == D3D12_DESCRIPTOR_RANGE_TYPE_SRV)
        {
            D3D12_SHADER_RESOURCE_VIEW_DESC view{};
            view.ViewDimension = D3D12_SRV_DIMENSION_BUFFER;
            view.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            view.Buffer.NumElements = static_cast<UINT>(bytes / binding.stride);
            view.Buffer.StructureByteStride = binding.stride;
            device->CreateShaderResourceView(buffer.upload.Get(), &view, cpuHandle);
        }
        else if (binding.type == D3D12_DESCRIPTOR_RANGE_TYPE_UAV)
        {
            buffer.gpu = makeBuffer(device.Get(), D3D12_HEAP_TYPE_DEFAULT, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS,
                D3D12_RESOURCE_STATE_COPY_DEST, bytes);
            buffer.readback = makeBuffer(device.Get(), D3D12_HEAP_TYPE_READBACK, D3D12_RESOURCE_FLAG_NONE,
                D3D12_RESOURCE_STATE_COPY_DEST, bytes);
            list->CopyBufferRegion(buffer.gpu.Get(), 0, buffer.upload.Get(), 0, bytes);
            transition(list.Get(), buffer.gpu.Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
            D3D12_UNORDERED_ACCESS_VIEW_DESC view{};
            view.ViewDimension = D3D12_UAV_DIMENSION_BUFFER;
            view.Buffer.NumElements = static_cast<UINT>(bytes / binding.stride);
            view.Buffer.StructureByteStride = binding.stride;
            device->CreateUnorderedAccessView(buffer.gpu.Get(), nullptr, &view, cpuHandle);
        }
        else
        {
            throw std::runtime_error("unsupported D3D12 descriptor type");
        }
    }

    ID3D12DescriptorHeap* heaps[]{heap.Get()};
    list->SetDescriptorHeaps(1, heaps);
    list->SetComputeRootSignature(root.Get());
    list->SetComputeRootDescriptorTable(0, heap->GetGPUDescriptorHandleForHeapStart());
    list->Dispatch(groupCount, 1, 1);
    for (size_t index = 0; index < buffers.size(); ++index)
    {
        const auto& buffer = buffers[index];
        if (!buffer.gpu)
            continue;
        transition(list.Get(), buffer.gpu.Get(), D3D12_RESOURCE_STATE_UNORDERED_ACCESS, D3D12_RESOURCE_STATE_COPY_SOURCE);
        list->CopyBufferRegion(buffer.readback.Get(), 0, buffer.gpu.Get(), 0, bindings[index].words.size() * sizeof(uint32_t));
    }
    check(list->Close(), "Close command list");
    ComPtr<ID3D12Fence> fence;
    check(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&fence)), "CreateFence");
    std::unique_ptr<void, CloseEvent> event(CreateEventW(nullptr, FALSE, FALSE, nullptr));
    if (!event)
        check(HRESULT_FROM_WIN32(GetLastError()), "CreateEventW");
    check(fence->SetEventOnCompletion(1, event.get()), "SetEventOnCompletion");
    ID3D12CommandList* submission[]{list.Get()};
    queue->ExecuteCommandLists(1, submission);
    const auto signal = queue->Signal(fence.Get(), 1);
    if (FAILED(signal))
    {
        // The list was already submitted. Establish a fence or observe device removal
        // before releasing anything the GPU might still be using.
        while (SUCCEEDED(device->GetDeviceRemovedReason()))
        {
            if (SUCCEEDED(queue->Signal(fence.Get(), 1)))
                break;
            Sleep(1);
        }
    }
    DWORD wait = WAIT_TIMEOUT;
    while (wait == WAIT_TIMEOUT && SUCCEEDED(device->GetDeviceRemovedReason()))
        wait = WaitForSingleObject(event.get(), 1000);
    if (wait != WAIT_OBJECT_0 && wait != WAIT_TIMEOUT)
    {
        while (fence->GetCompletedValue() < 1 && SUCCEEDED(device->GetDeviceRemovedReason()))
            Sleep(1);
        throw std::runtime_error("D3D12 fence wait failed");
    }
    check(signal, "Signal fence");
    check(device->GetDeviceRemovedReason(), "D3D12 device execution");

    for (size_t index = 0; index < buffers.size(); ++index)
    {
        const auto& buffer = buffers[index];
        if (!buffer.readback)
            continue;
        auto& destination = bindings[index].words;
        const auto bytes = destination.size() * sizeof(uint32_t);
        D3D12_RANGE readRange{0, bytes}, written{0, 0};
        void* mapped;
        check(buffer.readback->Map(0, &readRange, &mapped), "Map readback");
        std::memcpy(destination.data(), mapped, bytes);
        buffer.readback->Unmap(0, &written);
    }
}

void dispatchD3D12(IDXGIAdapter1* adapter, std::span<const uint32_t> dxil, uint32_t groupCount,
    std::span<D3D12Binding> bindings)
{
    D3D12Kernel kernel(adapter, dxil, bindings);
    kernel.execute(groupCount, bindings);
}
}
