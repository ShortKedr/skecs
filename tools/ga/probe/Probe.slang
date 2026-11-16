struct Nested
{
    int adjustment;
    uint factors[2];
};

struct Row
{
    int seed;
    Nested nested;
};

struct Settings
{
    uint rowCount;
    uint bias[2];
};

#if PERMUTE_BINDINGS
#define ROWS_BINDING 6
#define RESULTS_BINDING 3
#define AUX_BINDING 4
#else
#define ROWS_BINDING 0
#define RESULTS_BINDING 1
#define AUX_BINDING 1
#endif

#if PROBE_D3D12
    #if PERMUTE_BINDINGS
        StructuredBuffer<Row> rows : register(t6, space1);
        RWStructuredBuffer<uint> results : register(u3, space0);
        StructuredBuffer<uint> aux[2] : register(t4, space2);
    #else
        StructuredBuffer<Row> rows : register(t0, space0);
        RWStructuredBuffer<uint> results : register(u0, space0);
        StructuredBuffer<uint> aux[2] : register(t1, space0);
    #endif
    StructuredBuffer<uint> unusedRows : register(t9, space2);
#else
    [[vk::binding(ROWS_BINDING, 0)]] StructuredBuffer<Row> rows;
    [[vk::binding(RESULTS_BINDING, 0)]] RWStructuredBuffer<uint> results;
    [[vk::binding(AUX_BINDING, 1)]] StructuredBuffer<uint> aux[2];
    [[vk::binding(9, 1)]] StructuredBuffer<uint> unusedRows;
#endif
uniform Settings settings;

[shader("compute")]
[numthreads(4, 1, 1)]
void main(uint3 id : SV_DispatchThreadID, uniform uint entryBias)
{
    if (id.x >= settings.rowCount)
        return;

    Row row = rows[id.x];
    uint baseIndex = id.x * 4;
    results[baseIndex] = asuint(row.seed / 3 + row.nested.adjustment);
    // Vulkan leaves signed remainder with a negative operand undefined.
    uint bits = asuint(row.seed);
    uint magnitude = row.seed < 0 ? 0u - bits : bits;
    uint remainder = magnitude % 3u;
    results[baseIndex + 1] = row.seed < 0 ? 0u - remainder : remainder;
    results[baseIndex + 2] = row.nested.factors[0] * row.nested.factors[1];
    results[baseIndex + 3] = aux[0][id.x] + settings.bias[0] + aux[1][id.x] + settings.bias[1] + entryBias;
}
