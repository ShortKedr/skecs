#include "GaD3D11Native.h"

#include <d3d11.h>
#include <d3d11shader.h>
#include <d3dcompiler.h>
#include <dxgi.h>
#include <wrl/client.h>

#include <algorithm>
#include <array>
#include <bit>
#include <chrono>
#include <cstdio>
#include <cstring>
#include <limits>
#include <memory>
#include <mutex>
#include <span>
#include <stdexcept>
#include <thread>
#include <vector>

namespace
{
#include "StartupDxbc.h"

using Microsoft::WRL::ComPtr;
static_assert(sizeof(GaD3D11Device) == 304);
static_assert(sizeof(GaD3D11Location) == 8);
static_assert(sizeof(LUID) == 8);

constexpr uint32_t maxBufferBytes = D3D11_REQ_RESOURCE_SIZE_IN_MEGABYTES_EXPRESSION_A_TERM * 1024u * 1024u;
using Clock = std::chrono::steady_clock;
constexpr auto dispatchTimeout = std::chrono::seconds(30);

void require(bool condition, const char* text)
{
    if (!condition)
        throw std::runtime_error(text);
}

void check(HRESULT status, const char* step)
{
    if (FAILED(status))
    {
        char text[160];
        std::snprintf(text, sizeof(text), "%s failed (HRESULT 0x%08lx)", step, static_cast<unsigned long>(status));
        throw std::runtime_error(text);
    }
}

void writeError(char* error, uint32_t capacity, const char* text)
{
    if (!error || !capacity)
        return;
    const auto length = std::min(static_cast<size_t>(capacity - 1), std::strlen(text));
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
        writeError(error, errorCapacity, "unrecognized native D3D11 failure");
    }
    return 1;
}

HRESULT createDevice(IDXGIAdapter1* adapter, ID3D11Device** device, ID3D11DeviceContext** immediate)
{
    constexpr auto featureLevel = D3D_FEATURE_LEVEL_11_0;
    return D3D11CreateDevice(adapter, D3D_DRIVER_TYPE_UNKNOWN, nullptr, 0, &featureLevel, 1,
        D3D11_SDK_VERSION, device, nullptr, immediate);
}

struct Device
{
    ComPtr<IDXGIAdapter1> adapter;
    GaD3D11Device info;
};

struct Context
{
    std::vector<Device> devices;
};

std::vector<Device> enumerateDevices()
{
    ComPtr<IDXGIFactory1> factory;
    check(CreateDXGIFactory1(IID_PPV_ARGS(&factory)), "CreateDXGIFactory1");
    std::vector<Device> devices;
    for (UINT index = 0;; ++index)
    {
        ComPtr<IDXGIAdapter1> adapter;
        const auto status = factory->EnumAdapters1(index, &adapter);
        if (status == DXGI_ERROR_NOT_FOUND)
            break;
        check(status, "EnumAdapters1");
        DXGI_ADAPTER_DESC1 desc{};
        check(adapter->GetDesc1(&desc), "DXGI adapter description");
        if (desc.Flags & (DXGI_ADAPTER_FLAG_SOFTWARE | DXGI_ADAPTER_FLAG_REMOTE))
            continue;

        ComPtr<ID3D11Device> device;
        if (FAILED(createDevice(adapter.Get(), &device, nullptr)))
            continue;

        uint32_t deviceType = 0;
        D3D11_FEATURE_DATA_D3D11_OPTIONS2 architecture{};
        if (SUCCEEDED(device->CheckFeatureSupport(D3D11_FEATURE_D3D11_OPTIONS2, &architecture, sizeof(architecture))))
            deviceType = architecture.UnifiedMemoryArchitecture ? 1u : 2u;
        LARGE_INTEGER driver{};
        if (FAILED(adapter->CheckInterfaceSupport(__uuidof(IDXGIDevice), &driver)))
            driver.QuadPart = 0;

        GaD3D11Device info{desc.VendorId, desc.DeviceId, 0x50u, driver.LowPart,
            static_cast<uint32_t>(driver.HighPart), deviceType, D3D11_CS_DISPATCH_MAX_THREAD_GROUPS_PER_DIMENSION,
            D3D11_CS_THREAD_GROUP_MAX_X, D3D11_CS_THREAD_GROUP_MAX_THREADS_PER_GROUP, maxBufferBytes, {}, {}};
        std::array<char, 4 * std::size(desc.Description)> utf8{};
        const int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, desc.Description, -1,
            utf8.data(), static_cast<int>(utf8.size()), nullptr, nullptr);
        require(length > 0, "DXGI device name conversion failed");
        auto copy = std::min(static_cast<size_t>(length - 1), sizeof(info.name) - 1);
        while ((static_cast<unsigned char>(utf8[copy]) & 0xc0u) == 0x80u)
            --copy;
        std::memcpy(info.name, utf8.data(), copy);
        std::memcpy(info.deviceLuid, &desc.AdapterLuid, sizeof(info.deviceLuid));
        devices.push_back({std::move(adapter), info});
    }
    return devices;
}

const Context& getContext(void* handle)
{
    require(handle != nullptr, "D3D11 context is required");
    return *static_cast<Context*>(handle);
}

const Device& getDevice(void* handle, uint32_t index)
{
    const auto& devices = getContext(handle).devices;
    require(index < devices.size(), "D3D11 device index is out of range");
    return devices[index];
}

using Locations = std::array<GaD3D11Location, 3>;

void validateShader(std::span<const uint32_t> artifact, uint32_t workgroupX, const Locations& locations)
{
    require(artifact.size() >= 8 && artifact[0] == 0x43425844u && artifact[5] == 1
        && artifact[6] == artifact.size_bytes() && artifact[7] <= artifact.size() - 8,
        "D3D11 artifact has an invalid DXBC container header");
    require(locations[0].space == 0 && locations[1].space == 0 && locations[2].space == 0
        && locations[0].index < D3D11_COMMONSHADER_INPUT_RESOURCE_SLOT_COUNT
        && locations[1].index < D3D11_PS_CS_UAV_REGISTER_COUNT && locations[2].index < D3D11_PS_CS_UAV_REGISTER_COUNT
        && locations[1].index != locations[2].index, "D3D11 shader register locations are out of range");

    ComPtr<ID3D11ShaderReflection> reflection;
    check(D3DReflect(artifact.data(), artifact.size_bytes(), IID_PPV_ARGS(&reflection)), "D3DReflect");
    D3D11_SHADER_DESC shader{};
    check(reflection->GetDesc(&shader), "D3D11 shader reflection description");
    require(D3D11_SHVER_GET_TYPE(shader.Version) == D3D11_SHVER_COMPUTE_SHADER
        && D3D11_SHVER_GET_MAJOR(shader.Version) == 5 && D3D11_SHVER_GET_MINOR(shader.Version) == 0,
        "D3D11 artifact must contain a cs_5_0 program");
    UINT x = 0, y = 0, z = 0;
    reflection->GetThreadGroupSize(&x, &y, &z);
    require(x == workgroupX && y == 1 && z == 1 && shader.BoundResources == locations.size(),
        "D3D11 artifact has an incompatible compute group size or resource count");

    std::array<bool, 3> seen{};
    for (UINT i = 0; i < shader.BoundResources; ++i)
    {
        D3D11_SHADER_INPUT_BIND_DESC binding{};
        check(reflection->GetResourceBindingDesc(i, &binding), "D3D11 resource reflection");
        require(binding.BindCount == 1 && binding.Dimension == D3D_SRV_DIMENSION_BUFFER
            && binding.NumSamples == sizeof(uint32_t), "D3D11 shader requires single four-byte structured buffers");
        bool matched = false;
        for (size_t slot = 0; slot < seen.size(); ++slot)
        {
            const auto type = slot == 0 ? D3D_SIT_STRUCTURED : D3D_SIT_UAV_RWSTRUCTURED;
            if (!seen[slot] && binding.Type == type && binding.BindPoint == locations[slot].index)
            {
                seen[slot] = matched = true;
                break;
            }
        }
        require(matched, "D3D11 DXBC and reflected register locations differ");
    }
}

ComPtr<ID3D11Buffer> createBuffer(ID3D11Device* device, uint32_t words, UINT bind)
{
    D3D11_BUFFER_DESC description{};
    description.ByteWidth = static_cast<UINT>(words * sizeof(uint32_t));
    description.BindFlags = bind;
    if (bind)
    {
        description.Usage = D3D11_USAGE_DEFAULT;
        description.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED;
        description.StructureByteStride = sizeof(uint32_t);
    }
    else
    {
        description.Usage = D3D11_USAGE_STAGING;
        description.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
    }
    ComPtr<ID3D11Buffer> buffer;
    check(device->CreateBuffer(&description, nullptr, &buffer), "D3D11 CreateBuffer");
    return buffer;
}

struct Buffers
{
    std::array<uint32_t, 3> capacities{}, viewLengths{};
    ComPtr<ID3D11Buffer> input;
    ComPtr<ID3D11ShaderResourceView> srv;
    std::array<ComPtr<ID3D11Buffer>, 2> output, readback;
    std::array<ComPtr<ID3D11UnorderedAccessView>, 2> uav;

    void prepare(ID3D11Device* device, const std::array<uint32_t, 3>& words)
    {
        // Shader GetDimensions must see the live word count, even when a buffer retains its larger allocation.
        if (viewLengths[0] != words[0])
        {
            const bool grow = capacities[0] < words[0];
            auto buffer = grow ? createBuffer(device, words[0], D3D11_BIND_SHADER_RESOURCE) : input;
            D3D11_SHADER_RESOURCE_VIEW_DESC description{};
            description.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
            description.Buffer.NumElements = words[0];
            ComPtr<ID3D11ShaderResourceView> view;
            check(device->CreateShaderResourceView(buffer.Get(), &description, &view), "D3D11 CreateShaderResourceView");
            input = std::move(buffer);
            srv = std::move(view);
            capacities[0] = std::max(capacities[0], words[0]);
            viewLengths[0] = words[0];
        }

        for (size_t i = 0; i < output.size(); ++i)
        {
            const auto length = words[i + 1];
            if (viewLengths[i + 1] == length)
                continue;
            const bool grow = capacities[i + 1] < length;
            auto buffer = grow ? createBuffer(device, length, D3D11_BIND_UNORDERED_ACCESS) : output[i];
            auto staging = grow ? createBuffer(device, length, 0) : readback[i];
            D3D11_UNORDERED_ACCESS_VIEW_DESC description{};
            description.ViewDimension = D3D11_UAV_DIMENSION_BUFFER;
            description.Buffer.NumElements = length;
            ComPtr<ID3D11UnorderedAccessView> view;
            check(device->CreateUnorderedAccessView(buffer.Get(), &description, &view), "D3D11 CreateUnorderedAccessView");
            output[i] = std::move(buffer);
            readback[i] = std::move(staging);
            uav[i] = std::move(view);
            capacities[i + 1] = std::max(capacities[i + 1], length);
            viewLengths[i + 1] = length;
        }
    }
};

D3D11_BOX bufferRange(uint32_t words)
{
    return {0, 0, 0, static_cast<UINT>(words * sizeof(uint32_t)), 1, 1};
}

class MappedReadback
{
    ID3D11DeviceContext* context;
    ID3D11Buffer* buffer;
    D3D11_MAPPED_SUBRESOURCE mapping{};

public:
    MappedReadback(ID3D11Device* device, ID3D11DeviceContext* immediate, ID3D11Buffer* staging,
        Clock::time_point deadline) : context(immediate), buffer(staging)
    {
        for (;;)
        {
            const auto status = context->Map(buffer, 0, D3D11_MAP_READ, D3D11_MAP_FLAG_DO_NOT_WAIT, &mapping);
            if (status != DXGI_ERROR_WAS_STILL_DRAWING)
            {
                check(status, "D3D11 Map readback");
                return;
            }
            check(device->GetDeviceRemovedReason(), "D3D11 device execution");
            require(Clock::now() < deadline, "D3D11 readback timed out");
            std::this_thread::yield();
        }
    }

    ~MappedReadback() { context->Unmap(buffer, 0); }
    MappedReadback(const MappedReadback&) = delete;
    MappedReadback& operator=(const MappedReadback&) = delete;
    const void* data() const { return mapping.pData; }
};

class Kernel
{
    Locations locations;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> immediate;
    ComPtr<ID3D11ComputeShader> shader;
    ComPtr<ID3D11Query> completion;
    Buffers buffers;
    std::mutex dispatchMutex;
    bool unavailable = false;

    void wait(Clock::time_point deadline)
    {
        immediate->End(completion.Get());
        immediate->Flush();
        while (true)
        {
            BOOL complete = FALSE;
            const auto status = immediate->GetData(completion.Get(), &complete, sizeof(complete), D3D11_ASYNC_GETDATA_DONOTFLUSH);
            check(status, "D3D11 GetData dispatch completion");
            if (status == S_OK && complete)
                return;
            check(device->GetDeviceRemovedReason(), "D3D11 device execution");
            require(Clock::now() < deadline, "D3D11 dispatch completion timed out");
            std::this_thread::yield();
        }
    }

public:
    Kernel(IDXGIAdapter1* adapter, std::span<const uint32_t> artifact, uint32_t workgroupX, Locations map)
        : locations(map)
    {
        validateShader(artifact, workgroupX, locations);
        check(createDevice(adapter, &device, &immediate), "D3D11CreateDevice FL11_0");
        check(device->CreateComputeShader(artifact.data(), artifact.size_bytes(), nullptr, &shader),
            "D3D11 CreateComputeShader");
        const D3D11_QUERY_DESC query{D3D11_QUERY_EVENT, 0};
        check(device->CreateQuery(&query, &completion), "D3D11 CreateQuery completion");
        immediate->CSSetShader(shader.Get(), nullptr, 0);
    }

    void dispatch(uint32_t groupsX, std::span<const uint32_t> input, std::span<uint32_t> output,
        std::span<uint32_t> faults)
    {
        std::lock_guard lock(dispatchMutex);
        require(!unavailable, "D3D11 kernel has a failed dispatch");
        const std::array lengths{static_cast<uint32_t>(input.size()), static_cast<uint32_t>(output.size()),
            static_cast<uint32_t>(faults.size())};
        buffers.prepare(device.Get(), lengths);
        const auto inputRange = bufferRange(lengths[0]);
        const std::array outputRanges{bufferRange(lengths[1]), bufferRange(lengths[2])};
        immediate->UpdateSubresource(buffers.input.Get(), 0, &inputRange, input.data(), 0, 0);
        immediate->UpdateSubresource(buffers.output[0].Get(), 0, &outputRanges[0], output.data(), 0, 0);
        immediate->UpdateSubresource(buffers.output[1].Get(), 0, &outputRanges[1], faults.data(), 0, 0);

        ID3D11ShaderResourceView* readable = buffers.srv.Get();
        std::array<ID3D11UnorderedAccessView*, D3D11_PS_CS_UAV_REGISTER_COUNT> writable{};
        writable[locations[1].index] = buffers.uav[0].Get();
        writable[locations[2].index] = buffers.uav[1].Get();
        immediate->CSSetShaderResources(locations[0].index, 1, &readable);
        immediate->CSSetUnorderedAccessViews(0, static_cast<UINT>(writable.size()), writable.data(), nullptr);
        immediate->Dispatch(groupsX, 1, 1);
        readable = nullptr;
        writable.fill(nullptr);
        immediate->CSSetShaderResources(locations[0].index, 1, &readable);
        immediate->CSSetUnorderedAccessViews(0, static_cast<UINT>(writable.size()), writable.data(), nullptr);

        for (size_t i = 0; i < buffers.output.size(); ++i)
            immediate->CopySubresourceRegion(buffers.readback[i].Get(), 0, 0, 0, 0, buffers.output[i].Get(), 0, &outputRanges[i]);

        unavailable = true;
        const auto deadline = Clock::now() + dispatchTimeout;
        wait(deadline);
        MappedReadback mappedOutput(device.Get(), immediate.Get(), buffers.readback[0].Get(), deadline);
        MappedReadback mappedFaults(device.Get(), immediate.Get(), buffers.readback[1].Get(), deadline);
        check(device->GetDeviceRemovedReason(), "D3D11 device execution");
        std::memcpy(output.data(), mappedOutput.data(), output.size_bytes());
        std::memcpy(faults.data(), mappedFaults.data(), faults.size_bytes());
        unavailable = false;
    }
};
}

int32_t skecs_ga_d3d11_open(void** context, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(context != nullptr, "D3D11 context destination is required");
        *context = nullptr;
        auto handle = std::make_unique<Context>();
        handle->devices = enumerateDevices();
        *context = handle.release();
    });
}

void skecs_ga_d3d11_close(void* context)
{
    delete static_cast<Context*>(context);
}

int32_t skecs_ga_d3d11_device_count(void* context, uint32_t* count, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(count != nullptr, "D3D11 device count destination is required");
        *count = static_cast<uint32_t>(getContext(context).devices.size());
    });
}

int32_t skecs_ga_d3d11_get_device(void* context, uint32_t index, GaD3D11Device* device, char* error,
    uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(device != nullptr, "D3D11 device destination is required");
        *device = getDevice(context, index).info;
    });
}

int32_t skecs_ga_d3d11_probe(void* context, uint32_t index, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        constexpr uint32_t sentinel = 0xBADC0DEu;
        constexpr std::array values{-7, std::numeric_limits<int32_t>::min(), 7, std::numeric_limits<int32_t>::max()};
        std::array<uint32_t, values.size() + 1> input{static_cast<uint32_t>(values.size())};
        std::array<uint32_t, values.size() + 1> quotients, remainders;
        std::array<uint32_t, values.size() + 1> expectedQuotients{}, expectedRemainders{};
        quotients.fill(sentinel);
        remainders.fill(sentinel);
        for (size_t i = 0; i < values.size(); ++i)
        {
            input[i + 1] = std::bit_cast<uint32_t>(values[i]);
            expectedQuotients[i] = std::bit_cast<uint32_t>(values[i] / 3);
            expectedRemainders[i] = std::bit_cast<uint32_t>(values[i] % 3);
        }
        expectedQuotients.back() = sentinel;
        expectedRemainders.back() = sentinel;

        Kernel kernel(getDevice(context, index).adapter.Get(), startupDxbc, 64, {{{0, 0}, {0, 0}, {0, 1}}});
        kernel.dispatch(1, input, quotients, remainders);
        require(quotients == expectedQuotients && remainders == expectedRemainders,
            "D3D11 startup probe readback differs from exact CPU arithmetic");
    });
}

int32_t skecs_ga_d3d11_create_kernel(void* context, uint32_t index, const uint32_t* artifact, uint32_t artifactWordCount,
    uint32_t workgroupX, const GaD3D11Location* locations, void** kernel, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel != nullptr, "D3D11 kernel destination is required");
        *kernel = nullptr;
        require(artifact != nullptr && artifactWordCount <= maxBufferBytes / sizeof(uint32_t) && locations != nullptr
            && workgroupX > 0 && workgroupX <= D3D11_CS_THREAD_GROUP_MAX_X,
            "D3D11 kernel artifact, group size and register locations are required");
        const Locations map{locations[0], locations[1], locations[2]};
        *kernel = new Kernel(getDevice(context, index).adapter.Get(), {artifact, artifactWordCount}, workgroupX, map);
    });
}

void skecs_ga_d3d11_release_kernel(void* kernel)
{
    delete static_cast<Kernel*>(kernel);
}

int32_t skecs_ga_d3d11_dispatch(void* kernel, uint32_t groupsX, const uint32_t* input, uint32_t inputLength,
    uint32_t* output, uint32_t outputLength, uint32_t* faults, uint32_t faultsLength, char* error, uint32_t errorCapacity)
{
    return perform(error, errorCapacity, [&]
    {
        require(kernel && input && output && faults && inputLength && outputLength && faultsLength,
            "D3D11 dispatch requires a kernel and three nonempty buffers");
        require(groupsX && groupsX <= D3D11_CS_DISPATCH_MAX_THREAD_GROUPS_PER_DIMENSION,
            "D3D11 compute dispatch group count is out of range");
        constexpr auto maxBufferWords = maxBufferBytes / sizeof(uint32_t);
        require(inputLength <= maxBufferWords && outputLength <= maxBufferWords && faultsLength <= maxBufferWords,
            "D3D11 storage buffer exceeds device limits");
        static_cast<Kernel*>(kernel)->dispatch(groupsX, {input, inputLength}, {output, outputLength}, {faults, faultsLength});
    });
}
