#include "GaVulkanNative.h"
#include "VulkanDispatch.h"

#include <algorithm>
#include <array>
#include <cstring>
#include <limits>
#include <memory>
#include <stdexcept>

namespace
{
#include "StartupSpirv.h"

static_assert(sizeof(GaVulkanDevice) == 320);
static_assert(sizeof(GaVulkanLocation) == 8);

struct Context
{
    ga::VulkanInstance instance;
    std::vector<VkPhysicalDevice> devices = instance.devices();
};

using ContextHandle = std::shared_ptr<Context>;

struct Kernel
{
    ContextHandle context;
    std::array<GaVulkanLocation, 3> locations;
    ga::VulkanKernel native;
    uint32_t maxBufferWords;
};

void require(bool condition, const char* text)
{
    if (!condition)
        throw std::runtime_error(text);
}

void writeError(char* error, uint32_t capacity, const char* text)
{
    if (!error || !capacity)
        return;
    auto length = std::min(static_cast<size_t>(capacity - 1), std::strlen(text));
    std::memcpy(error, text, length);
    error[length] = '\0';
}

template<class Work>
int32_t perform(char* error, uint32_t errorCapacity, Work work) noexcept
{
    writeError(error, errorCapacity, "");
    try
    {
        work();
        return 0;
    }
    catch (const std::exception& failure)
    {
        writeError(error, errorCapacity, failure.what());
    }
    catch (...)
    {
        writeError(error, errorCapacity, "unrecognized native Vulkan failure");
    }
    return 1;
}

const ContextHandle& getContext(void* handle)
{
    require(handle != nullptr, "Vulkan context is required");
    return *static_cast<ContextHandle*>(handle);
}

VkPhysicalDevice getDevice(const Context& context, uint32_t index)
{
    require(index < context.devices.size(), "Vulkan device index is out of range");
    return context.devices[index];
}

ga::BufferBinding makeBuffer(const GaVulkanLocation& location, bool readback, std::vector<uint32_t> words)
{
    return {location.set, location.binding, 0, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, readback, std::move(words)};
}
}

int32_t skecs_ga_vk_open(void** context, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(context != nullptr, "Vulkan context destination is required");
        *context = nullptr;
        auto handle = std::make_unique<ContextHandle>(std::make_shared<Context>());
        *context = handle.release();
    });
}

void skecs_ga_vk_close(void* context)
{
    delete static_cast<ContextHandle*>(context);
}

int32_t skecs_ga_vk_device_count(void* context, uint32_t* count, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(count != nullptr, "Vulkan device count destination is required");
        *count = static_cast<uint32_t>(getContext(context)->devices.size());
    });
}

int32_t skecs_ga_vk_get_device(void* context, uint32_t index, GaVulkanDevice* device, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(device != nullptr, "Vulkan device destination is required");
        auto physical = getDevice(*getContext(context), index);
        VkPhysicalDeviceProperties properties;
        vkGetPhysicalDeviceProperties(physical, &properties);
        const auto& limits = properties.limits;
        *device = {properties.vendorID, properties.deviceID, properties.apiVersion, properties.driverVersion,
            static_cast<uint32_t>(properties.deviceType),
            limits.maxComputeWorkGroupCount[0], limits.maxComputeWorkGroupSize[0], limits.maxComputeWorkGroupInvocations,
            limits.maxStorageBufferRange, limits.maxBoundDescriptorSets,
            limits.maxPerStageDescriptorStorageBuffers, limits.maxDescriptorSetStorageBuffers, {}, {}};
        std::memcpy(device->name, properties.deviceName, sizeof(device->name));
        if (properties.apiVersion >= VK_API_VERSION_1_1)
        {
            VkPhysicalDeviceIDProperties ids{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_ID_PROPERTIES};
            VkPhysicalDeviceProperties2 properties2{VK_STRUCTURE_TYPE_PHYSICAL_DEVICE_PROPERTIES_2, &ids};
            vkGetPhysicalDeviceProperties2(physical, &properties2);
            std::memcpy(device->deviceUuid, ids.deviceUUID, sizeof(device->deviceUuid));
        }
    });
}

int32_t skecs_ga_vk_probe(void* context, uint32_t index, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        constexpr uint32_t sentinel = 0xBADC0DEu;
        constexpr std::array values{-7, std::numeric_limits<int32_t>::min(), 7, std::numeric_limits<int32_t>::max()};
        std::vector<uint32_t> input{static_cast<uint32_t>(values.size())};
        std::vector<uint32_t> expectedQuotients, expectedRemainders;
        for (auto value : values)
        {
            input.push_back(static_cast<uint32_t>(value));
            expectedQuotients.push_back(static_cast<uint32_t>(value / 3));
            expectedRemainders.push_back(static_cast<uint32_t>(value % 3));
        }
        expectedQuotients.push_back(sentinel);
        expectedRemainders.push_back(sentinel);
        std::array buffers{
            makeBuffer({0, 3}, false, std::move(input)),
            makeBuffer({0, 5}, true, std::vector<uint32_t>(expectedQuotients.size(), sentinel)),
            makeBuffer({0, 7}, true, std::vector<uint32_t>(expectedRemainders.size(), sentinel))};
        auto physical = getDevice(*getContext(context), index);
        ga::dispatch(physical, startupSpirv, "main", 64, 1, {}, buffers);
        require(buffers[1].words == expectedQuotients && buffers[2].words == expectedRemainders,
            "Vulkan startup probe readback differs from exact CPU arithmetic");
    });
}

int32_t skecs_ga_vk_create_kernel(void* context, uint32_t index, const uint32_t* artifact, uint32_t artifactWordCount,
    const char* entry, uint32_t workgroupX, const GaVulkanLocation* locations, void** kernel, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel != nullptr, "Vulkan kernel destination is required");
        *kernel = nullptr;
        require(artifact != nullptr && artifactWordCount >= 5 && entry != nullptr && entry[0] != '\0' && locations != nullptr,
            "Vulkan kernel artifact, entry point and locations are required");
        std::array<GaVulkanLocation, 3> map{locations[0], locations[1], locations[2]};
        std::array buffers{
            makeBuffer(map[0], false, {0}), makeBuffer(map[1], true, {0}), makeBuffer(map[2], true, {0})};
        auto owner = getContext(context);
        auto physical = getDevice(*owner, index);
        VkPhysicalDeviceProperties properties;
        vkGetPhysicalDeviceProperties(physical, &properties);
        auto handle = new Kernel{std::move(owner), map, {physical, {artifact, artifactWordCount}, entry, workgroupX, 0, buffers},
            properties.limits.maxStorageBufferRange / 4};
        *kernel = handle;
    });
}

void skecs_ga_vk_release_kernel(void* kernel)
{
    delete static_cast<Kernel*>(kernel);
}

int32_t skecs_ga_vk_dispatch(void* kernel, uint32_t groupsX, const uint32_t* input, uint32_t inputLength,
    uint32_t* output, uint32_t outputLength, uint32_t* faults, uint32_t faultsLength, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel && input && output && faults && inputLength && outputLength && faultsLength,
            "Vulkan dispatch requires a kernel and three nonempty buffers");
        auto& state = *static_cast<Kernel*>(kernel);
        require(inputLength <= state.maxBufferWords && outputLength <= state.maxBufferWords && faultsLength <= state.maxBufferWords,
            "Vulkan dispatch storage buffer exceeds device limits");
        std::array buffers{
            makeBuffer(state.locations[0], false, {input, input + inputLength}),
            makeBuffer(state.locations[1], true, {output, output + outputLength}),
            makeBuffer(state.locations[2], true, {faults, faults + faultsLength})};
        state.native.execute(groupsX, {}, buffers);
        std::copy(buffers[1].words.begin(), buffers[1].words.end(), output);
        std::copy(buffers[2].words.begin(), buffers[2].words.end(), faults);
    });
}
