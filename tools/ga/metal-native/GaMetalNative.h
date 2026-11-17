#pragma once

#include <stdint.h>

#define SKECS_GA_METAL_API __attribute__((visibility("default")))

#ifdef __cplusplus
extern "C" {
#endif

typedef struct GaMetalDevice
{
    uint64_t registryId;
    uint32_t platform; // 1 for macOS, 2 for iOS.
    uint32_t maxWorkgroupX;
    uint32_t maxWorkgroupInvocations;
    uint32_t maxStorageBufferRange;
    char name[256];
} GaMetalDevice;

// Status 0 means success. Status 1 can return a NUL-terminated UTF-8 error.
SKECS_GA_METAL_API int32_t skecs_ga_metal_open(void** context, char* error, uint32_t errorCapacity);
SKECS_GA_METAL_API void skecs_ga_metal_close(void* context);
SKECS_GA_METAL_API int32_t skecs_ga_metal_device_count(
    void* context, uint32_t* count, char* error, uint32_t errorCapacity);
SKECS_GA_METAL_API int32_t skecs_ga_metal_get_device(
    void* context, uint32_t index, GaMetalDevice* device, char* error, uint32_t errorCapacity);
SKECS_GA_METAL_API int32_t skecs_ga_metal_probe(void* context, uint32_t index, char* error, uint32_t errorCapacity);

// locations contains input, output, fault and input byte-length slots. UINT32_MAX
// means the byte-length slot is absent. The kernel retains its own device and queue.
SKECS_GA_METAL_API int32_t skecs_ga_metal_create_kernel(void* context, uint32_t index,
    const uint8_t* artifact, uint32_t artifactLength, const char* entry, uint32_t workgroupX,
    const uint32_t* locations, void** kernel, char* error, uint32_t errorCapacity);
SKECS_GA_METAL_API void skecs_ga_metal_release_kernel(void* kernel);

// Payload lengths are in 32-bit words. Output and faults are updated on success.
SKECS_GA_METAL_API int32_t skecs_ga_metal_dispatch(void* kernel, uint32_t groupsX,
    const uint32_t* input, uint32_t inputLength, uint32_t* output, uint32_t outputLength,
    uint32_t* faults, uint32_t faultsLength, char* error, uint32_t errorCapacity);

#ifdef __cplusplus
}
#endif
