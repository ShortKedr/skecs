[[vk::binding(0, 0)]] StructuredBuffer<uint> gaInput;
[[vk::binding(1, 0)]] RWStructuredBuffer<uint> gaOutput;
[[vk::binding(2, 0)]] RWStructuredBuffer<uint> gaFaults;

[shader("compute")]
[numthreads(64, 1, 1)]
void main(uint3 id : SV_DispatchThreadID)
{
    uint rows = gaInput[0];
    if (id.x >= rows)
        return;

    uint before = gaInput[1u + id.x];
    uint after = before * 1664525u + 1013904223u;
    gaOutput[id.x] = after;

    // Follow the ordinary result rows with a v1 packet. Each invocation owns one event slice.
    if (id.x == 0u)
    {
        gaOutput[rows] = 1u;
        gaOutput[rows + 1u] = rows;
        gaOutput[rows + 2u] = 2u;
        gaOutput[rows + 3u] = 2u;
    }
    // The sentinel injects a declared overflow without writing beyond the assigned slice.
    uint count = before == 0xffffffffu ? 3u : before % 3u;
    uint slice = rows + 4u + 5u * id.x;
    gaOutput[slice] = count;
    if (count >= 1u)
    {
        gaOutput[slice + 1u] = before;
        gaOutput[slice + 2u] = after;
    }
    if (count >= 2u)
    {
        gaOutput[slice + 3u] = after;
        gaOutput[slice + 4u] = before ^ 0xfeed1234u;
    }

    uint fault = 4u * id.x;
    gaFaults[fault] = 0u;
    gaFaults[fault + 1u] = 0u;
    gaFaults[fault + 2u] = 0u;
    gaFaults[fault + 3u] = 0u;
}
