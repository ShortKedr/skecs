// GaSnapshotPacket.FormatVersion = 1. Use a view with the same buffer that opened it.
struct GaSnapshotViewV1
{
    uint firstRow;
    uint rowCount;
    uint rowWordCount;
};

bool gaTryOpenSnapshotV1(StructuredBuffer<uint> words, uint packetStart, uint packetWords,
    uint expectedValueWords, out GaSnapshotViewV1 view)
{
    view.firstRow = 0u;
    view.rowCount = 0u;
    view.rowWordCount = 0u;

    uint bufferWords, stride;
    words.GetDimensions(bufferWords, stride);
    if (packetStart > bufferWords || packetWords < 3u || expectedValueWords > 0x7ffffffdu)
        return false;
    if (packetWords > bufferWords - packetStart)
        return false;
    if (words[packetStart] != 1u || words[packetStart + 2u] != expectedValueWords)
        return false;

    uint rowWordCount = expectedValueWords + 2u;
    uint bodyWords = packetWords - 3u;
    uint rows = words[packetStart + 1u];
    if (bodyWords % rowWordCount != 0u || rows != bodyWords / rowWordCount)
        return false;

    view.firstRow = packetStart + 3u;
    view.rowCount = rows;
    view.rowWordCount = rowWordCount;
    return true;
}

// The host packet builder establishes strictly ascending, unique IDs.
bool gaTryFindSnapshotV1(StructuredBuffer<uint> words, GaSnapshotViewV1 view, uint id,
    uint generation, out uint valuesOffset)
{
    valuesOffset = 0u;
    if (id > 0x7fffffffu || generation == 0u || generation > 0x7fffffffu)
        return false;

    uint first = 0u;
    uint last = view.rowCount;
    while (first < last)
    {
        uint middle = first + (last - first) / 2u;
        uint row = view.firstRow + middle * view.rowWordCount;
        uint rowId = words[row];
        if (rowId < id)
            first = middle + 1u;
        else if (rowId > id)
            last = middle;
        else
        {
            if (words[row + 1u] != generation)
                return false;
            valuesOffset = row + 2u;
            return true;
        }
    }
    return false;
}
