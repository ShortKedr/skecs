#include "VulkanDispatch.h"

#include <array>
#include <cstring>
#include <map>
#include <mutex>
#include <set>
#include <stdexcept>
#include <string>
#include <utility>

namespace ga
{
namespace
{
void check(VkResult result, const char* operation)
{
    if (result != VK_SUCCESS)
        throw std::runtime_error(std::string(operation) + " failed with VkResult " + std::to_string(result));
}

uint32_t findComputeQueue(VkPhysicalDevice physicalDevice)
{
    uint32_t count = 0;
    vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, nullptr);
    std::vector<VkQueueFamilyProperties> queues(count);
    vkGetPhysicalDeviceQueueFamilyProperties(physicalDevice, &count, queues.data());
    for (uint32_t index = 0; index < count; ++index)
    {
        if (queues[index].queueCount && (queues[index].queueFlags & VK_QUEUE_COMPUTE_BIT))
            return index;
    }
    throw std::runtime_error("device has no compute queue");
}

struct Allocation
{
    VkBuffer buffer{};
    VkDeviceMemory memory{};
    void* mapped{};
    bool coherent{};
};

struct PipelineResources
{
    VkDevice device{};
    VkShaderModule shader{};
    std::vector<VkDescriptorSetLayout> setLayouts;
    VkPipelineLayout pipelineLayout{};
    VkPipeline pipeline{};
    VkQueue queue{};

    ~PipelineResources()
    {
        if (!device)
            return;
        vkDestroyPipeline(device, pipeline, nullptr);
        vkDestroyPipelineLayout(device, pipelineLayout, nullptr);
        for (VkDescriptorSetLayout layout : setLayouts)
            vkDestroyDescriptorSetLayout(device, layout, nullptr);
        vkDestroyShaderModule(device, shader, nullptr);
        vkDestroyDevice(device, nullptr);
    }
};

struct DispatchResources
{
    VkDevice device;
    VkDescriptorPool descriptorPool{};
    VkCommandPool commandPool{};
    VkFence fence{};
    std::vector<Allocation> buffers;

    ~DispatchResources()
    {
        vkDestroyFence(device, fence, nullptr);
        vkDestroyCommandPool(device, commandPool, nullptr);
        vkDestroyDescriptorPool(device, descriptorPool, nullptr);
        for (const auto& buffer : buffers)
        {
            if (buffer.mapped)
                vkUnmapMemory(device, buffer.memory);
            vkDestroyBuffer(device, buffer.buffer, nullptr);
            vkFreeMemory(device, buffer.memory, nullptr);
        }
    }
};

void allocateBuffer(
    VkPhysicalDevice physicalDevice, VkDevice device, const BufferBinding& binding, Allocation& allocation)
{
    VkBufferCreateInfo bufferInfo{VK_STRUCTURE_TYPE_BUFFER_CREATE_INFO};
    bufferInfo.size = binding.words.size() * sizeof(uint32_t);
    bufferInfo.usage = binding.type == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER
        ? VK_BUFFER_USAGE_UNIFORM_BUFFER_BIT : VK_BUFFER_USAGE_STORAGE_BUFFER_BIT;
    check(vkCreateBuffer(device, &bufferInfo, nullptr, &allocation.buffer), "vkCreateBuffer");

    VkMemoryRequirements requirements;
    vkGetBufferMemoryRequirements(device, allocation.buffer, &requirements);
    VkPhysicalDeviceMemoryProperties properties;
    vkGetPhysicalDeviceMemoryProperties(physicalDevice, &properties);
    uint32_t memoryType = properties.memoryTypeCount;
    for (uint32_t index = 0; index < properties.memoryTypeCount; ++index)
    {
        auto flags = properties.memoryTypes[index].propertyFlags;
        if (!(requirements.memoryTypeBits & (1u << index)) || !(flags & VK_MEMORY_PROPERTY_HOST_VISIBLE_BIT))
            continue;
        memoryType = index;
        allocation.coherent = (flags & VK_MEMORY_PROPERTY_HOST_COHERENT_BIT) != 0;
        if (allocation.coherent)
            break;
    }
    if (memoryType == properties.memoryTypeCount)
        throw std::runtime_error("buffer has no host-visible memory type");

    VkMemoryAllocateInfo memoryInfo{VK_STRUCTURE_TYPE_MEMORY_ALLOCATE_INFO};
    memoryInfo.allocationSize = requirements.size;
    memoryInfo.memoryTypeIndex = memoryType;
    check(vkAllocateMemory(device, &memoryInfo, nullptr, &allocation.memory), "vkAllocateMemory");
    check(vkBindBufferMemory(device, allocation.buffer, allocation.memory, 0), "vkBindBufferMemory");
    check(vkMapMemory(device, allocation.memory, 0, VK_WHOLE_SIZE, 0, &allocation.mapped), "vkMapMemory");
    std::memcpy(allocation.mapped, binding.words.data(), static_cast<size_t>(bufferInfo.size));
    if (!allocation.coherent)
    {
        VkMappedMemoryRange range{VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE, nullptr, allocation.memory, 0, VK_WHOLE_SIZE};
        check(vkFlushMappedMemoryRanges(device, 1, &range), "vkFlushMappedMemoryRanges");
    }
}

using SetBindings = std::vector<std::map<uint32_t, VkDescriptorSetLayoutBinding>>;

SetBindings buildLayout(const VkPhysicalDeviceLimits& limits, std::span<const BufferBinding> buffers)
{
    using Location = std::array<uint32_t, 3>;
    std::set<Location> elements;
    uint32_t storageCount = 0;
    uint32_t uniformCount = 0;
    SetBindings sets;
    for (const auto& binding : buffers)
    {
        const bool uniform = binding.type == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER;
        const auto rangeLimit = uniform ? limits.maxUniformBufferRange : limits.maxStorageBufferRange;
        const auto descriptorLimit = uniform ? limits.maxPerStageDescriptorUniformBuffers : limits.maxPerStageDescriptorStorageBuffers;
        if ((!uniform && binding.type != VK_DESCRIPTOR_TYPE_STORAGE_BUFFER)
            || (uniform && binding.readback)
            || binding.words.empty() || binding.words.size() > rangeLimit / sizeof(uint32_t)
            || binding.set >= limits.maxBoundDescriptorSets || binding.arrayElement >= descriptorLimit)
            throw std::runtime_error("buffer binding exceeds the probe contract or device limits");
        if (!elements.insert({binding.set, binding.binding, binding.arrayElement}).second)
            throw std::runtime_error("duplicate buffer binding location");

        if (sets.size() <= binding.set)
            sets.resize(static_cast<size_t>(binding.set) + 1);
        auto [item, inserted] = sets[binding.set].try_emplace(binding.binding,
            VkDescriptorSetLayoutBinding{binding.binding, binding.type, 0, VK_SHADER_STAGE_COMPUTE_BIT, nullptr});
        if (!inserted && item->second.descriptorType != binding.type)
            throw std::runtime_error("array binding has mixed descriptor types");
        if (item->second.descriptorCount <= binding.arrayElement)
            item->second.descriptorCount = binding.arrayElement + 1;
        uniform ? ++uniformCount : ++storageCount;
    }
    if (uniformCount > limits.maxDescriptorSetUniformBuffers || uniformCount > limits.maxPerStageDescriptorUniformBuffers
        || storageCount > limits.maxDescriptorSetStorageBuffers || storageCount > limits.maxPerStageDescriptorStorageBuffers
        || buffers.size() > limits.maxPerStageResources)
        throw std::runtime_error("descriptor count exceeds device limits");

    for (uint32_t set = 0; set < sets.size(); ++set)
    {
        for (const auto& [binding, info] : sets[set])
        {
            for (uint32_t index = 0; index < info.descriptorCount; ++index)
            {
                if (!elements.contains({set, binding, index}))
                    throw std::runtime_error("array binding has missing elements");
            }
        }
    }
    return sets;
}
}

VulkanInstance::VulkanInstance()
{
    VkApplicationInfo app{VK_STRUCTURE_TYPE_APPLICATION_INFO};
    app.pApplicationName = "SKECS GA compute probe";
    app.apiVersion = VK_API_VERSION_1_1;
    VkInstanceCreateInfo createInfo{VK_STRUCTURE_TYPE_INSTANCE_CREATE_INFO};
    createInfo.pApplicationInfo = &app;
    check(vkCreateInstance(&createInfo, nullptr, &handle), "vkCreateInstance");
}

VulkanInstance::~VulkanInstance()
{
    vkDestroyInstance(handle, nullptr);
}

std::vector<VkPhysicalDevice> VulkanInstance::devices() const
{
    uint32_t count = 0;
    check(vkEnumeratePhysicalDevices(handle, &count, nullptr), "vkEnumeratePhysicalDevices count");
    if (count == 0)
        return {};
    std::vector<VkPhysicalDevice> result(count);
    check(vkEnumeratePhysicalDevices(handle, &count, result.data()), "vkEnumeratePhysicalDevices");
    result.resize(count);
    return result;
}

struct VulkanKernel::Impl
{
    VkPhysicalDevice physicalDevice{};
    VkPhysicalDeviceLimits limits{};
    uint32_t queueFamily{};
    uint32_t pushConstantBytes{};
    PipelineResources resources;
    std::vector<VkDescriptorPoolSize> poolSizes;
    std::vector<std::array<uint32_t, 5>> signature;
    std::mutex executeLock;
};

namespace
{
std::array<uint32_t, 5> signatureOf(const BufferBinding& binding)
{
    return {binding.set, binding.binding, binding.arrayElement, static_cast<uint32_t>(binding.type), binding.readback};
}
}

VulkanKernel::VulkanKernel(VkPhysicalDevice physicalDevice, std::span<const uint32_t> spirv, const char* entry,
    uint32_t workgroupX, uint32_t pushConstantBytes, std::span<const BufferBinding> bindings)
    : impl(std::make_unique<Impl>())
{
    impl->physicalDevice = physicalDevice;
    impl->pushConstantBytes = pushConstantBytes;
    VkPhysicalDeviceProperties properties;
    vkGetPhysicalDeviceProperties(physicalDevice, &properties);
    impl->limits = properties.limits;
    if (properties.apiVersion < VK_API_VERSION_1_1)
        throw std::runtime_error("device does not expose Vulkan 1.1");
    if (workgroupX == 0 || workgroupX > properties.limits.maxComputeWorkGroupSize[0]
        || workgroupX > properties.limits.maxComputeWorkGroupInvocations)
        throw std::runtime_error("workgroup size exceeds device limits");
    if (pushConstantBytes % 4 != 0 || pushConstantBytes > properties.limits.maxPushConstantsSize)
        throw std::runtime_error("push constants exceed device limits");
    if (spirv.size() < 5 || spirv[0] != 0x07230203 || spirv[1] < 0x00010000 || spirv[1] > 0x00010300)
        throw std::runtime_error("expected a SPIR-V 1.0–1.3 artifact");
    auto descriptorLayouts = buildLayout(properties.limits, bindings);
    for (const auto& binding : bindings)
        impl->signature.push_back(signatureOf(binding));

    impl->queueFamily = findComputeQueue(physicalDevice);
    float priority = 1;
    VkDeviceQueueCreateInfo queueInfo{VK_STRUCTURE_TYPE_DEVICE_QUEUE_CREATE_INFO};
    queueInfo.queueFamilyIndex = impl->queueFamily;
    queueInfo.queueCount = 1;
    queueInfo.pQueuePriorities = &priority;
    VkDeviceCreateInfo deviceInfo{VK_STRUCTURE_TYPE_DEVICE_CREATE_INFO};
    deviceInfo.queueCreateInfoCount = 1;
    deviceInfo.pQueueCreateInfos = &queueInfo;
    auto& resources = impl->resources;
    check(vkCreateDevice(physicalDevice, &deviceInfo, nullptr, &resources.device), "vkCreateDevice");
    vkGetDeviceQueue(resources.device, impl->queueFamily, 0, &resources.queue);

    VkShaderModuleCreateInfo shaderInfo{VK_STRUCTURE_TYPE_SHADER_MODULE_CREATE_INFO};
    shaderInfo.codeSize = spirv.size_bytes();
    shaderInfo.pCode = spirv.data();
    check(vkCreateShaderModule(resources.device, &shaderInfo, nullptr, &resources.shader), "vkCreateShaderModule");

    std::map<VkDescriptorType, uint32_t> counts;
    resources.setLayouts.resize(descriptorLayouts.size());
    for (size_t set = 0; set < descriptorLayouts.size(); ++set)
    {
        std::vector<VkDescriptorSetLayoutBinding> layoutBindings;
        for (const auto& [index, binding] : descriptorLayouts[set])
        {
            layoutBindings.push_back(binding);
            counts[binding.descriptorType] += binding.descriptorCount;
        }
        VkDescriptorSetLayoutCreateInfo layoutInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_LAYOUT_CREATE_INFO};
        layoutInfo.bindingCount = static_cast<uint32_t>(layoutBindings.size());
        layoutInfo.pBindings = layoutBindings.data();
        check(vkCreateDescriptorSetLayout(resources.device, &layoutInfo, nullptr, &resources.setLayouts[set]), "vkCreateDescriptorSetLayout");
    }
    for (const auto& [type, count] : counts)
        impl->poolSizes.push_back({type, count});

    VkPushConstantRange pushRange{VK_SHADER_STAGE_COMPUTE_BIT, 0, pushConstantBytes};
    VkPipelineLayoutCreateInfo pipelineLayoutInfo{VK_STRUCTURE_TYPE_PIPELINE_LAYOUT_CREATE_INFO};
    pipelineLayoutInfo.setLayoutCount = static_cast<uint32_t>(resources.setLayouts.size());
    pipelineLayoutInfo.pSetLayouts = resources.setLayouts.data();
    if (pushConstantBytes)
    {
        pipelineLayoutInfo.pushConstantRangeCount = 1;
        pipelineLayoutInfo.pPushConstantRanges = &pushRange;
    }
    check(vkCreatePipelineLayout(resources.device, &pipelineLayoutInfo, nullptr, &resources.pipelineLayout), "vkCreatePipelineLayout");

    VkComputePipelineCreateInfo pipelineInfo{VK_STRUCTURE_TYPE_COMPUTE_PIPELINE_CREATE_INFO};
    pipelineInfo.stage = {VK_STRUCTURE_TYPE_PIPELINE_SHADER_STAGE_CREATE_INFO};
    pipelineInfo.stage.stage = VK_SHADER_STAGE_COMPUTE_BIT;
    pipelineInfo.stage.module = resources.shader;
    pipelineInfo.stage.pName = entry;
    pipelineInfo.layout = resources.pipelineLayout;
    check(vkCreateComputePipelines(resources.device, VK_NULL_HANDLE, 1, &pipelineInfo, nullptr, &resources.pipeline), "vkCreateComputePipelines");
}

VulkanKernel::~VulkanKernel() = default;

void VulkanKernel::execute(uint32_t groups, std::span<const uint32_t> pushConstants, std::span<BufferBinding> bindings)
{
    if (groups == 0 || groups > impl->limits.maxComputeWorkGroupCount[0])
        throw std::runtime_error("dispatch group count is outside device limits");
    if (pushConstants.size_bytes() != impl->pushConstantBytes || bindings.size() != impl->signature.size())
        throw std::runtime_error("dispatch data does not match the pipeline layout");
    for (size_t index = 0; index < bindings.size(); ++index)
    {
        const auto& binding = bindings[index];
        auto rangeLimit = binding.type == VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER ? impl->limits.maxUniformBufferRange : impl->limits.maxStorageBufferRange;
        if (signatureOf(binding) != impl->signature[index] || binding.words.empty()
            || binding.words.size() > rangeLimit / sizeof(uint32_t))
            throw std::runtime_error("dispatch binding does not match the pipeline layout or device limits");
    }

    std::lock_guard guard(impl->executeLock);
    const auto& pipeline = impl->resources;
    DispatchResources resources{pipeline.device};
    std::vector<VkDescriptorSet> sets(pipeline.setLayouts.size());
    if (!sets.empty())
    {
        VkDescriptorPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_POOL_CREATE_INFO};
        poolInfo.maxSets = static_cast<uint32_t>(sets.size());
        poolInfo.poolSizeCount = static_cast<uint32_t>(impl->poolSizes.size());
        poolInfo.pPoolSizes = impl->poolSizes.data();
        check(vkCreateDescriptorPool(resources.device, &poolInfo, nullptr, &resources.descriptorPool), "vkCreateDescriptorPool");
        VkDescriptorSetAllocateInfo setsInfo{VK_STRUCTURE_TYPE_DESCRIPTOR_SET_ALLOCATE_INFO};
        setsInfo.descriptorPool = resources.descriptorPool;
        setsInfo.descriptorSetCount = static_cast<uint32_t>(sets.size());
        setsInfo.pSetLayouts = pipeline.setLayouts.data();
        check(vkAllocateDescriptorSets(resources.device, &setsInfo, sets.data()), "vkAllocateDescriptorSets");
    }
    resources.buffers.resize(bindings.size());
    for (size_t index = 0; index < bindings.size(); ++index)
    {
        const auto& binding = bindings[index];
        auto& allocation = resources.buffers[index];
        allocateBuffer(impl->physicalDevice, resources.device, binding, allocation);
        VkDescriptorBufferInfo bufferInfo{allocation.buffer, 0, binding.words.size() * sizeof(uint32_t)};
        VkWriteDescriptorSet write{VK_STRUCTURE_TYPE_WRITE_DESCRIPTOR_SET};
        write.dstSet = sets[binding.set];
        write.dstBinding = binding.binding;
        write.dstArrayElement = binding.arrayElement;
        write.descriptorCount = 1;
        write.descriptorType = binding.type;
        write.pBufferInfo = &bufferInfo;
        vkUpdateDescriptorSets(resources.device, 1, &write, 0, nullptr);
    }

    VkCommandPoolCreateInfo poolInfo{VK_STRUCTURE_TYPE_COMMAND_POOL_CREATE_INFO};
    poolInfo.queueFamilyIndex = impl->queueFamily;
    check(vkCreateCommandPool(resources.device, &poolInfo, nullptr, &resources.commandPool), "vkCreateCommandPool");
    VkCommandBufferAllocateInfo commandInfo{VK_STRUCTURE_TYPE_COMMAND_BUFFER_ALLOCATE_INFO};
    commandInfo.commandPool = resources.commandPool;
    commandInfo.commandBufferCount = 1;
    VkCommandBuffer command;
    check(vkAllocateCommandBuffers(resources.device, &commandInfo, &command), "vkAllocateCommandBuffers");
    VkCommandBufferBeginInfo beginInfo{VK_STRUCTURE_TYPE_COMMAND_BUFFER_BEGIN_INFO};
    beginInfo.flags = VK_COMMAND_BUFFER_USAGE_ONE_TIME_SUBMIT_BIT;
    check(vkBeginCommandBuffer(command, &beginInfo), "vkBeginCommandBuffer");
    vkCmdBindPipeline(command, VK_PIPELINE_BIND_POINT_COMPUTE, pipeline.pipeline);
    if (!sets.empty())
        vkCmdBindDescriptorSets(command, VK_PIPELINE_BIND_POINT_COMPUTE, pipeline.pipelineLayout, 0,
            static_cast<uint32_t>(sets.size()), sets.data(), 0, nullptr);
    if (!pushConstants.empty())
        vkCmdPushConstants(command, pipeline.pipelineLayout, VK_SHADER_STAGE_COMPUTE_BIT, 0, impl->pushConstantBytes, pushConstants.data());
    vkCmdDispatch(command, groups, 1, 1);
    VkMemoryBarrier readback{VK_STRUCTURE_TYPE_MEMORY_BARRIER, nullptr, VK_ACCESS_SHADER_WRITE_BIT, VK_ACCESS_HOST_READ_BIT};
    vkCmdPipelineBarrier(command, VK_PIPELINE_STAGE_COMPUTE_SHADER_BIT, VK_PIPELINE_STAGE_HOST_BIT,
        0, 1, &readback, 0, nullptr, 0, nullptr);
    check(vkEndCommandBuffer(command), "vkEndCommandBuffer");

    VkFenceCreateInfo fenceInfo{VK_STRUCTURE_TYPE_FENCE_CREATE_INFO};
    check(vkCreateFence(resources.device, &fenceInfo, nullptr, &resources.fence), "vkCreateFence");
    VkSubmitInfo submit{VK_STRUCTURE_TYPE_SUBMIT_INFO};
    submit.commandBufferCount = 1;
    submit.pCommandBuffers = &command;
    check(vkQueueSubmit(pipeline.queue, 1, &submit, resources.fence), "vkQueueSubmit");
    const VkResult wait = vkWaitForFences(resources.device, 1, &resources.fence, VK_TRUE, UINT64_MAX);
    if (wait != VK_SUCCESS && wait != VK_ERROR_DEVICE_LOST)
        vkQueueWaitIdle(pipeline.queue);
    check(wait, "vkWaitForFences");

    for (size_t index = 0; index < bindings.size(); ++index)
    {
        auto& binding = bindings[index];
        const auto& allocation = resources.buffers[index];
        if (!binding.readback)
            continue;
        if (!allocation.coherent)
        {
            VkMappedMemoryRange range{VK_STRUCTURE_TYPE_MAPPED_MEMORY_RANGE, nullptr, allocation.memory, 0, VK_WHOLE_SIZE};
            check(vkInvalidateMappedMemoryRanges(resources.device, 1, &range), "vkInvalidateMappedMemoryRanges");
        }
        std::memcpy(binding.words.data(), allocation.mapped, binding.words.size() * sizeof(uint32_t));
    }
}

void dispatch(
    VkPhysicalDevice physicalDevice, std::span<const uint32_t> spirv, const char* entry,
    uint32_t workgroupX, uint32_t groups, std::span<const uint32_t> pushConstants, std::span<BufferBinding> bindings)
{
    VulkanKernel kernel(physicalDevice, spirv, entry, workgroupX, static_cast<uint32_t>(pushConstants.size_bytes()), bindings);
    kernel.execute(groups, pushConstants, bindings);
}
}
