#include "GaD3D12Native.h"
#include "D3D12Dispatch.h"

#include <algorithm>
#include <array>
#include <bit>
#include <cstring>
#include <limits>
#include <stdexcept>
#include <string_view>
#include <vector>
#include <wrl/client.h>

namespace
{
#include "StartupDxil.h"

using Microsoft::WRL::ComPtr;
static_assert(sizeof(GaD3D12Device) == 304);
static_assert(sizeof(GaD3D12Location) == 8);
static_assert(sizeof(LUID) == 8);
constexpr uint32_t maxBufferBytes = D3D12_REQ_RESOURCE_SIZE_IN_MEGABYTES_EXPRESSION_A_TERM * 1024u * 1024u;

struct Device
{
    ComPtr<IDXGIAdapter1> adapter;
    GaD3D12Device info;
};

struct Context
{
    std::vector<Device> devices;
};

struct Kernel
{
    std::array<GaD3D12Location, 3> locations;
    ga::D3D12Kernel native;
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
        writeError(error, errorCapacity, "unrecognized native D3D12 failure");
    }
    return 1;
}

std::vector<Device> enumerateDevices()
{
    ComPtr<IDXGIFactory4> factory;
    require(SUCCEEDED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))), "DXGI factory creation failed");
    std::vector<Device> devices;
    for (UINT index = 0;; ++index)
    {
        ComPtr<IDXGIAdapter1> adapter;
        const auto status = factory->EnumAdapters1(index, &adapter);
        if (status == DXGI_ERROR_NOT_FOUND)
            break;
        require(SUCCEEDED(status), "DXGI adapter enumeration failed");
        DXGI_ADAPTER_DESC1 properties{};
        require(SUCCEEDED(adapter->GetDesc1(&properties)), "DXGI adapter description failed");
        if (properties.Flags & (DXGI_ADAPTER_FLAG_SOFTWARE | DXGI_ADAPTER_FLAG_REMOTE))
            continue;
        ComPtr<ID3D12Device> device;
        if (FAILED(D3D12CreateDevice(adapter.Get(), D3D_FEATURE_LEVEL_12_0, IID_PPV_ARGS(&device))))
            continue;
        D3D12_FEATURE_DATA_SHADER_MODEL shader{D3D_SHADER_MODEL_6_0};
        if (FAILED(device->CheckFeatureSupport(D3D12_FEATURE_SHADER_MODEL, &shader, sizeof(shader)))
            || shader.HighestShaderModel < D3D_SHADER_MODEL_6_0)
            continue;
        D3D12_FEATURE_DATA_ARCHITECTURE architecture{};
        require(SUCCEEDED(device->CheckFeatureSupport(D3D12_FEATURE_ARCHITECTURE, &architecture, sizeof(architecture))),
            "D3D12 device architecture query failed");

        LARGE_INTEGER driver{};
        if (FAILED(adapter->CheckInterfaceSupport(__uuidof(IDXGIDevice), &driver)))
            driver.QuadPart = 0;
        GaD3D12Device info{properties.VendorId, properties.DeviceId, static_cast<uint32_t>(shader.HighestShaderModel),
            driver.LowPart, static_cast<uint32_t>(driver.HighPart), architecture.UMA ? 1u : 2u,
            D3D12_CS_DISPATCH_MAX_THREAD_GROUPS_PER_DIMENSION, D3D12_CS_THREAD_GROUP_MAX_X,
            D3D12_CS_THREAD_GROUP_MAX_THREADS_PER_GROUP, maxBufferBytes, {}, {}};
        constexpr size_t nameCapacity = sizeof(properties.Description) * 2;
        std::array<char, nameCapacity> name{};
        auto length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, properties.Description, -1,
            name.data(), static_cast<int>(name.size()), nullptr, nullptr);
        require(length != 0, "DXGI device name conversion failed");
        auto copy = std::min(static_cast<size_t>(length - 1), sizeof(info.name) - 1);
        while ((static_cast<unsigned char>(name[copy]) & 0xc0u) == 0x80u)
            --copy;
        std::memcpy(info.name, name.data(), copy);
        std::memcpy(info.deviceLuid, &properties.AdapterLuid, sizeof(info.deviceLuid));
        devices.push_back({std::move(adapter), info});
    }
    return devices;
}

const Context& getContext(void* handle)
{
    require(handle != nullptr, "D3D12 context is required");
    return *static_cast<Context*>(handle);
}

const Device& getDevice(void* handle, uint32_t index)
{
    const auto& devices = getContext(handle).devices;
    require(index < devices.size(), "D3D12 device index is out of range");
    return devices[index];
}

ga::D3D12Binding makeBuffer(const GaD3D12Location& location, bool writable, std::vector<uint32_t> words)
{
    return {writable ? D3D12_DESCRIPTOR_RANGE_TYPE_UAV : D3D12_DESCRIPTOR_RANGE_TYPE_SRV,
        location.space, location.index, 0, sizeof(uint32_t), std::move(words)};
}

void validateShader(std::span<const uint32_t> container, uint32_t workgroupX, const GaD3D12Location* locations)
{
    require(container.size() >= 8 && container[0] == 0x43425844u && container[5] == 1
        && container[6] == container.size_bytes() && container[7] <= container.size() - 8,
        "D3D12 artifact has an invalid DXIL container header");
    const auto offsets = container.subspan(8, container[7]);
    std::span<const uint32_t> psv;
    bool hasComputeShader = false;
    for (auto offset : offsets)
    {
        require(offset % 4 == 0 && offset / 4 >= 8 + offsets.size() && offset / 4 <= container.size() - 2,
            "D3D12 artifact has an invalid DXIL part offset");
        const auto words = container.subspan(offset / 4);
        const auto size = words[1];
        require(size <= (words.size() - 2) * sizeof(uint32_t), "D3D12 artifact has a truncated DXIL part");
        if (words[0] == 0x4c495844u) // DXIL
        {
            require(!hasComputeShader && size >= sizeof(uint32_t) && words[2] == 0x00050060u,
                "D3D12 artifact must contain one cs_6_0 DXIL program");
            hasComputeShader = true;
        }
        else if (words[0] == 0x30565350u) // PSV0
        {
            require(psv.empty() && size != 0 && size % 4 == 0, "D3D12 artifact has an invalid PSV0 part");
            psv = words.subspan(2, size / 4);
        }
    }

    // DXC 1.8.0.4806 emits PSVRuntimeInfo3 (52 bytes) and PSVResourceBindInfo1 (24 bytes).
    // Check the binary layout as well as the Slang reflection before creating the pipeline.
    // https://github.com/microsoft/DirectXShaderCompiler/blob/main/include/dxc/DxilContainer/DxilPipelineStateValidation.h
    constexpr uint32_t computeShaderKind = 5;
    constexpr size_t resourceStart = 16;
    constexpr size_t resourceStride = 6;
    constexpr size_t stringTableSize = resourceStart + 3 * resourceStride;
    require(hasComputeShader && psv.size() > stringTableSize && psv[0] == 52
        && (psv[7] & 0xffu) == computeShaderKind && psv[10] == workgroupX && psv[11] == 1 && psv[12] == 1
        && psv[14] == 3 && psv[15] == 24, "D3D12 artifact PSV0 has incompatible group size or resource count");
    const auto stringBytes = psv[stringTableSize];
    const auto entryOffset = psv[13];
    require(stringBytes <= (psv.size() - stringTableSize - 1) * sizeof(uint32_t)
        && entryOffset <= stringBytes && stringBytes - entryOffset >= 5,
        "D3D12 artifact PSV0 has an invalid entry name");
    const auto strings = reinterpret_cast<const char*>(psv.data() + stringTableSize + 1);
    require(std::string_view(strings + entryOffset, 5) == std::string_view("main\0", 5),
        "D3D12 artifact must use the main entry point");

    std::array<bool, 3> seen{};
    constexpr uint32_t structuredBufferKind = 12;
    for (size_t bindingIndex = 0; bindingIndex < seen.size(); ++bindingIndex)
    {
        const auto binding = psv.subspan(resourceStart + bindingIndex * resourceStride, resourceStride);
        require(binding[2] == binding[3] && binding[4] == structuredBufferKind && binding[5] == 0,
            "D3D12 artifact requires single structured resources without counters");
        bool found = false;
        for (size_t slot = 0; slot < seen.size(); ++slot)
        {
            constexpr uint32_t structuredSrv = 5, structuredUav = 8;
            if (!seen[slot] && binding[0] == (slot == 0 ? structuredSrv : structuredUav)
                && binding[1] == locations[slot].space && binding[2] == locations[slot].index)
            {
                seen[slot] = found = true;
                break;
            }
        }
        require(found, "D3D12 artifact PSV0 and reflected register locations differ");
    }
}
}

int32_t skecs_ga_d3d12_open(void** context, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(context != nullptr, "D3D12 context destination is required");
        *context = nullptr;
        auto handle = std::make_unique<Context>();
        handle->devices = enumerateDevices();
        *context = handle.release();
    });
}

void skecs_ga_d3d12_close(void* context)
{
    delete static_cast<Context*>(context);
}

int32_t skecs_ga_d3d12_device_count(void* context, uint32_t* count, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(count != nullptr, "D3D12 device count destination is required");
        *count = static_cast<uint32_t>(getContext(context).devices.size());
    });
}

int32_t skecs_ga_d3d12_get_device(void* context, uint32_t index, GaD3D12Device* device, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(device != nullptr, "D3D12 device destination is required");
        *device = getDevice(context, index).info;
    });
}

int32_t skecs_ga_d3d12_probe(void* context, uint32_t index, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        constexpr uint32_t sentinel = 0xBADC0DEu;
        constexpr std::array values{-7, std::numeric_limits<int32_t>::min(), 7, std::numeric_limits<int32_t>::max()};
        constexpr GaD3D12Location locations[]{{0, 0}, {0, 0}, {0, 1}};
        std::vector<uint32_t> input{static_cast<uint32_t>(values.size())};
        std::vector<uint32_t> expectedQuotients, expectedRemainders;
        for (auto value : values)
        {
            input.push_back(std::bit_cast<uint32_t>(value));
            expectedQuotients.push_back(std::bit_cast<uint32_t>(value / 3));
            expectedRemainders.push_back(std::bit_cast<uint32_t>(value % 3));
        }
        expectedQuotients.push_back(sentinel);
        expectedRemainders.push_back(sentinel);
        std::array buffers{
            makeBuffer(locations[0], false, std::move(input)),
            makeBuffer(locations[1], true, std::vector<uint32_t>(expectedQuotients.size(), sentinel)),
            makeBuffer(locations[2], true, std::vector<uint32_t>(expectedRemainders.size(), sentinel))};
        validateShader(startupDxil, 64, locations);
        ga::dispatchD3D12(getDevice(context, index).adapter.Get(), startupDxil, 1, buffers);
        require(buffers[1].words == expectedQuotients && buffers[2].words == expectedRemainders,
            "D3D12 startup probe readback differs from exact CPU arithmetic");
    });
}

int32_t skecs_ga_d3d12_create_kernel(void* context, uint32_t index, const uint32_t* artifact, uint32_t artifactWordCount,
    uint32_t workgroupX, const GaD3D12Location* locations, void** kernel, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel != nullptr, "D3D12 kernel destination is required");
        *kernel = nullptr;
        require(artifact != nullptr && locations != nullptr && workgroupX > 0 && workgroupX <= D3D12_CS_THREAD_GROUP_MAX_X,
            "D3D12 kernel artifact, group size and register locations are required");
        const std::span<const uint32_t> dxil{artifact, artifactWordCount};
        validateShader(dxil, workgroupX, locations);
        std::array<GaD3D12Location, 3> map{locations[0], locations[1], locations[2]};
        const std::array bindings{
            makeBuffer(map[0], false, {}), makeBuffer(map[1], true, {}), makeBuffer(map[2], true, {})};
        auto handle = new Kernel{map, {getDevice(context, index).adapter.Get(), dxil, bindings}};
        *kernel = handle;
    });
}

void skecs_ga_d3d12_release_kernel(void* kernel)
{
    delete static_cast<Kernel*>(kernel);
}

int32_t skecs_ga_d3d12_dispatch(void* kernel, uint32_t groupsX, const uint32_t* input, uint32_t inputLength,
    uint32_t* output, uint32_t outputLength, uint32_t* faults, uint32_t faultsLength, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel && input && output && faults && inputLength && outputLength && faultsLength,
            "D3D12 dispatch requires a kernel and three nonempty buffers");
        require(groupsX && groupsX <= D3D12_CS_DISPATCH_MAX_THREAD_GROUPS_PER_DIMENSION,
            "D3D12 compute dispatch group count is out of range");
        constexpr auto maxBufferWords = maxBufferBytes / sizeof(uint32_t);
        require(inputLength <= maxBufferWords && outputLength <= maxBufferWords && faultsLength <= maxBufferWords,
            "D3D12 storage buffer exceeds device limits");
        auto& state = *static_cast<Kernel*>(kernel);
        std::array bindings{
            makeBuffer(state.locations[0], false, {input, input + inputLength}),
            makeBuffer(state.locations[1], true, {output, output + outputLength}),
            makeBuffer(state.locations[2], true, {faults, faults + faultsLength})};
        state.native.execute(groupsX, bindings);
        std::ranges::copy(bindings[1].words, output);
        std::ranges::copy(bindings[2].words, faults);
    });
}
