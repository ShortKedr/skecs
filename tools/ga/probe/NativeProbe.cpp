#include "GaVulkanNative.h"
#include "WordsFile.h"

#include <array>
#include <bit>
#include <cstdint>
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
    check(skecs_ga_vk_open(&context, error, sizeof(error)));
    return std::unique_ptr<void, decltype(&skecs_ga_vk_close)>(context, skecs_ga_vk_close);
}

void testDevice(uint32_t index, const std::vector<uint32_t>& spirv)
{
    auto context = openContext();
    GaVulkanDevice info{};
    check(skecs_ga_vk_get_device(context.get(), index, &info, error, sizeof(error)));
    if (info.deviceType != 1 && info.deviceType != 2)
        return;
    check(skecs_ga_vk_probe(context.get(), index, error, sizeof(error)));

    const GaVulkanLocation locations[]{{0, 3}, {0, 5}, {0, 7}};
    const GaVulkanLocation invalid[]{{0, 3}, {0, 3}, {0, 7}};
    void* kernel = nullptr;
    auto create = [&](const GaVulkanLocation* map)
    {
        return skecs_ga_vk_create_kernel(context.get(), index, spirv.data(), static_cast<uint32_t>(spirv.size()),
            "main", 64, map, &kernel, error, sizeof(error));
    };
    if (create(invalid) == 0 || kernel)
        throw std::runtime_error("invalid descriptor locations were accepted");
    check(create(locations));
    std::unique_ptr<void, decltype(&skecs_ga_vk_release_kernel)> handle(kernel, skecs_ga_vk_release_kernel);
    context.reset();

    constexpr uint32_t sentinel = 0xBADC0DEu;
    for (uint32_t rowCount : {1u, 5u, 65u})
    {
        std::vector<uint32_t> input{rowCount};
        std::vector<uint32_t> quotient(rowCount + 1, sentinel), remainder(rowCount + 1, sentinel);
        for (uint32_t i = 0; i < rowCount; ++i)
        {
            constexpr std::array boundaries{-7, std::numeric_limits<int32_t>::min(), 7, std::numeric_limits<int32_t>::max()};
            input.push_back(std::bit_cast<uint32_t>(boundaries[i % boundaries.size()]));
        }
        auto dispatch = [&](uint32_t groups)
        {
            return skecs_ga_vk_dispatch(handle.get(), groups, input.data(), static_cast<uint32_t>(input.size()),
                quotient.data(), static_cast<uint32_t>(quotient.size()), remainder.data(), static_cast<uint32_t>(remainder.size()),
                error, sizeof(error));
        };
        if (dispatch(0) == 0 || quotient[0] != sentinel || remainder[0] != sentinel)
            throw std::runtime_error("invalid dispatch count changed the results");
        check(dispatch((rowCount + 63) / 64));
        for (uint32_t i = 0; i < rowCount; ++i)
        {
            const int32_t value = std::bit_cast<int32_t>(input[i + 1]);
            if (std::bit_cast<int32_t>(quotient[i]) != value / 3 || std::bit_cast<int32_t>(remainder[i]) != value % 3)
                throw std::runtime_error("native persistent pipeline returned an incorrect row");
        }
        if (quotient.back() != sentinel || remainder.back() != sentinel)
            throw std::runtime_error("native persistent pipeline modified data beyond the logical rows");
    }
    std::cout << info.name << " native ABI startup and repeated dispatch passed\n";
}
}

int wmain(int argc, wchar_t** argv)
{
    try
    {
        if (argc != 2)
            throw std::runtime_error("usage: ga-native-probe <startup.spv>");
        const auto spirv = ga::readWords(argv[1], UINT32_MAX);

        auto context = openContext();
        uint32_t count = 0;
        check(skecs_ga_vk_device_count(context.get(), &count, error, sizeof(error)));
        for (uint32_t i = 0; i < count; ++i)
            testDevice(i, spirv);
    }
    catch (const std::exception& failure)
    {
        std::cerr << failure.what() << '\n';
        return 1;
    }
}
