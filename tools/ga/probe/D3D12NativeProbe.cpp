#include "GaD3D12Native.h"
#include "WordsFile.h"

#include <array>
#include <bit>
#include <cstring>
#include <iostream>
#include <limits>
#include <memory>
#include <stdexcept>
#include <vector>

namespace
{
char error[512];

void check(int32_t status)
{
    if (status)
        throw std::runtime_error(error);
}

auto openContext()
{
    void* context = nullptr;
    check(skecs_ga_d3d12_open(&context, error, sizeof(error)));
    return std::unique_ptr<void, decltype(&skecs_ga_d3d12_close)>(context, skecs_ga_d3d12_close);
}

void testDevice(uint32_t index, const std::vector<uint32_t>& dxil)
{
    auto context = openContext();
    GaD3D12Device info{};
    check(skecs_ga_d3d12_get_device(context.get(), index, &info, error, sizeof(error)));
    check(skecs_ga_d3d12_probe(context.get(), index, error, sizeof(error)));

    constexpr GaD3D12Location locations[]{{0, 0}, {0, 0}, {0, 1}};
    constexpr GaD3D12Location duplicateUav[]{{0, 0}, {0, 0}, {0, 0}};
    constexpr GaD3D12Location wrongSrvSpace[]{{1, 0}, {0, 0}, {0, 1}};
    void* kernel = nullptr;
    auto create = [&](const GaD3D12Location* map, uint32_t groupSize)
    {
        return skecs_ga_d3d12_create_kernel(context.get(), index, dxil.data(), static_cast<uint32_t>(dxil.size()),
            groupSize, map, &kernel, error, sizeof(error));
    };
    if (create(duplicateUav, 64) == 0 || kernel || create(wrongSrvSpace, 64) == 0 || kernel ||
        create(locations, 32) == 0 || kernel)
        throw std::runtime_error("D3D12 accepted metadata that disagreed with the shader");

    check(create(locations, 64));
    std::unique_ptr<void, decltype(&skecs_ga_d3d12_release_kernel)> handle(kernel, skecs_ga_d3d12_release_kernel);
    context.reset();

    constexpr uint32_t sentinel = 0xBADC0DEu;
    constexpr std::array boundaries{-7, std::numeric_limits<int32_t>::min(), 7, std::numeric_limits<int32_t>::max()};
    for (uint32_t rowCount : {1u, 5u, 65u})
    {
        std::vector<uint32_t> input{rowCount};
        std::vector<uint32_t> quotient(rowCount + 1, sentinel), remainder(rowCount + 1, sentinel);
        for (uint32_t row = 0; row < rowCount; ++row)
            input.push_back(std::bit_cast<uint32_t>(boundaries[row % boundaries.size()]));

        auto dispatch = [&](uint32_t groups)
        {
            return skecs_ga_d3d12_dispatch(handle.get(), groups, input.data(), static_cast<uint32_t>(input.size()),
                quotient.data(), static_cast<uint32_t>(quotient.size()), remainder.data(), static_cast<uint32_t>(remainder.size()),
                error, sizeof(error));
        };
        if (dispatch(0) == 0 || quotient.front() != sentinel || remainder.front() != sentinel)
            throw std::runtime_error("invalid D3D12 dispatch changed the results");
        check(dispatch((rowCount + 63) / 64));
        for (uint32_t row = 0; row < rowCount; ++row)
        {
            const int32_t value = std::bit_cast<int32_t>(input[row + 1]);
            if (std::bit_cast<int32_t>(quotient[row]) != value / 3 || std::bit_cast<int32_t>(remainder[row]) != value % 3)
                throw std::runtime_error("native persistent D3D12 pipeline returned an incorrect row");
        }
        if (quotient.back() != sentinel || remainder.back() != sentinel)
            throw std::runtime_error("native persistent D3D12 pipeline modified data beyond the logical rows");
    }
    uint32_t luidWords[2]{};
    std::memcpy(luidWords, info.deviceLuid, sizeof(luidWords));
    std::cout << info.name << " (D3D12 LUID " << std::hex << luidWords[1] << ':' << luidWords[0]
        << " driver " << info.driverVersionHigh << ':' << info.driverVersionLow << std::dec
        << ") native ABI startup and persistent dispatch passed\n";
}
}

int wmain(int argc, wchar_t** argv)
{
    try
    {
        if (argc != 2)
            throw std::runtime_error("usage: ga-d3d12-native-probe <startup.dxil>");
        const auto dxil = ga::readWords(argv[1], UINT32_MAX);
        auto context = openContext();
        uint32_t count = 0;
        check(skecs_ga_d3d12_device_count(context.get(), &count, error, sizeof(error)));
        if (count == 0)
            throw std::runtime_error("No physical D3D12 device supports the required compute profile");
        for (uint32_t index = 0; index < count; ++index)
            testDevice(index, dxil);
    }
    catch (const std::exception& failure)
    {
        std::cerr << failure.what() << '\n';
        return 1;
    }
}
