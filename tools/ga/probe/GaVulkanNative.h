#pragma once

#include <stdint.h>

#if defined(_WIN32)
#if defined(SKECS_GA_VK_BUILD)
#define SKECS_GA_VK_API __declspec(dllexport)
#else
#define SKECS_GA_VK_API __declspec(dllimport)
#endif
#define SKECS_GA_VK_CALL __cdecl
#else
#define SKECS_GA_VK_API __attribute__((visibility("default")))
#define SKECS_GA_VK_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct GaVulkanDevice
{
    uint32_t vendorId, deviceId, apiVersion, driverVersion, deviceType;
    uint32_t maxGroupsX, maxWorkgroupX, maxWorkgroupInvocations, maxStorageBufferRange;
    uint32_t maxBoundDescriptorSets, maxPerStageStorageBuffers, maxDescriptorSetStorageBuffers;
    char name[256];
    uint8_t deviceUuid[16];
} GaVulkanDevice;

typedef struct GaVulkanLocation
{
    uint32_t set, binding;
} GaVulkanLocation;

// Status 0 means success. Status 1 includes a NUL-terminated UTF-8 error when a buffer is provided.
SKECS_GA_VK_API int32_t SKECS_GA_VK_CALL skecs_ga_vk_open(void** context, char* error, uint32_t errorCapacity);
SKECS_GA_VK_API void SKECS_GA_VK_CALL skecs_ga_vk_close(void* context);
SKECS_GA_VK_API int32_t SKECS_GA_VK_CALL skecs_ga_vk_device_count(void* context, uint32_t* count, char* error, uint32_t errorCapacity);
SKECS_GA_VK_API int32_t SKECS_GA_VK_CALL skecs_ga_vk_get_device(void* context, uint32_t index,
    GaVulkanDevice* device, char* error, uint32_t errorCapacity);
SKECS_GA_VK_API int32_t SKECS_GA_VK_CALL skecs_ga_vk_probe(void* context, uint32_t index, char* error, uint32_t errorCapacity);

// locations has three entries in input, output, fault order. The kernel outlives the context if needed.
SKECS_GA_VK_API int32_t SKECS_GA_VK_CALL skecs_ga_vk_create_kernel(void* context, uint32_t index,
    const uint32_t* artifact, uint32_t artifactWordCount, const char* entry, uint32_t workgroupX,
    const GaVulkanLocation* locations, void** kernel, char* error, uint32_t errorCapacity);
SKECS_GA_VK_API void SKECS_GA_VK_CALL skecs_ga_vk_release_kernel(void* kernel);

// All lengths are 32-bit words. On success, output and faults contain the completed readback.
// Keep the kernel handle and all payloads alive until the call has returned.
SKECS_GA_VK_API int32_t SKECS_GA_VK_CALL skecs_ga_vk_dispatch(void* kernel, uint32_t groupsX,
    const uint32_t* input, uint32_t inputLength, uint32_t* output, uint32_t outputLength,
    uint32_t* faults, uint32_t faultsLength, char* error, uint32_t errorCapacity);

#ifdef __cplusplus
}
#endif
