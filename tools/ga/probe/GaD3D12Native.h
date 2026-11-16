#pragma once

#include <stdint.h>

#if defined(SKECS_GA_D3D12_BUILD)
#define SKECS_GA_D3D12_API __declspec(dllexport)
#else
#define SKECS_GA_D3D12_API __declspec(dllimport)
#endif
#define SKECS_GA_D3D12_CALL __cdecl

#ifdef __cplusplus
extern "C" {
#endif

typedef struct GaD3D12Device
{
    uint32_t vendorId, deviceId, shaderModel, driverVersionLow, driverVersionHigh, deviceType;
    uint32_t maxGroupsX, maxWorkgroupX, maxWorkgroupInvocations, maxStorageBufferRange;
    char name[256];
    uint8_t deviceLuid[8];
} GaD3D12Device;

typedef struct GaD3D12Location
{
    uint32_t space, index;
} GaD3D12Location;

// Status 0 means success. Status 1 includes a NUL-terminated UTF-8 error when a buffer is provided.
SKECS_GA_D3D12_API int32_t SKECS_GA_D3D12_CALL skecs_ga_d3d12_open(void** context, char* error, uint32_t errorCapacity);
SKECS_GA_D3D12_API void SKECS_GA_D3D12_CALL skecs_ga_d3d12_close(void* context);
SKECS_GA_D3D12_API int32_t SKECS_GA_D3D12_CALL skecs_ga_d3d12_device_count(void* context, uint32_t* count,
    char* error, uint32_t errorCapacity);
SKECS_GA_D3D12_API int32_t SKECS_GA_D3D12_CALL skecs_ga_d3d12_get_device(void* context, uint32_t index,
    GaD3D12Device* device, char* error, uint32_t errorCapacity);
SKECS_GA_D3D12_API int32_t SKECS_GA_D3D12_CALL skecs_ga_d3d12_probe(void* context, uint32_t index,
    char* error, uint32_t errorCapacity);

// Locations are input SRV, output UAV and fault UAV, in that order. A kernel outlives its enumeration context.
SKECS_GA_D3D12_API int32_t SKECS_GA_D3D12_CALL skecs_ga_d3d12_create_kernel(void* context, uint32_t index,
    const uint32_t* artifact, uint32_t artifactWordCount, uint32_t workgroupX, const GaD3D12Location* locations,
    void** kernel, char* error, uint32_t errorCapacity);
SKECS_GA_D3D12_API void SKECS_GA_D3D12_CALL skecs_ga_d3d12_release_kernel(void* kernel);

// Lengths are 32-bit words. Readback is copied only after a successful dispatch and fence wait.
// The kernel and payloads must remain live until this call returns.
SKECS_GA_D3D12_API int32_t SKECS_GA_D3D12_CALL skecs_ga_d3d12_dispatch(void* kernel, uint32_t groupsX,
    const uint32_t* input, uint32_t inputLength, uint32_t* output, uint32_t outputLength,
    uint32_t* faults, uint32_t faultsLength, char* error, uint32_t errorCapacity);

#ifdef __cplusplus
}
#endif
