#include "VulkanDispatch.h"
#include "WordsFile.h"

#include <algorithm>
#include <iostream>
#include <limits>
#include <stdexcept>
#include <string_view>

namespace
{
uint32_t number(std::wstring_view text)
{
    if (text.empty())
        throw std::runtime_error("expected an unsigned integer");
    uint32_t value = 0;
    for (wchar_t digit : text)
    {
        if (digit < L'0' || digit > L'9' || value > (UINT32_MAX - (digit - L'0')) / 10)
            throw std::runtime_error("expected an unsigned 32-bit integer");
        value = value * 10 + (digit - L'0');
    }
    return value;
}

void listDevices(std::span<const VkPhysicalDevice> devices)
{
    for (size_t index = 0; index < devices.size(); ++index)
    {
        VkPhysicalDeviceProperties info;
        vkGetPhysicalDeviceProperties(devices[index], &info);
        std::cout << index << '\t' << info.vendorID << '\t' << info.deviceID << '\t'
            << VK_API_VERSION_MAJOR(info.apiVersion) << '.' << VK_API_VERSION_MINOR(info.apiVersion) << '.'
            << VK_API_VERSION_PATCH(info.apiVersion) << '\t' << info.driverVersion << '\t' << info.deviceType << '\t'
            << info.deviceName << '\n';
    }
}

void run(std::span<const VkPhysicalDevice> devices, std::span<wchar_t*> args)
{
    const uint32_t deviceIndex = number(args[0]);
    if (deviceIndex >= devices.size())
        throw std::runtime_error("Vulkan device index is out of range");
    auto device = devices[deviceIndex];
    VkPhysicalDeviceProperties info;
    vkGetPhysicalDeviceProperties(device, &info);
    const auto spirv = ga::readWords(args[1], UINT32_MAX);
    std::wstring_view entryArg(args[2]);
    if (entryArg.empty() || !std::ranges::all_of(entryArg, [](wchar_t c) { return c > 0 && c < 128; }))
        throw std::runtime_error("ASCII entry point required");
    std::string entry(entryArg.size(), '\0');
    std::ranges::transform(entryArg, entry.begin(), [](wchar_t c) { return static_cast<char>(c); });
    const uint32_t groups = number(args[3]);
    std::vector<ga::BufferBinding> bindings;
    std::vector<std::filesystem::path> outputPaths;
    for (size_t index = 4; index < args.size(); index += 4)
    {
        const uint32_t set = number(args[index]);
        const uint32_t binding = number(args[index + 1]);
        const std::wstring_view mode(args[index + 2]);
        if (mode != L"r" && mode != L"rw")
            throw std::runtime_error("binding access must be r or rw");
        std::filesystem::path path(args[index + 3]);
        const bool readback = mode == L"rw";
        bindings.push_back({set, binding, 0, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, readback,
            ga::readWords(path, info.limits.maxStorageBufferRange)});
        outputPaths.push_back(readback ? std::move(path) : std::filesystem::path{});
    }

    ga::dispatch(device, spirv, entry.c_str(), 64, groups, {}, bindings);
    for (size_t index = 0; index < bindings.size(); ++index)
    {
        if (bindings[index].readback)
            ga::writeWords(outputPaths[index], bindings[index].words);
    }
}
}

int wmain(int argc, wchar_t** argv)
{
    try
    {
        const bool enumerate = argc == 2 && std::wstring_view(argv[1]) == L"devices";
        const bool dispatch = argc >= 10 && (argc - 6) % 4 == 0 && std::wstring_view(argv[1]) == L"dispatch";
        if (!enumerate && !dispatch)
            throw std::runtime_error("usage: ga-vulkan devices | dispatch <deviceIndex> <shader.spv> <entry> <groupsX> (<set> <binding> <r|rw> <file>)+");
        ga::VulkanInstance instance;
        const auto devices = instance.devices();
        if (enumerate)
            listDevices(devices);
        else
            run(devices, {argv + 2, static_cast<size_t>(argc - 2)});
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
