using System.Xml;

namespace SKECS.RuntimeProfiles.Tools;

internal static partial class TraceReader
{
    private sealed partial class Builder
    {
        private readonly Dictionary<int, PackedType> _packedTypes = new();
        private string? _packedNextKind;
        private int _packedNextType;
        private long _packedIndexes;
        private long _packedPresence;

        private void ReadPacked(XmlReader xml, string kind)
        {
            var id = Int(xml, "componentId");
            Require(_packedNextKind == null || id == _packedNextType, "A packed row transition must stay with its component.");
            if (kind == "packedComponentDefined")
            {
                Require(!_components.ContainsKey(id), "Duplicate packed component or an existing archetype column.");
                var component = RegisterComponent(xml, id);
                var native = Int(xml, "requestedNativeBytes", 1);
                Require(native == checked(Align((long)component.Size * _capacity, 16) + 15), "Invalid packed page geometry.");
                _packedTypes.Add(id, new PackedType(component, native));
                return;
            }

            if (!_packedTypes.TryGetValue(id, out var type)) throw new InvalidDataException($"Undefined packed component {id}.");
            Require(type.Component.Assembly == Required(xml, "assembly") && type.Component.Type == Required(xml, "type"),
                "Inconsistent packed component identity.");
            switch (kind)
            {
                case "packedPageAllocated":
                case "packedPageReused":
                    var page = Int(xml, "page");
                    var reused = kind == "packedPageReused";
                    Require(type.Entities.Count % _capacity == 0 && page == type.Entities.Count / _capacity &&
                        type.Pages.Count == page, "A packed page must extend the populated prefix.");
                    var backing = reused ? TakePackedBacking(xml) : NewBacking(xml);
                    Require(reused ? backing.Native >= type.NativeBytes : backing.Native == type.NativeBytes,
                        "The backing does not match the packed page geometry.");
                    type.Pages.Add(page, backing);
                    Expect(id, "packedRowsChanged");
                    break;
                case "packedRowsChanged":
                    ReadPackedRows(xml, id, type);
                    break;
                case "packedPageReturned":
                    Require(_packedNextKind == kind && Int(xml, "page") == type.Entities.Count / _capacity,
                        "Unexpected packed page return.");
                    var empty = DetachPacked(xml, type);
                    Require(empty.Rows == 0 && empty.Native == Int(xml, "nativeBytes", 1) &&
                        empty.Pinned == Number(xml, "pinnedBytes", 1), "The returned packed backing changed or is occupied.");
                    _pool.Add(empty.Id, empty);
                    Expect(id, "packedIndexesMeasured");
                    break;
                case "packedPageReleased":
                    Require(!_frameOpen && _packedNextKind == null, "A packed page release requires world teardown.");
                    var released = DetachPacked(xml, type);
                    Require(released.Rows == 1, "An empty packed page must return to the pool.");
                    _retainedChunks -= released.Native + released.Pinned;
                    _teardown = true;
                    break;
                case "packedIndexesMeasured":
                    Require(_packedNextKind == kind && type.Pages.Count == ((long)type.Entities.Count + _capacity - 1) / _capacity,
                        "Packed indexes must follow a complete row transition.");
                    var bytes = Number(xml, "indexBytes");
                    _packedIndexes = checked(_packedIndexes - type.IndexBytes + bytes);
                    type.IndexBytes = bytes;
                    _packedPresence = Number(xml, "presenceBytes");
                    _packedNextKind = null;
                    Observe();
                    break;
            }
        }

        private void ReadPackedRows(XmlReader xml, int id, PackedType type)
        {
            var count = Int(xml, "count");
            var entity = Int(xml, "entity");
            var delta = Int(xml, "delta", -1);
            var previous = type.Entities.Count;
            Require((delta is 1 or -1) && (long)previous + delta == count, "Inconsistent packed row count or delta.");
            var swap = Number(xml, "swapBytes");
            if (delta == 1)
            {
                var newPage = previous % _capacity == 0;
                Require(newPage == (_packedNextKind != null) && swap == 0 && type.Entities.Add(entity), "Invalid packed row addition.");
                if (newPage)
                {
                    var backing = type.Pages[previous / _capacity];
                    backing.Rows = 1;
                    _native = checked(_native + backing.Native);
                    _pinned = checked(_pinned + backing.Pinned);
                }
                Expect(id, "packedIndexesMeasured");
                return;
            }

            Require(_packedNextKind == null && (swap == 0 || swap == type.Component.Size + 8L) && type.Entities.Remove(entity),
                "Invalid packed row removal.");
            if (count % _capacity == 0)
            {
                var backing = type.Pages[count / _capacity];
                backing.Rows = 0;
                _native -= backing.Native;
                _pinned -= backing.Pinned;
                Expect(id, "packedPageReturned");
            }
            else Expect(id, "packedIndexesMeasured");
        }

        private Chunk TakePackedBacking(XmlReader xml)
        {
            if (!_pool.Remove(Number(xml, "backing", 1), out var backing) ||
                backing.Native != Int(xml, "nativeBytes", 1) || backing.Pinned != Number(xml, "pinnedBytes", 1))
                throw new InvalidDataException("The reused packed backing was absent or changed size.");
            return backing;
        }

        private static Chunk DetachPacked(XmlReader xml, PackedType type)
        {
            if (!type.Pages.Remove(Int(xml, "page"), out var backing) || backing.Id != Number(xml, "backing", 1))
                throw new InvalidDataException("The packed backing is not attached to the specified page.");
            return backing;
        }

        private void Expect(int id, string kind)
        {
            _packedNextType = id;
            _packedNextKind = kind;
        }

        private sealed record PackedType(TraceComponent Component, int NativeBytes)
        {
            internal Dictionary<int, Chunk> Pages { get; } = new();
            internal HashSet<int> Entities { get; } = new();
            internal long IndexBytes { get; set; }
        }
    }
}
