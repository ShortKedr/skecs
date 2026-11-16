using System;
using System.Runtime.InteropServices;

namespace SKECS;

// Owned by one legacy chunk or by one pooled backing. Never copy an initialized instance.
internal unsafe struct ChunkMemory
{
    internal const int Alignment = 16;
    private IntPtr _allocation;
    private GCHandle _entitiesHandle;
    private GCHandle _versionsHandle;

    internal byte* Address { get; private set; }
    internal int[] Entities { get; private set; }
    internal int[] Versions { get; private set; }
    internal readonly int* PinnedEntities => (int*)_entitiesHandle.AddrOfPinnedObject();
    internal readonly int* PinnedVersions => (int*)_versionsHandle.AddrOfPinnedObject();

    internal void Allocate(int nativeBytes, int capacity)
    {
        Entities = new int[capacity];
        Versions = new int[capacity];
        try
        {
            _entitiesHandle = GCHandle.Alloc(Entities, GCHandleType.Pinned);
            _versionsHandle = GCHandle.Alloc(Versions, GCHandleType.Pinned);
            _allocation = Marshal.AllocHGlobal(nativeBytes);
            var address = (byte*)_allocation;
            var misalignment = (int)((nuint)address & (Alignment - 1));
            // A one-byte allocation backs only the layout without columns.
            Address = nativeBytes == 1 || misalignment == 0 ? address : address + Alignment - misalignment;
        }
        catch
        {
            Release();
            throw;
        }
    }

    internal void Release()
    {
        if (_entitiesHandle.IsAllocated) _entitiesHandle.Free();
        if (_versionsHandle.IsAllocated) _versionsHandle.Free();
        Marshal.FreeHGlobal(_allocation);
        _allocation = IntPtr.Zero;
    }
}
