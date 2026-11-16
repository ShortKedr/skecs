#pragma once

#include <stddef.h>
#include <stdint.h>

#if defined(SKECS_GA_GL_BUILD)
#define SKECS_GA_GL_API __declspec(dllexport)
#else
#define SKECS_GA_GL_API __declspec(dllimport)
#endif
#define SKECS_GA_GL_CALL __cdecl

#ifdef __cplusplus
extern "C" {
#endif

typedef struct GaOpenGlDevice
{
    uint32_t glMajor, glMinor, maxGroupsX, maxWorkgroupX, maxWorkgroupInvocations;
    uint32_t maxStorageBufferRange, maxStorageBindings;
    char vendor[128], renderer[256], version[256];
} GaOpenGlDevice;

// Status 0 means success. Status 1 includes a NUL-terminated UTF-8 error when a buffer is provided.
SKECS_GA_GL_API int32_t SKECS_GA_GL_CALL skecs_ga_gl_open(void** context, char* error, uint32_t errorCapacity);
// ANGLE accepts an absolute DLL directory. Renderer 0 is D3D11, 1 is Vulkan, and 2 is OpenGL.
SKECS_GA_GL_API int32_t SKECS_GA_GL_CALL skecs_ga_gl_open_es(const wchar_t* angleDirectory, uint32_t renderer,
    void** context, char* error, uint32_t errorCapacity);
SKECS_GA_GL_API void SKECS_GA_GL_CALL skecs_ga_gl_close(void* context);
SKECS_GA_GL_API int32_t SKECS_GA_GL_CALL skecs_ga_gl_device_count(void* context, uint32_t* count,
    char* error, uint32_t errorCapacity);
SKECS_GA_GL_API int32_t SKECS_GA_GL_CALL skecs_ga_gl_get_device(void* context, uint32_t index,
    GaOpenGlDevice* device, char* error, uint32_t errorCapacity);
SKECS_GA_GL_API int32_t SKECS_GA_GL_CALL skecs_ga_gl_probe(void* context, uint32_t index,
    char* error, uint32_t errorCapacity);

// Bindings are input, output and fault SSBOs in that order. A kernel outlives its enumeration context.
SKECS_GA_GL_API int32_t SKECS_GA_GL_CALL skecs_ga_gl_create_kernel(void* context, uint32_t index,
    const char* source, uint32_t sourceByteCount, uint32_t workgroupX, const uint32_t* bindings,
    void** kernel, char* error, uint32_t errorCapacity);
SKECS_GA_GL_API void SKECS_GA_GL_CALL skecs_ga_gl_release_kernel(void* kernel);

// Lengths are in 32-bit words. The kernel and payloads must remain live until this call returns.
SKECS_GA_GL_API int32_t SKECS_GA_GL_CALL skecs_ga_gl_dispatch(void* kernel, uint32_t groupsX,
    const uint32_t* input, uint32_t inputLength, uint32_t* output, uint32_t outputLength,
    uint32_t* faults, uint32_t faultsLength, char* error, uint32_t errorCapacity);

#ifdef __cplusplus
}
#endif
