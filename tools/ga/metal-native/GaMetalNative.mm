#include "GaMetalNative.h"

#import <Foundation/Foundation.h>
#import <Metal/Metal.h>
#import <TargetConditionals.h>
#include <dispatch/dispatch.h>

#include <algorithm>
#include <array>
#include <cstddef>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>

namespace
{
static_assert(sizeof(GaMetalDevice) == 280);
static_assert(offsetof(GaMetalDevice, name) == 24);
constexpr uint32_t absentSlot = std::numeric_limits<uint32_t>::max();
constexpr uint32_t bufferSlots = 31;
using Locations = std::array<uint32_t, 4>;

struct Context
{
    NSArray<id<MTLDevice>>* devices;
};

struct Kernel
{
    id<MTLDevice> device;
    id<MTLCommandQueue> queue;
    id<MTLComputePipelineState> pipeline;
    Locations locations;
    uint32_t workgroupX;
    std::mutex dispatchGate;
};

void require(bool condition, const char* message)
{
    if (!condition)
        throw std::runtime_error(message);
}

void writeError(char* destination, uint32_t capacity, const char* message)
{
    if (!destination || capacity == 0)
        return;
    auto length = std::min(static_cast<size_t>(capacity - 1), std::strlen(message));
    std::memcpy(destination, message, length);
    destination[length] = '\0';
}

[[noreturn]] void failMetal(const char* operation, NSError* error)
{
    if (!error)
        throw std::runtime_error(operation);
    throw std::runtime_error(std::string(operation) + ": " +
        (error.domain.UTF8String ?: "unknown") + "/" + std::to_string(error.code) + " " +
        (error.localizedDescription.UTF8String ?: ""));
}

template<class Work>
int32_t perform(char* error, uint32_t errorCapacity, Work work) noexcept
{
    writeError(error, errorCapacity, "");
    @autoreleasepool
    {
        @try
        {
            try
            {
                work();
                return 0;
            }
            catch (const std::exception& failure)
            {
                writeError(error, errorCapacity, failure.what());
            }
        }
        @catch (NSException* failure)
        {
            writeError(error, errorCapacity, failure.reason.UTF8String ?: "Metal framework exception");
        }
    }
    return 1;
}

id<MTLDevice> getDevice(void* handle, uint32_t index)
{
    require(handle != nullptr, "Metal context is required");
    auto& context = *static_cast<Context*>(handle);
    require(index < context.devices.count, "Metal device index is out of range");
    return context.devices[index];
}

void validateLocations(const Locations& locations)
{
    for (size_t i = 0; i < locations.size(); ++i)
    {
        if (i == 3 && locations[i] == absentSlot)
            continue;
        require(locations[i] < bufferSlots, "Metal buffer slot is out of range");
        for (size_t j = 0; j < i; ++j)
            require(locations[i] != locations[j], "Metal buffer slots must be distinct");
    }
}

bool wordArray(MTLStructType* structure)
{
    if (structure.members.count != 1)
        return false;
    MTLStructMember* member = structure.members.firstObject;
    if (member.offset != 0 || member.dataType != MTLDataTypeArray)
        return false;
    MTLArrayType* type = member.arrayType;
    return type.elementType == MTLDataTypeUInt && type.arrayLength == 1 && type.stride == sizeof(uint32_t);
}

bool wordBuffer(MTLArgument* argument, bool length)
{
    if (argument.bufferDataType == MTLDataTypePointer)
    {
        MTLPointerType* pointer = argument.bufferPointerType;
        if (!pointer || pointer.elementIsArgumentBuffer ||
            pointer.dataSize != sizeof(uint32_t) || pointer.alignment != alignof(uint32_t))
            return false;
        return length
            ? pointer.elementType == MTLDataTypeUInt
            : pointer.elementType == MTLDataTypeStruct && wordArray(pointer.elementStructType);
    }
    if (argument.bufferDataSize != sizeof(uint32_t))
        return false;
    return length
        ? argument.bufferDataType == MTLDataTypeUInt
        : argument.bufferDataType == MTLDataTypeStruct && wordArray(argument.bufferStructType);
}

void validateReflection(MTLComputePipelineReflection* reflection, const Locations& locations)
{
    require(reflection != nil, "Metal pipeline did not return reflection");
    NSArray<NSString*>* names = @[@"gaInput", @"gaOutput", @"gaFaults", @"spvBufferSizeConstants"];
    const size_t bindingCount = locations[3] == absentSlot ? 3 : 4;
    std::array<bool, 4> seen{};
    for (MTLArgument* argument in reflection.arguments)
    {
        if (!argument.active)
            continue;
        const auto end = locations.begin() + bindingCount;
        const auto match = std::find(locations.begin(), end, argument.index);
        require(argument.type == MTLArgumentTypeBuffer && match != end,
            "Metal reflection found an unexpected resource");
        const auto role = static_cast<size_t>(match - locations.begin());
        const bool readsOnly = role == 0 || role == 3;
        const bool accessMatches = readsOnly
            ? argument.access == MTLArgumentAccessReadOnly
            : argument.access == MTLArgumentAccessReadWrite || argument.access == MTLArgumentAccessWriteOnly;
        require(!seen[role] && [argument.name isEqualToString:names[role]] &&
            accessMatches && wordBuffer(argument, role == 3),
            "Metal reflection disagrees with a GA buffer slot, access or word layout");
        seen[role] = true;
    }
    require(std::all_of(seen.begin(), seen.begin() + bindingCount, [](bool value) { return value; }),
        "Metal reflection is missing a required GA buffer");
}

std::unique_ptr<Kernel> createKernel(id<MTLDevice> device, id<MTLLibrary> library,
    NSString* entry, uint32_t workgroupX, const Locations& locations)
{
    validateLocations(locations);
    require(workgroupX > 0 && workgroupX <= device.maxThreadsPerThreadgroup.width,
        "Metal workgroup exceeds device limits");
    id<MTLFunction> function = [library newFunctionWithName:entry];
    require(function != nil && function.functionType == MTLFunctionTypeKernel,
        "Metal library is missing the requested compute entry");
    NSError* error = nil;
    MTLComputePipelineReflection* reflection = nil;
    const auto options = MTLPipelineOptionArgumentInfo | MTLPipelineOptionBufferTypeInfo;
    id<MTLComputePipelineState> pipeline = [device newComputePipelineStateWithFunction:function
        options:options reflection:&reflection error:&error];
    if (!pipeline)
        failMetal("Metal compute pipeline creation failed", error);
    require(workgroupX <= pipeline.maxTotalThreadsPerThreadgroup && pipeline.staticThreadgroupMemoryLength == 0,
        "Metal pipeline workgroup or threadgroup memory is incompatible with this profile");
    validateReflection(reflection, locations);

    id<MTLCommandQueue> queue = [device newCommandQueue];
    require(queue != nil, "Metal command queue creation failed");
    return std::unique_ptr<Kernel>(new Kernel{device, queue, pipeline, locations, workgroupX, {}});
}

id<MTLBuffer> makeBuffer(id<MTLDevice> device, const uint32_t* words, uint32_t bytes)
{
    // The default storage mode also handles discrete macOS GPUs.
    id<MTLBuffer> buffer = [device newBufferWithLength:bytes options:MTLResourceCPUCacheModeDefaultCache];
    require(buffer != nil && buffer.contents != nullptr, "Metal CPU-visible buffer allocation failed");
    std::memcpy(buffer.contents, words, bytes);
#if TARGET_OS_OSX
    if (buffer.storageMode == MTLStorageModeManaged)
        [buffer didModifyRange:NSMakeRange(0, bytes)];
#endif
    return buffer;
}

uint32_t bufferByteCount(uint32_t words, id<MTLDevice> device)
{
    const uint64_t bytes = static_cast<uint64_t>(words) * sizeof(uint32_t);
    require(bytes != 0 && bytes <= device.maxBufferLength && bytes <= std::numeric_limits<uint32_t>::max(),
        "Metal storage buffer exceeds device or byte-count limits");
    return static_cast<uint32_t>(bytes);
}

void dispatch(Kernel& kernel, uint32_t groupsX, const uint32_t* input, uint32_t inputLength,
    uint32_t* output, uint32_t outputLength, uint32_t* faults, uint32_t faultsLength)
{
    require(groupsX > 0 && groupsX <= std::numeric_limits<uint32_t>::max() / kernel.workgroupX,
        "Metal dispatch width exceeds the 32-bit invocation range");
    require(input && output && faults, "Metal dispatch requires three payload buffers");
    const uint32_t inputBytes = bufferByteCount(inputLength, kernel.device);
    const uint32_t outputBytes = bufferByteCount(outputLength, kernel.device);
    const uint32_t faultBytes = bufferByteCount(faultsLength, kernel.device);
    std::lock_guard lock(kernel.dispatchGate);
    id<MTLBuffer> source = makeBuffer(kernel.device, input, inputBytes);
    id<MTLBuffer> result = makeBuffer(kernel.device, output, outputBytes);
    id<MTLBuffer> faultBuffer = makeBuffer(kernel.device, faults, faultBytes);
    id<MTLCommandBuffer> command = [kernel.queue commandBuffer];
    require(command != nil, "Metal command buffer creation failed");
    id<MTLComputeCommandEncoder> encoder = [command computeCommandEncoder];
    require(encoder != nil, "Metal compute encoder creation failed");
    [encoder setComputePipelineState:kernel.pipeline];
    [encoder setBuffer:source offset:0 atIndex:kernel.locations[0]];
    [encoder setBuffer:result offset:0 atIndex:kernel.locations[1]];
    [encoder setBuffer:faultBuffer offset:0 atIndex:kernel.locations[2]];
    if (kernel.locations[3] != absentSlot)
        [encoder setBytes:&inputBytes length:sizeof(inputBytes) atIndex:kernel.locations[3]];
    [encoder dispatchThreadgroups:MTLSizeMake(groupsX, 1, 1)
        threadsPerThreadgroup:MTLSizeMake(kernel.workgroupX, 1, 1)];
    [encoder endEncoding];
#if TARGET_OS_OSX
    if (result.storageMode == MTLStorageModeManaged || faultBuffer.storageMode == MTLStorageModeManaged)
    {
        id<MTLBlitCommandEncoder> blit = [command blitCommandEncoder];
        require(blit != nil, "Metal managed readback encoder creation failed");
        if (result.storageMode == MTLStorageModeManaged)
            [blit synchronizeResource:result];
        if (faultBuffer.storageMode == MTLStorageModeManaged)
            [blit synchronizeResource:faultBuffer];
        [blit endEncoding];
    }
#endif
    [command commit];
    [command waitUntilCompleted];
    if (command.status != MTLCommandBufferStatusCompleted)
        failMetal("Metal compute dispatch or readback failed", command.error);
    std::memcpy(output, result.contents, outputBytes);
    std::memcpy(faults, faultBuffer.contents, faultBytes);
}

const char* startupSource = R"metal(
#include <metal_stdlib>
using namespace metal;

struct Words { uint data[1]; };

kernel void skecs_ga_startup(const device Words& gaInput [[buffer(7)]],
    device Words& gaOutput [[buffer(3)]], device Words& gaFaults [[buffer(5)]],
    constant uint* spvBufferSizeConstants [[buffer(26)]], uint index [[thread_position_in_grid]])
{
    uint words = spvBufferSizeConstants[0] / 4;
    if (words > 0 && index < gaInput.data[0] && index < words - 1)
    {
        int value = as_type<int>(gaInput.data[index + 1]);
        gaOutput.data[index] = as_type<uint>(value / 3);
        gaFaults.data[index] = as_type<uint>(value % 3);
    }
}
)metal";
}

int32_t skecs_ga_metal_open(void** context, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(context != nullptr, "Metal context destination is required");
        *context = nullptr;
#if TARGET_OS_OSX
        auto devices = MTLCopyAllDevices();
#else
        id<MTLDevice> device = MTLCreateSystemDefaultDevice();
        NSArray<id<MTLDevice>>* devices = device ? @[device] : @[];
#endif
        *context = new Context{devices};
    });
}

void skecs_ga_metal_close(void* context)
{
    @autoreleasepool
    {
        delete static_cast<Context*>(context);
    }
}

int32_t skecs_ga_metal_device_count(void* context, uint32_t* count, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(context && count, "Metal context and device count destination are required");
        *count = static_cast<uint32_t>(static_cast<Context*>(context)->devices.count);
    });
}

int32_t skecs_ga_metal_get_device(
    void* context, uint32_t index, GaMetalDevice* device, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(device != nullptr, "Metal device destination is required");
        id<MTLDevice> native = getDevice(context, index);
        const auto width = static_cast<uint32_t>(native.maxThreadsPerThreadgroup.width);
        const auto storage = static_cast<uint32_t>(std::min<uint64_t>(native.maxBufferLength, absentSlot));
#if TARGET_OS_OSX
        *device = {native.registryID, 1, width, width, storage, {}};
#else
        *device = {0, 2, width, width, storage, {}};
#endif
        writeError(device->name, sizeof(device->name), native.name.UTF8String ?: "Metal device");
    });
}

int32_t skecs_ga_metal_probe(void* context, uint32_t index, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        id<MTLDevice> device = getDevice(context, index);
        MTLCompileOptions* options = [MTLCompileOptions new];
        options.languageVersion = MTLLanguageVersion2_3;
        NSError* compilationError = nil;
        id<MTLLibrary> library = [device newLibraryWithSource:[NSString stringWithUTF8String:startupSource]
            options:options error:&compilationError];
        if (!library)
            failMetal("Metal startup probe compilation failed", compilationError);
        auto kernel = createKernel(device, library, @"skecs_ga_startup", 64, {7, 3, 5, 26});

        constexpr std::array<int32_t, 4> values{-7, std::numeric_limits<int32_t>::min(),
            7, std::numeric_limits<int32_t>::max()};
        constexpr uint32_t sentinel = 0x0BADC0DEu;
        std::array<uint32_t, values.size() + 1> input{}, quotient{}, remainder{};
        input[0] = input.size(); // One more row than the actual byte-count argument allows.
        std::transform(values.begin(), values.end(), input.begin() + 1,
            [](int32_t value) { return static_cast<uint32_t>(value); });
        quotient.fill(sentinel);
        remainder.fill(sentinel);
        dispatch(*kernel, 1, input.data(), input.size(), quotient.data(), quotient.size(),
            remainder.data(), remainder.size());
        for (size_t i = 0; i < values.size(); ++i)
            require(quotient[i] == static_cast<uint32_t>(values[i] / 3) &&
                remainder[i] == static_cast<uint32_t>(values[i] % 3),
                "Metal startup probe readback differs from exact CPU arithmetic");
        require(quotient.back() == sentinel && remainder.back() == sentinel,
            "Metal startup probe wrote past the exact byte-count range");
    });
}

int32_t skecs_ga_metal_create_kernel(void* context, uint32_t index, const uint8_t* artifact,
    uint32_t artifactLength, const char* entry, uint32_t workgroupX,
    const uint32_t* locations, void** kernel, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel != nullptr, "Metal kernel destination is required");
        *kernel = nullptr;
        require(artifact && artifactLength > 4 && std::memcmp(artifact, "MTLB", 4) == 0,
            "Metal kernel requires a compiled metallib artifact");
        require(entry && entry[0] != '\0' && locations, "Metal kernel entry and buffer locations are required");
        NSString* name = [NSString stringWithUTF8String:entry];
        require(name != nil, "Metal kernel entry is invalid UTF-8");
        id<MTLDevice> device = getDevice(context, index);
        dispatch_data_t data = dispatch_data_create(artifact, artifactLength,
            dispatch_get_global_queue(QOS_CLASS_DEFAULT, 0), DISPATCH_DATA_DESTRUCTOR_DEFAULT);
        require(data != nullptr, "Metal library data allocation failed");
        NSError* loadError = nil;
        id<MTLLibrary> library = [device newLibraryWithData:data error:&loadError];
        if (!library)
            failMetal("Metal library loading failed", loadError);
        auto state = createKernel(device, library, name, workgroupX,
            {locations[0], locations[1], locations[2], locations[3]});
        *kernel = state.release();
    });
}

void skecs_ga_metal_release_kernel(void* kernel)
{
    @autoreleasepool
    {
        delete static_cast<Kernel*>(kernel);
    }
}

int32_t skecs_ga_metal_dispatch(void* kernel, uint32_t groupsX, const uint32_t* input, uint32_t inputLength,
    uint32_t* output, uint32_t outputLength, uint32_t* faults, uint32_t faultsLength,
    char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel != nullptr, "Metal kernel is required");
        dispatch(*static_cast<Kernel*>(kernel), groupsX, input, inputLength, output, outputLength, faults, faultsLength);
    });
}
