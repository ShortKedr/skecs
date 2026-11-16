#pragma once

#include <cstdint>
#include <filesystem>
#include <fstream>
#include <span>
#include <stdexcept>
#include <vector>

namespace ga
{
inline std::vector<uint32_t> readWords(const std::filesystem::path& path, uint64_t limit)
{
    std::ifstream stream(path, std::ios::binary | std::ios::ate);
    const auto size = stream.tellg();
    if (size <= 0 || size % sizeof(uint32_t) != 0 || static_cast<uint64_t>(size) > limit)
        throw std::runtime_error("input file must contain a nonempty UInt32 array within the allowed size");
    std::vector<uint32_t> words(static_cast<size_t>(size) / sizeof(uint32_t));
    stream.seekg(0);
    stream.read(reinterpret_cast<char*>(words.data()), size);
    if (!stream)
        throw std::runtime_error("could not read input file");
    return words;
}

inline void writeWords(const std::filesystem::path& path, std::span<const uint32_t> words)
{
    std::ofstream stream(path, std::ios::binary | std::ios::trunc);
    stream.write(reinterpret_cast<const char*>(words.data()), words.size_bytes());
    stream.close();
    if (!stream)
        throw std::runtime_error("could not write UInt32 array file");
}
}
