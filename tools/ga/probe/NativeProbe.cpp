#include "GaVulkanNative.h"
#include "WordsFile.h"

#include <array>
#include <cstdint>
#include <filesystem>
#include <iomanip>
#include <iostream>
#include <limits>
#include <memory>
#include <optional>
#include <stdexcept>
#include <string_view>
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

bool testDevice(uint32_t index, const std::vector<uint32_t>& spirv, bool software)
{
    auto context = openContext();
    GaVulkanDevice info{};
    check(skecs_ga_vk_get_device(context.get(), index, &info, error, sizeof(error)));
    // VkPhysicalDeviceType values are carried by the C ABI without Vulkan headers.
    constexpr uint32_t integratedGpu = 1, discreteGpu = 2, cpu = 4;
    const bool selected = software ? info.deviceType == cpu
                                   : info.deviceType == integratedGpu || info.deviceType == discreteGpu;
    if (!selected)
        return false;
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
    for (uint32_t rowCount : {1u, 5u, 65u, 1u, 65u, 65u, 129u, 5u})
    {
        std::vector<uint32_t> input{rowCount};
        std::vector<uint32_t> quotient(rowCount + 1, sentinel), remainder(rowCount + 1, sentinel);
        for (uint32_t i = 0; i < rowCount; ++i)
        {
            constexpr std::array boundaries{-7, std::numeric_limits<int32_t>::min(), 7, std::numeric_limits<int32_t>::max()};
            input.push_back(static_cast<uint32_t>(boundaries[i % boundaries.size()]));
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
            const int32_t value = static_cast<int32_t>(input[i + 1]);
            if (static_cast<int32_t>(quotient[i]) != value / 3 || static_cast<int32_t>(remainder[i]) != value % 3)
                throw std::runtime_error("native persistent pipeline returned an incorrect row");
        }
        if (quotient.back() != sentinel || remainder.back() != sentinel)
            throw std::runtime_error("native persistent pipeline modified data beyond the logical rows");
    }
    std::cout << info.name << " native ABI startup and repeated dispatch passed"
        << " deviceType=" << (software ? "software" : "physical")
        << " deviceIndex=" << index << " vkDeviceType=" << info.deviceType
        << std::hex << std::setfill('0')
        << " pci=" << std::setw(4) << info.vendorId << ':' << std::setw(4) << info.deviceId
        << " uuid=";
    for (uint8_t byte : info.deviceUuid)
        std::cout << std::setw(2) << static_cast<unsigned>(byte);
    std::cout << " apiVersionRaw=0x" << std::setw(8) << info.apiVersion
        << " driverVersionRaw=0x" << std::setw(8) << info.driverVersion
        << std::dec << std::setfill(' ') << '\n';
    return true;
}

template<class Char>
uint32_t parseIndex(std::basic_string_view<Char> value)
{
    constexpr auto invalid = "device index must be an unsigned decimal uint32";
    if (value.empty())
        throw std::runtime_error(invalid);
    uint32_t result = 0;
    for (auto character : value)
    {
        if (character < '0' || character > '9')
            throw std::runtime_error(invalid);
        const auto digit = static_cast<uint32_t>(character - '0');
        if (result > (UINT32_MAX - digit) / 10)
            throw std::runtime_error(invalid);
        result = result * 10 + digit;
    }
    return result;
}

template<class Char>
int run(int argc, Char** argv, const Char* softwareFlag, const Char* indexFlag)
{
    try
    {
        constexpr auto usage = "usage: ga-native-probe <startup.spv> [--software] [--device-index N]";
        if (argc < 2)
            throw std::runtime_error(usage);
        bool software = false;
        std::optional<uint32_t> index;
        for (int i = 2; i < argc; ++i)
        {
            const std::basic_string_view<Char> argument(argv[i]);
            if (argument == softwareFlag && !software)
                software = true;
            else if (argument == indexFlag && !index && i + 1 < argc)
                index = parseIndex(std::basic_string_view<Char>(argv[++i]));
            else
                throw std::runtime_error(usage);
        }
        const auto spirv = ga::readWords(std::filesystem::path(argv[1]), UINT32_MAX);

        auto context = openContext();
        uint32_t count = 0;
        check(skecs_ga_vk_device_count(context.get(), &count, error, sizeof(error)));
        if (index && *index >= count)
            throw std::runtime_error("Vulkan device index is out of range");
        uint32_t tested = 0;
        if (index)
            tested = testDevice(*index, spirv, software);
        else
            for (uint32_t i = 0; i < count; ++i)
                tested += testDevice(i, spirv, software);
        if (tested == 0)
            throw std::runtime_error(software ? "no Vulkan software CPU device was tested" :
                "no Vulkan integrated or discrete physical GPU was tested");
        return 0;
    }
    catch (const std::exception& failure)
    {
        std::cerr << failure.what() << '\n';
        return 1;
    }
}
}

#if defined(_WIN32)
int wmain(int argc, wchar_t** argv)
{
    return run(argc, argv, L"--software", L"--device-index");
}
#else
int main(int argc, char** argv)
{
    return run(argc, argv, "--software", "--device-index");
}
#endif
