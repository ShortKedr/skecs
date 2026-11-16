#include "VulkanDispatch.h"
#include "WordsFile.h"

#include <algorithm>
#include <array>
#include <bit>
#include <charconv>
#include <filesystem>
#include <fstream>
#include <iostream>
#include <limits>
#include <optional>
#include <stdexcept>
#include <string_view>
#if __has_include(<slang/slang.h>)
#include <slang/slang-com-ptr.h>
#include <slang/slang.h>
#else
#include <slang-com-ptr.h>
#include <slang.h>
#endif

#if defined(_WIN32) && !defined(SKECS_GA_PROBE_VULKAN_ONLY)
#include "D3D12Dispatch.h"
#include <d3d11shader.h>
#include <d3dcompiler.h>
#include <memory>
#include <type_traits>
#include <wrl/client.h>
#define SKECS_GA_PROBE_D3D12 1
#else
#define SKECS_GA_PROBE_D3D12 0
#endif

namespace
{
using Slang::ComPtr;
using slang::ParameterCategory;

enum class Target { Vulkan, D3D12, D3D11 };

void require(bool condition, const char* message)
{
    if (!condition)
        throw std::runtime_error(message);
}

void checkSlang(SlangResult result, slang::IBlob* diagnostics, const char* message)
{
    if (diagnostics && diagnostics->getBufferSize())
        std::cerr.write(static_cast<const char*>(diagnostics->getBufferPointer()), diagnostics->getBufferSize());
    require(SLANG_SUCCEEDED(result), message);
}

#if SKECS_GA_PROBE_D3D12
using FxcLibrary = std::unique_ptr<std::remove_pointer_t<HMODULE>, decltype(&FreeLibrary)>;

FxcLibrary loadFxc(const char* directory)
{
    auto path = std::filesystem::absolute(directory) / L"d3dcompiler_47.dll";
    FxcLibrary library(LoadLibraryExW(path.c_str(), nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32),
        &FreeLibrary);
    require(library != nullptr, "could not load d3dcompiler_47.dll from the FXC directory");
    return library;
}

template<typename T>
T fxcExport(const FxcLibrary& library, const char* name)
{
    auto entry = std::bit_cast<T>(GetProcAddress(library.get(), name));
    require(entry != nullptr, "required FXC export missing");
    return entry;
}

std::vector<uint32_t> compileDxbc(slang::IBlob* hlsl, const char* entryName, const char* directory)
{
    auto library = loadFxc(directory);
    auto compile = fxcExport<decltype(&D3DCompile)>(library, "D3DCompile");
    Microsoft::WRL::ComPtr<ID3DBlob> shader, diagnostics;
    // The optimizing FXC paths fail on generated control flow with structured UAV writes.
    const auto result = compile(hlsl->getBufferPointer(), hlsl->getBufferSize(), "Kernel.hlsl", nullptr, nullptr,
        entryName, "cs_5_0", D3DCOMPILE_SKIP_OPTIMIZATION, 0, &shader, &diagnostics);
    if (diagnostics && diagnostics->GetBufferSize() > 1)
        std::cerr.write(static_cast<const char*>(diagnostics->GetBufferPointer()), diagnostics->GetBufferSize() - 1);
    require(SUCCEEDED(result) && shader && shader->GetBufferSize() % sizeof(uint32_t) == 0,
        "FXC could not compile the Slang HLSL to DXBC");
    const auto* words = static_cast<const uint32_t*>(shader->GetBufferPointer());
    return {words, words + shader->GetBufferSize() / sizeof(uint32_t)};
}
#endif

slang::VariableLayoutReflection* findField(slang::TypeLayoutReflection* type, const char* name)
{
    auto index = type->findFieldIndexByName(name);
    require(index >= 0, "expected reflected field");
    return type->getFieldByIndex(static_cast<unsigned>(index));
}

bool isUsed(slang::IMetadata* metadata, ParameterCategory category, size_t set, size_t binding)
{
    bool used = false;
    checkSlang(metadata->isParameterLocationUsed(static_cast<SlangParameterCategory>(category), set, binding, used),
        nullptr, "location usage metadata unavailable");
    return used;
}

struct Binding
{
    uint32_t set;
    uint32_t index;
    uint32_t count;
    VkDescriptorType kind;
    bool used;

    ga::BufferBinding buffer(uint32_t arrayElement, bool readback, std::vector<uint32_t> words) const
    {
        require(used && arrayElement < count, "manifest references absent descriptor");
        return {set, index, arrayElement, kind, readback, std::move(words)};
    }
};

Binding reflectBinding(slang::VariableLayoutReflection* variable, slang::IMetadata* metadata, VkDescriptorType kind,
    slang::VariableLayoutReflection* parent = nullptr, ParameterCategory category = ParameterCategory::DescriptorTableSlot)
{
    auto set = variable->getBindingSpace(category);
    auto binding = variable->getOffset(category);
    if (parent)
    {
        set += parent->getBindingSpace(category);
        binding += parent->getOffset(category);
    }
    auto type = variable->getTypeLayout();
    auto elementCount = type->isArray() ? type->getElementCount() : 1;
    require(set <= UINT32_MAX && binding <= UINT32_MAX && elementCount > 0 && elementCount <= UINT32_MAX,
        "unsupported descriptor location or count");
    bool used = isUsed(metadata, category, set, binding);
    return {static_cast<uint32_t>(set), static_cast<uint32_t>(binding), static_cast<uint32_t>(elementCount), kind, used};
}

struct WordField
{
    size_t offset;
    size_t stride;
};

WordField reflectField(slang::VariableLayoutReflection* field, size_t parentOffset = 0)
{
    auto type = field->getTypeLayout();
    auto offset = parentOffset + field->getOffset();
    size_t stride = type->isArray() ? type->getElementStride(SLANG_PARAMETER_CATEGORY_UNIFORM) : 4;
    require((offset % 4) == 0 && (stride % 4) == 0, "fixture requires aligned u32 fields");
    return {offset / 4, stride / 4};
}

struct Manifest
{
    Target target;
    Binding rows, results, aux, unused, settings, entry;
    size_t rowStride, rowAlignment, resultStride, settingsSize, settingsAlignment, pushSize;
    WordField seed, adjustment, factors, rowCount, bias, entryBias;
    std::array<SlangUInt, 3> groupSize;

    void verify(bool permuted) const
    {
        require(rows.used && results.used && aux.used && !unused.used && settings.used, "unexpected declared/used resources");
        const bool d3d12 = target == Target::D3D12;
        require(rows.set == (d3d12 && permuted ? 1u : 0u) && results.set == 0
            && aux.set == (d3d12 ? (permuted ? 2u : 0u) : 1u) && unused.set == (d3d12 ? 2u : 1u) && settings.set == 0,
            "resource sets or spaces do not match the fixture");
        require(rows.index == (permuted ? 6u : 0u) && results.index == (permuted ? 3u : (d3d12 ? 0u : 1u))
            && aux.index == (permuted ? 4u : 1u) && unused.index == 9,
            "runtime manifest differs from reflected locations");
        require(rows.count == 1 && results.count == 1 && aux.count == 2 && unused.count == 1 && settings.count == 1,
            "reflected descriptor array shape differs");
        if (d3d12)
        {
            require(settings.index == 0 && entry.used && entry.index == 1 && entry.set == 0 && entry.count == 1,
                "unexpected implicit D3D12 CBV locations");
        }
        else
        {
            require(settings.index != rows.index && settings.index != results.index, "implicit Vulkan descriptor collides");
        }
        require(rowStride == 4 && rowAlignment == 4 && resultStride == 1
            && seed.offset == 0 && adjustment.offset == 1 && factors.offset == 2 && factors.stride == 1,
            "storage layout differs from expected std430 layout");
        require(settingsSize == (d3d12 ? 9 : 12) && settingsAlignment == 16
            && rowCount.offset == 0 && bias.offset == 4 && bias.stride == 4,
            "uniform layout differs from expected std140 layout");
        require(pushSize == 1 && entryBias.offset == 0, "push constant layout differs from expectation");
        require(groupSize == std::array<SlangUInt, 3>{4, 1, 1}, "wrong reflected workgroup size");
    }
};

struct CompiledFixture
{
    std::vector<uint32_t> code;
    Manifest manifest;
};

struct Program
{
    ComPtr<slang::ISession> session;
    ComPtr<slang::IComponentType> linked;
    ComPtr<slang::IMetadata> metadata;
    slang::ProgramLayout* layout{};
    std::vector<uint32_t> code;
};

Program compileProgram(slang::IGlobalSession* global, const std::string& source, const char* entryName,
    std::optional<bool> permuted = std::nullopt, Target shaderTarget = Target::Vulkan,
    [[maybe_unused]] const char* fxcDirectory = nullptr)
{
    slang::TargetDesc target;
    slang::CompilerOptionEntry entryOption{slang::CompilerOptionName::VulkanUseEntryPointName,
        {slang::CompilerOptionValueKind::Int, 1}};
    switch (shaderTarget)
    {
    case Target::D3D12:
        target.format = SLANG_DXIL;
        target.profile = global->findProfile("sm_6_0");
        global->setDownstreamCompilerForTransition(SLANG_HLSL, SLANG_DXIL, SLANG_PASS_THROUGH_DXC);
        break;
    case Target::D3D11:
        target.format = SLANG_HLSL;
        target.profile = global->findProfile("sm_5_0");
        break;
    case Target::Vulkan:
        target.format = SLANG_SPIRV;
        target.profile = global->findProfile("spirv_1_3");
        target.flags = SLANG_TARGET_FLAG_GENERATE_SPIRV_DIRECTLY;
        target.compilerOptionEntries = &entryOption;
        target.compilerOptionEntryCount = 1;
        break;
    }
    require(target.profile != SLANG_PROFILE_UNKNOWN, "pinned shader profile missing");

    slang::PreprocessorMacroDesc defines[]{
        {"PERMUTE_BINDINGS", permuted.value_or(false) ? "1" : "0"},
        {"PROBE_D3D12", shaderTarget == Target::D3D12 ? "1" : "0"}};
    slang::SessionDesc description;
    description.targets = &target;
    description.targetCount = 1;
    if (permuted.has_value())
    {
        description.preprocessorMacros = defines;
        description.preprocessorMacroCount = 2;
    }
    Program program;
    checkSlang(global->createSession(description, program.session.writeRef()), nullptr, "createSession failed");

    ComPtr<slang::IBlob> diagnostics;
    auto module = program.session->loadModuleFromSourceString("ga_source", "Kernel.slang", source.c_str(), diagnostics.writeRef());
    checkSlang(module ? SLANG_OK : SLANG_FAIL, diagnostics, "loadModuleFromSourceString failed");
    ComPtr<slang::IEntryPoint> entry;
    checkSlang(module->findEntryPointByName(entryName, entry.writeRef()), nullptr, "compute entry point missing");
    slang::IComponentType* parts[] = {module, entry};
    ComPtr<slang::IComponentType> composite;
    auto result = program.session->createCompositeComponentType(parts, 2, composite.writeRef(), diagnostics.writeRef());
    checkSlang(result, diagnostics, "createCompositeComponentType failed");
    result = composite->link(program.linked.writeRef(), diagnostics.writeRef());
    checkSlang(result, diagnostics, "link failed");

    program.layout = program.linked->getLayout(0, diagnostics.writeRef());
    checkSlang(program.layout ? SLANG_OK : SLANG_FAIL, diagnostics, "getLayout failed");
    require(program.layout->getEntryPointCount() == 1 && program.layout->getEntryPointByIndex(0)->getStage() == SLANG_STAGE_COMPUTE,
        "exactly one compute entry point required");
    ComPtr<slang::IBlob> code;
    result = program.linked->getEntryPointCode(0, 0, code.writeRef(), diagnostics.writeRef());
    checkSlang(result, diagnostics, "getEntryPointCode failed");
    result = program.linked->getEntryPointMetadata(0, 0, program.metadata.writeRef(), diagnostics.writeRef());
    checkSlang(result, diagnostics, "getEntryPointMetadata failed");
    require(code && program.metadata, "invalid code or absent metadata");
#if SKECS_GA_PROBE_D3D12
    if (shaderTarget == Target::D3D11)
    {
        program.code = compileDxbc(code.get(), entryName, fxcDirectory);
        return program;
    }
#endif
    require(code->getBufferSize() % sizeof(uint32_t) == 0, "invalid binary code");
    const auto words = static_cast<const uint32_t*>(code->getBufferPointer());
    program.code.assign(words, words + code->getBufferSize() / sizeof(uint32_t));
    return program;
}

CompiledFixture compileFixture(slang::IGlobalSession* global, const std::string& source, bool permuted, Target target)
{
    const bool d3d12 = target == Target::D3D12;
    auto program = compileProgram(global, source, "main", permuted, target);
    auto layout = program.layout;
    auto metadata = program.metadata.get();
    auto scopeVariable = layout->getGlobalParamsVarLayout();
    auto scope = scopeVariable->getTypeLayout();
    auto globals = scope->getElementTypeLayout();
    auto implicitContainer = scope->getContainerVarLayout();
    require(globals && implicitContainer, "global uniform scope did not expose implicit container");
    auto entryPoint = layout->getEntryPointByIndex(0);
    auto entryScope = entryPoint->getTypeLayout();
    auto entryContainer = entryScope->getContainerVarLayout();
    auto entryData = entryScope->getElementTypeLayout();
    require(entryContainer && entryData && entryContainer->getCategory()
        == (d3d12 ? ParameterCategory::ConstantBuffer : ParameterCategory::PushConstantBuffer),
        "entry point scope did not expose the expected implicit uniform container");

    auto rows = findField(globals, "rows");
    auto results = findField(globals, "results");
    auto aux = findField(globals, "aux");
    auto unused = findField(globals, "unusedRows");
    auto settings = findField(globals, "settings");
    auto settingsType = settings->getTypeLayout();
    auto row = rows->getTypeLayout()->getElementTypeLayout();
    auto resultData = results->getTypeLayout()->getElementTypeLayout();
    auto nested = findField(row, "nested");

    auto storage = d3d12 ? ParameterCategory::ShaderResource : ParameterCategory::DescriptorTableSlot;
    auto writable = d3d12 ? ParameterCategory::UnorderedAccess : ParameterCategory::DescriptorTableSlot;
    auto uniform = d3d12 ? ParameterCategory::ConstantBuffer : ParameterCategory::DescriptorTableSlot;
    Manifest manifest{
        target,
        reflectBinding(rows, metadata, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, nullptr, storage),
        reflectBinding(results, metadata, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, nullptr, writable),
        reflectBinding(aux, metadata, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, nullptr, storage),
        reflectBinding(unused, metadata, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, nullptr, storage),
        reflectBinding(implicitContainer, metadata, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, scopeVariable, uniform),
        d3d12 ? reflectBinding(entryContainer, metadata, VK_DESCRIPTOR_TYPE_UNIFORM_BUFFER, entryPoint->getVarLayout(), uniform) : Binding{},
        row->getStride() / 4, static_cast<size_t>(row->getAlignment()), resultData->getStride() / 4,
        globals->getSize() / 4, static_cast<size_t>(settingsType->getAlignment()), entryData->getSize() / 4,
        reflectField(findField(row, "seed")),
        reflectField(findField(nested->getTypeLayout(), "adjustment"), nested->getOffset()),
        reflectField(findField(nested->getTypeLayout(), "factors"), nested->getOffset()),
        reflectField(findField(settingsType, "rowCount"), settings->getOffset()),
        reflectField(findField(settingsType, "bias"), settings->getOffset()),
        reflectField(findField(entryData, "entryBias")),
        {}};
    entryPoint->getComputeThreadGroupSize(3, manifest.groupSize.data());
    manifest.verify(permuted);
    return {std::move(program.code), manifest};
}

#if SKECS_GA_PROBE_D3D12
struct DxbcStorage
{
    D3D_SHADER_INPUT_TYPE type;
    uint32_t index;
    uint32_t count;
};

void verifyDxbcStorage(const Program& program, const std::array<SlangUInt, 3>& groupSize,
    std::vector<DxbcStorage> storage, const char* fxcDirectory)
{
    auto compiler = loadFxc(fxcDirectory);
    auto reflect = fxcExport<decltype(&D3DReflect)>(compiler, "D3DReflect");
    Microsoft::WRL::ComPtr<ID3D11ShaderReflection> reflection;
    require(SUCCEEDED(reflect(program.code.data(), program.code.size() * sizeof(uint32_t),
        IID_PPV_ARGS(&reflection))), "D3DReflect could not read emitted DXBC");

    D3D11_SHADER_DESC description{};
    require(SUCCEEDED(reflection->GetDesc(&description))
        && D3D11_SHVER_GET_TYPE(description.Version) == D3D11_SHVER_COMPUTE_SHADER
        && D3D11_SHVER_GET_MAJOR(description.Version) == 5
        && D3D11_SHVER_GET_MINOR(description.Version) == 0,
        "FXC did not emit a compute shader model 5.0 artifact");
    UINT width = 0, height = 0, depth = 0;
    reflection->GetThreadGroupSize(&width, &height, &depth);
    require(groupSize == std::array<SlangUInt, 3>{width, height, depth},
        "FXC and Slang workgroup reflections disagree");
    require(description.BoundResources == storage.size(), "FXC and Slang storage usage counts disagree");
    for (UINT index = 0; index < description.BoundResources; ++index)
    {
        D3D11_SHADER_INPUT_BIND_DESC binding{};
        require(SUCCEEDED(reflection->GetResourceBindingDesc(index, &binding)), "cannot reflect DXBC binding");
        const auto found = std::find_if(storage.begin(), storage.end(), [&](const DxbcStorage& expected)
        {
            return binding.Type == expected.type && binding.BindPoint == expected.index
                && binding.BindCount == expected.count;
        });
        require(found != storage.end(), "FXC and Slang storage reflections disagree");
        storage.erase(found);
    }
}
#endif

void writeStorageLayout(const std::filesystem::path& path, const Program& program, const char* entryName, Target target,
    [[maybe_unused]] const char* fxcDirectory)
{
    const bool direct3D = target != Target::Vulkan;
    auto layout = program.layout;
    auto entryPoint = layout->getEntryPointByIndex(0);
    require(!layout->getGlobalParamsVarLayout()->getTypeLayout()->getContainerVarLayout()
        && !entryPoint->getTypeLayout()->getContainerVarLayout(), "only explicit global storage bindings are supported");
    std::array<SlangUInt, 3> groupSize{};
    entryPoint->getComputeThreadGroupSize(3, groupSize.data());
#if SKECS_GA_PROBE_D3D12
    std::vector<DxbcStorage> dxbcStorage;
#endif
    // The artifact manifest hashes these bytes, including the LF record delimiters.
    std::ofstream stream(path, std::ios::binary);
    stream << "slang\t2025.6.1\ntarget\t"
        << (target == Target::D3D11 ? "dxbc\tsm_5_0" : direct3D ? "dxil\tsm_6_0" : "spirv\tspirv_1_3")
        << "\nentry\t" << entryName << "\nworkgroup\t"
        << groupSize[0] << '\t' << groupSize[1] << '\t' << groupSize[2] << '\n';
    for (unsigned i = 0; i < layout->getParameterCount(); ++i)
    {
        auto variable = layout->getParameterByIndex(i);
        auto type = variable->getTypeLayout();
        auto resource = type->isArray() ? type->getElementTypeLayout() : type;
        auto access = resource->getResourceAccess();
        auto element = resource->getElementTypeLayout();
        auto category = direct3D
            ? (access == SLANG_RESOURCE_ACCESS_READ ? ParameterCategory::ShaderResource : ParameterCategory::UnorderedAccess)
            : ParameterCategory::DescriptorTableSlot;
        require(variable->getCategory() == category
            && resource->getResourceShape() == SLANG_STRUCTURED_BUFFER
            && (access == SLANG_RESOURCE_ACCESS_READ || access == SLANG_RESOURCE_ACCESS_READ_WRITE)
            && element && element->getType()->getScalarType() == slang::TypeReflection::ScalarType::UInt32,
            "only StructuredBuffer<uint> and RWStructuredBuffer<uint> are supported");
        auto binding = reflectBinding(variable, program.metadata, VK_DESCRIPTOR_TYPE_STORAGE_BUFFER, nullptr, category);
#if SKECS_GA_PROBE_D3D12
        if (target == Target::D3D11)
        {
            require(binding.set == 0, "D3D11 storage resources must use register space zero");
            if (binding.used)
                dxbcStorage.push_back({access == SLANG_RESOURCE_ACCESS_READ ? D3D_SIT_STRUCTURED : D3D_SIT_UAV_RWSTRUCTURED,
                    binding.index, binding.count});
        }
#endif
        stream << "binding\t" << variable->getName() << '\t' << binding.set << '\t' << binding.index << '\t'
            << binding.count << "\tstorage\t" << (access == SLANG_RESOURCE_ACCESS_READ ? "r" : "rw") << '\t'
            << (binding.used ? "used" : "unused") << '\t' << element->getStride() << '\t' << element->getAlignment() << '\n';
    }
#if SKECS_GA_PROBE_D3D12
    if (target == Target::D3D11)
        verifyDxbcStorage(program, groupSize, std::move(dxbcStorage), fxcDirectory);
#endif
    stream.close();
    require(static_cast<bool>(stream), "could not write reflected layout");
}

void writeManifest(const std::filesystem::path& path, const Manifest& manifest)
{
    const bool d3d12 = manifest.target == Target::D3D12;
    std::ofstream stream(path);
    stream << "Slang 2025.6.1 " << (d3d12 ? "dxil sm_6_0 dxc" : "spirv spirv_1_3 direct") << '\n';
    stream << "entry main\nworkgroup 4 1 1\n";
    auto writeBinding = [&](const char* id, const Binding& binding, const char* kind)
    {
        stream << id << ' ' << binding.set << ' ' << binding.index << ' ' << binding.count << ' '
            << kind << ' ' << (binding.used ? "used" : "unused") << '\n';
    };
    writeBinding("rows", manifest.rows, d3d12 ? "srv" : "storage-r");
    writeBinding("results", manifest.results, d3d12 ? "uav" : "storage-rw");
    writeBinding("aux", manifest.aux, d3d12 ? "srv" : "storage-r");
    writeBinding("unusedRows", manifest.unused, d3d12 ? "srv" : "storage-r");
    writeBinding("globalScope.container", manifest.settings, d3d12 ? "cbv" : "uniform");
    if (d3d12)
        writeBinding("entryScope.container", manifest.entry, "cbv");
    else
        stream << "entryScope.container pushConstant " << manifest.pushSize * 4 << '\n';
    stream << "storage.row stride " << manifest.rowStride * 4 << " align " << manifest.rowAlignment << '\n'
        << "storage.row.seed offset " << manifest.seed.offset * 4 << '\n'
        << "storage.row.nested.adjustment offset " << manifest.adjustment.offset * 4 << '\n'
        << "storage.row.nested.factors offset " << manifest.factors.offset * 4 << " stride " << manifest.factors.stride * 4 << '\n'
        << "uniform.settings size " << manifest.settingsSize * 4 << " align " << manifest.settingsAlignment << '\n'
        << "uniform.settings.rowCount offset " << manifest.rowCount.offset * 4 << '\n'
        << "uniform.settings.bias offset " << manifest.bias.offset * 4 << " stride " << manifest.bias.stride * 4 << '\n'
        << "entryScope.entryBias size " << manifest.pushSize * 4 << " offset " << manifest.entryBias.offset * 4 << '\n';
    require(static_cast<bool>(stream), "could not write reflected manifest");
}

struct FixtureBuffers
{
    std::vector<uint32_t> rows, settings, entry, results, aux0, aux1;
};

template<class Dispatch>
void runFixture(const CompiledFixture& fixture, Dispatch dispatch)
{
    struct Row { int32_t seed, adjustment; uint32_t a, b; };
    constexpr std::array rows{
        Row{-7, 4, 0xFFFFFFFFu, 2}, Row{7, -4, 0xA0000000u, 4},
        Row{std::numeric_limits<int32_t>::min(), 15, 5, 73}, Row{0, 9, 4, 11},
        Row{std::numeric_limits<int32_t>::max(), -21, 0xFFFFFFFFu, 0xFFFFFFFFu}};
    constexpr uint32_t bias0 = 123, bias1 = 456, entryBias = 789, untouched = 0xBADC0DEu;
    const auto& schema = fixture.manifest;
    FixtureBuffers inputs{
        std::vector<uint32_t>(rows.size() * schema.rowStride), std::vector<uint32_t>(schema.settingsSize, untouched),
        std::vector<uint32_t>(schema.pushSize), std::vector<uint32_t>((rows.size() + 1) * 4, untouched),
        {3, 13, 23, 33, 43}, {4, 104, 204, 304, 404}};
    auto expected = inputs.results;
    inputs.settings.at(schema.rowCount.offset) = static_cast<uint32_t>(rows.size());
    inputs.settings.at(schema.bias.offset) = bias0;
    inputs.settings.at(schema.bias.offset + schema.bias.stride) = bias1;
    inputs.entry.at(schema.entryBias.offset) = entryBias;
    for (size_t index = 0; index < rows.size(); ++index)
    {
        const auto& row = rows[index];
        size_t base = index * schema.rowStride;
        inputs.rows.at(base + schema.seed.offset) = std::bit_cast<uint32_t>(row.seed);
        inputs.rows.at(base + schema.adjustment.offset) = std::bit_cast<uint32_t>(row.adjustment);
        inputs.rows.at(base + schema.factors.offset) = row.a;
        inputs.rows.at(base + schema.factors.offset + schema.factors.stride) = row.b;
        expected[index * 4] = std::bit_cast<uint32_t>(row.seed / 3 + row.adjustment);
        expected[index * 4 + 1] = std::bit_cast<uint32_t>(row.seed % 3);
        expected[index * 4 + 2] = row.a * row.b;
        expected[index * 4 + 3] = inputs.aux0[index] + bias0 + inputs.aux1[index] + bias1 + entryBias;
    }

    uint32_t groupCount = static_cast<uint32_t>((rows.size() + schema.groupSize[0] - 1) / schema.groupSize[0]);
    auto actual = dispatch(groupCount, std::move(inputs));
    for (size_t i = 0; i < expected.size(); ++i)
    {
        if (actual[i] != expected[i])
            std::cerr << "word " << i << ": got " << actual[i] << ", expected " << expected[i] << '\n';
    }
    require(actual == expected, "GPU readback differs from independent CPU arithmetic");
}

void runVulkanFixture(VkPhysicalDevice device, const CompiledFixture& fixture)
{
    runFixture(fixture, [&](uint32_t groups, FixtureBuffers inputs)
    {
        const auto& schema = fixture.manifest;
        std::array buffers{
            schema.aux.buffer(1, false, std::move(inputs.aux1)),
            schema.settings.buffer(0, false, std::move(inputs.settings)),
            schema.results.buffer(0, true, std::move(inputs.results)),
            schema.rows.buffer(0, false, std::move(inputs.rows)),
            schema.aux.buffer(0, false, std::move(inputs.aux0))};
        ga::dispatch(device, fixture.code, "main", static_cast<uint32_t>(schema.groupSize[0]), groups, inputs.entry, buffers);
        return std::move(buffers[2].words);
    });
}

#if SKECS_GA_PROBE_D3D12
void runD3D12Fixture(IDXGIAdapter1* adapter, const CompiledFixture& fixture)
{
    runFixture(fixture, [&](uint32_t groups, FixtureBuffers inputs)
    {
        const auto& schema = fixture.manifest;
        auto buffer = [](const Binding& location, D3D12_DESCRIPTOR_RANGE_TYPE type,
            uint32_t arrayElement, uint32_t stride, std::vector<uint32_t> words) -> ga::D3D12Binding
        {
            require(location.used && arrayElement < location.count, "manifest references absent descriptor");
            return {type, location.set, location.index, arrayElement, stride, std::move(words)};
        };
        std::array buffers{
            buffer(schema.aux, D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 0, 4, std::move(inputs.aux0)),
            buffer(schema.aux, D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 1, 4, std::move(inputs.aux1)),
            buffer(schema.settings, D3D12_DESCRIPTOR_RANGE_TYPE_CBV, 0, 4, std::move(inputs.settings)),
            buffer(schema.results, D3D12_DESCRIPTOR_RANGE_TYPE_UAV, 0, static_cast<uint32_t>(schema.resultStride * 4), std::move(inputs.results)),
            buffer(schema.rows, D3D12_DESCRIPTOR_RANGE_TYPE_SRV, 0, static_cast<uint32_t>(schema.rowStride * 4), std::move(inputs.rows)),
            buffer(schema.entry, D3D12_DESCRIPTOR_RANGE_TYPE_CBV, 0, 4, std::move(inputs.entry))};
        ga::dispatchD3D12(adapter, fixture.code, groups, buffers);
        return std::move(buffers[3].words);
    });
}

template<class Run>
void testD3D12Adapters(Run run, const wchar_t* successMessage)
{
    using Microsoft::WRL::ComPtr;
    ComPtr<IDXGIFactory4> factory;
    require(SUCCEEDED(CreateDXGIFactory1(IID_PPV_ARGS(&factory))), "DXGI factory creation failed");
    size_t tested = 0;
    for (UINT index = 0;; ++index)
    {
        ComPtr<IDXGIAdapter1> adapter;
        auto status = factory->EnumAdapters1(index, &adapter);
        if (status == DXGI_ERROR_NOT_FOUND)
            break;
        require(SUCCEEDED(status), "DXGI adapter enumeration failed");
        DXGI_ADAPTER_DESC1 properties;
        require(SUCCEEDED(adapter->GetDesc1(&properties)), "DXGI adapter description failed");
        if (properties.Flags & (DXGI_ADAPTER_FLAG_SOFTWARE | DXGI_ADAPTER_FLAG_REMOTE))
            continue;
        std::wcout << properties.Description << L" (D3D12 feature level 12_0, vendor:device " << std::hex
            << properties.VendorId << L':' << properties.DeviceId << L", LUID " << properties.AdapterLuid.HighPart
            << L':' << properties.AdapterLuid.LowPart << std::dec << L") " << std::flush;
        run(adapter.Get());
        std::wcout << successMessage << '\n';
        ++tested;
    }
    require(tested > 0, "no physical D3D12 GPU tested");
}

uint32_t unsignedNumber(std::string_view text)
{
    uint32_t value = 0;
    const auto [end, error] = std::from_chars(text.data(), text.data() + text.size(), value);
    require(error == std::errc{} && end == text.data() + text.size(), "expected uint32 argument");
    return value;
}

void dispatchD3D12Files(std::span<char*> args)
{
    require(args.size() >= 6 && (args.size() - 2) % 4 == 0,
        "usage: ga-slang-probe dispatch-dxil <artifact.dxil> <groupsX> (<space> <register> <r|rw> <file>)+");
    const auto dxil = ga::readWords(args[0], UINT32_MAX);
    uint32_t groups = unsignedNumber(args[1]);
    std::vector<ga::D3D12Binding> bindings;
    std::vector<std::filesystem::path> paths;
    for (size_t i = 2; i < args.size(); i += 4)
    {
        const std::string_view access(args[i + 2]);
        require(access == "r" || access == "rw", "D3D12 access must be r (t register) or rw (u register)");
        paths.emplace_back(args[i + 3]);
        bindings.push_back({access == "r" ? D3D12_DESCRIPTOR_RANGE_TYPE_SRV : D3D12_DESCRIPTOR_RANGE_TYPE_UAV,
            unsignedNumber(args[i]), unsignedNumber(args[i + 1]), 0, 4, ga::readWords(paths.back(), UINT32_MAX)});
    }
    std::vector<ga::D3D12Binding> result;
    testD3D12Adapters([&](IDXGIAdapter1* adapter)
    {
        auto current = bindings;
        ga::dispatchD3D12(adapter, dxil, groups, current);
        if (result.empty())
        {
            result = std::move(current);
            return;
        }
        for (size_t i = 0; i < current.size(); ++i)
            require(current[i].words == result[i].words, "D3D12 adapters produced different readbacks");
    }, L"dispatch and readback passed");
    for (size_t i = 0; i < result.size(); ++i)
    {
        if (result[i].type == D3D12_DESCRIPTOR_RANGE_TYPE_UAV)
            ga::writeWords(paths[i], result[i].words);
    }
}
#endif
}

int main(int argc, char** argv)
{
    try
    {
        if (argc >= 2 && std::string_view(argv[1]) == "dispatch-dxil")
        {
#if SKECS_GA_PROBE_D3D12
            dispatchD3D12Files({argv + 2, static_cast<size_t>(argc - 2)});
            return 0;
#else
            throw std::runtime_error("this probe was built without D3D12 support");
#endif
        }
        const bool buildDxil = argc == 7 && std::string_view(argv[1]) == "compile-dxil";
        const bool buildDxbc = argc == 7 && (std::string_view(argv[1]) == "compile-dxbc-hlsl"
            || std::string_view(argv[1]) == "compile-dxbc");
        const bool build = buildDxil || buildDxbc || (argc == 6 && std::string_view(argv[1]) == "compile");
        const bool d3d12 = buildDxil || (argc == 5 && std::string_view(argv[1]) == "d3d12");
#if !SKECS_GA_PROBE_D3D12
        require(!d3d12, "this probe was built without D3D12 support");
        require(!buildDxbc, "this probe was built without D3D11 support");
#endif
        require(build || d3d12 || argc == 3,
            "usage: ga-slang-probe <Probe.slang> <out directory>\n"
            "       ga-slang-probe compile <source.slang> <entry> <out.spv> <out.layout>\n"
#if SKECS_GA_PROBE_D3D12
            "       ga-slang-probe d3d12 <Probe.slang> <out directory> <DXC directory>\n"
            "       ga-slang-probe compile-dxil <source.slang> <entry> <out.dxil> <out.layout> <DXC directory>\n"
            "       ga-slang-probe compile-dxbc-hlsl <source.slang> <entry> <out.dxbc> <out.layout> <FXC directory>\n"
#endif
        );
        std::ifstream sourceFile(argv[build || d3d12 ? 2 : 1], std::ios::binary);
        require(static_cast<bool>(sourceFile), "could not open probe source");
        std::string source{std::istreambuf_iterator<char>(sourceFile), std::istreambuf_iterator<char>()};
        ComPtr<slang::IGlobalSession> global;
        checkSlang(slang::createGlobalSession(global.writeRef()), nullptr, "createGlobalSession failed");
        require(std::string_view(global->getBuildTagString()) == "2025.6.1", "expected pinned Slang 2025.6.1");
        if (d3d12)
            global->setDownstreamCompilerPath(SLANG_PASS_THROUGH_DXC, argv[buildDxil ? 6 : 4]);
        auto target = buildDxbc ? Target::D3D11 : d3d12 ? Target::D3D12 : Target::Vulkan;
        if (build)
        {
            auto program = compileProgram(global, source, argv[3], std::nullopt, target, buildDxbc ? argv[6] : nullptr);
            writeStorageLayout(argv[5], program, argv[3], target, buildDxbc ? argv[6] : nullptr);
            ga::writeWords(argv[4], program.code);
            return 0;
        }
        std::cout << "Slang " << global->getBuildTagString() << (d3d12 ? ", DXIL sm_6_0 via DXC\n" : ", direct SPIR-V spirv_1_3\n");
        auto initial = compileFixture(global, source, false, target);
        auto permuted = compileFixture(global, source, true, target);
        std::filesystem::path directory(argv[d3d12 ? 3 : 2]);
        ga::writeWords(directory / (d3d12 ? "initial.api.dxil" : "initial.api.spv"), initial.code);
        ga::writeWords(directory / (d3d12 ? "permuted.api.dxil" : "permuted.api.spv"), permuted.code);
        writeManifest(directory / (d3d12 ? "initial.dxil.api.layout" : "initial.api.layout"), initial.manifest);
        writeManifest(directory / (d3d12 ? "permuted.dxil.api.layout" : "permuted.api.layout"), permuted.manifest);

        auto corrupted = initial.manifest;
        corrupted.rows.index = 9;
        bool rejected = false;
        try { corrupted.verify(false); }
        catch (const std::runtime_error&) { rejected = true; }
        require(rejected, "corrupted manifest accepted");
        std::cout << "corrupted manifest rejected\n";

#if SKECS_GA_PROBE_D3D12
        if (d3d12)
        {
            testD3D12Adapters([&](IDXGIAdapter1* adapter)
            {
                runD3D12Fixture(adapter, initial);
                runD3D12Fixture(adapter, permuted);
            }, L"exact readback passed, both register permutations");
            return 0;
        }
#endif

        ga::VulkanInstance instance;
        size_t tested = 0;
        for (auto device : instance.devices())
        {
            VkPhysicalDeviceProperties properties;
            vkGetPhysicalDeviceProperties(device, &properties);
            if (properties.deviceType != VK_PHYSICAL_DEVICE_TYPE_DISCRETE_GPU
                && properties.deviceType != VK_PHYSICAL_DEVICE_TYPE_INTEGRATED_GPU)
                continue;
            std::cout << properties.deviceName << " (Vulkan " << VK_API_VERSION_MAJOR(properties.apiVersion) << '.'
                << VK_API_VERSION_MINOR(properties.apiVersion) << '.' << VK_API_VERSION_PATCH(properties.apiVersion) << ") " << std::flush;
            runVulkanFixture(device, initial);
            runVulkanFixture(device, permuted);
            std::cout << "exact readback passed, both binding permutations\n";
            ++tested;
        }
        require(tested > 0, "no physical integrated or discrete GPU tested");
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << '\n';
        return 1;
    }
}
