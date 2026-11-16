#pragma once

#include <cstdint>
#include <memory>
#include <span>
#include <vector>
#include <vulkan/vulkan.h>

namespace ga
{
struct BufferBinding
{
    uint32_t set;
    uint32_t binding;
    uint32_t arrayElement;
    VkDescriptorType type;
    bool readback;
    std::vector<uint32_t> words;
};

class VulkanInstance
{
public:
    VulkanInstance();
    ~VulkanInstance();
    VulkanInstance(const VulkanInstance&) = delete;
    VulkanInstance& operator=(const VulkanInstance&) = delete;

    std::vector<VkPhysicalDevice> devices() const;

private:
    VkInstance handle{};
};

class VulkanKernel
{
public:
    VulkanKernel(VkPhysicalDevice physicalDevice, std::span<const uint32_t> spirv, const char* entry,
        uint32_t workgroupX, uint32_t pushConstantBytes, std::span<const BufferBinding> bindings);
    ~VulkanKernel();
    VulkanKernel(const VulkanKernel&) = delete;
    VulkanKernel& operator=(const VulkanKernel&) = delete;

    void execute(uint32_t groups, std::span<const uint32_t> pushConstants, std::span<BufferBinding> bindings);

private:
    struct Impl;
    std::unique_ptr<Impl> impl;
};

void dispatch(
    VkPhysicalDevice physicalDevice, std::span<const uint32_t> spirv, const char* entry,
    uint32_t workgroupX, uint32_t groups, std::span<const uint32_t> pushConstants, std::span<BufferBinding> bindings);
}
