#pragma once

#include <stdint.h>

#if defined(SKECS_GA_D3D11_BUILD)
#define SKECS_GA_D3D11_API __declspec(dllexport)
#else
#define SKECS_GA_D3D11_API __declspec(dllimport)
#endif
#define SKECS_GA_D3D11_CALL __cdecl

#ifdef __cplusplus
extern "C" {
#endif

typedef struct GaD3D11Device
{
    uint32_t vendorId, deviceId, shaderModel, driverVersionLow, driverVersionHigh, deviceType;
    uint32_t maxGroupsX, maxWorkgroupX, maxWorkgroupInvocations, maxStorageBufferRange;
    char name[256];
    uint8_t deviceLuid[8];
} GaD3D11Device;

typedef struct GaD3D11Location
{
    uint32_t space, index;
} GaD3D11Location;

// Status 0 means success. Status 1 includes a NUL-terminated UTF-8 error when a buffer is provided.
SKECS_GA_D3D11_API int32_t SKECS_GA_D3D11_CALL skecs_ga_d3d11_open(void** context, char* error, uint32_t errorCapacity);
SKECS_GA_D3D11_API void SKECS_GA_D3D11_CALL skecs_ga_d3d11_close(void* context);
SKECS_GA_D3D11_API int32_t SKECS_GA_D3D11_CALL skecs_ga_d3d11_device_count(void* context, uint32_t* count,
    char* error, uint32_t errorCapacity);
SKECS_GA_D3D11_API int32_t SKECS_GA_D3D11_CALL skecs_ga_d3d11_get_device(void* context, uint32_t index,
    GaD3D11Device* device, char* error, uint32_t errorCapacity);
SKECS_GA_D3D11_API int32_t SKECS_GA_D3D11_CALL skecs_ga_d3d11_probe(void* context, uint32_t index,
    char* error, uint32_t errorCapacity);

// Locations are input SRV, output UAV and fault UAV, in that order. A kernel outlives its enumeration context.
SKECS_GA_D3D11_API int32_t SKECS_GA_D3D11_CALL skecs_ga_d3d11_create_kernel(void* context, uint32_t index,
    const uint32_t* artifact, uint32_t artifactWordCount, uint32_t workgroupX, const GaD3D11Location* locations,
    void** kernel, char* error, uint32_t errorCapacity);
SKECS_GA_D3D11_API void SKECS_GA_D3D11_CALL skecs_ga_d3d11_release_kernel(void* kernel);

// Lengths are 32-bit words. Readback is copied only after both GPU readbacks succeed.
// The kernel and payloads must remain live until this call returns.
SKECS_GA_D3D11_API int32_t SKECS_GA_D3D11_CALL skecs_ga_d3d11_dispatch(void* kernel, uint32_t groupsX,
    const uint32_t* input, uint32_t inputLength, uint32_t* output, uint32_t outputLength,
    uint32_t* faults, uint32_t faultsLength, char* error, uint32_t errorCapacity);

#ifdef __cplusplus
}
#endif
