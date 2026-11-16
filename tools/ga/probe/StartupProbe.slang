[[vk::binding(3, 0)]] StructuredBuffer<uint> gaInput;
[[vk::binding(5, 0)]] RWStructuredBuffer<uint> gaOutput;
[[vk::binding(7, 0)]] RWStructuredBuffer<uint> gaFaults;

[shader("compute")]
[numthreads(64, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= gaInput[0])
        return;

    uint bits = gaInput[id.x + 1];
    int value = asint(bits);
    uint magnitude = value < 0 ? 0u - bits : bits;
    uint remainder = magnitude % 3u;
    gaOutput[id.x] = asuint(value / 3);
    gaFaults[id.x] = value < 0 ? 0u - remainder : remainder;
}
