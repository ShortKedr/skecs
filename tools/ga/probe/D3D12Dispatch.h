#pragma once

#include <cstdint>
#include <memory>
#include <span>
#include <vector>
#include <d3d12.h>
#include <dxgi1_4.h>

namespace ga
{
struct D3D12Binding
{
    D3D12_DESCRIPTOR_RANGE_TYPE type;
    uint32_t space, index, arrayElement, stride;
    std::vector<uint32_t> words;
};

class D3D12Kernel
{
public:
    D3D12Kernel(IDXGIAdapter1* adapter, std::span<const uint32_t> dxil, std::span<const D3D12Binding> bindings);
    ~D3D12Kernel();
    void execute(uint32_t groupCount, std::span<D3D12Binding> bindings);

private:
    struct State;
    std::unique_ptr<State> state;
};

void dispatchD3D12(IDXGIAdapter1* adapter, std::span<const uint32_t> dxil, uint32_t groupCount,
    std::span<D3D12Binding> bindings);
}
