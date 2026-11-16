using System;
using System.Collections.Generic;

namespace SKECS;

internal sealed class ChurnChunkPool : IDisposable
{
    private readonly int _chunkCapacity;
    private readonly ChurnControlOptions _options;
    private readonly SortedList<int, LinkedList<Backing>> _sizes = new();
    private readonly LinkedList<Backing> _byAge = new();
    private DiagnosticBuffer? _diagnostics;

    internal ChurnChunkPool(int chunkCapacity, ChurnControlOptions options)
    {
        _chunkCapacity = chunkCapacity;
        _options = options;

        var remaining = options.StartupReserveBytes;
        foreach (var block in options.StartupBlocks)
        {
            var bytes = block.NativeBytes + PinnedBytes;
            if (block.Count > remaining / bytes)
                throw new ArgumentException("Startup blocks exceed the startup reserve byte allowance.", nameof(options));
            remaining -= block.Count * bytes;
        }

        try
        {
            foreach (var block in options.StartupBlocks)
                for (var i = 0; i < block.Count; i++)
                    Return(Allocate(block.NativeBytes), null);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private long PinnedBytes => 2L * sizeof(int) * _chunkCapacity;
    internal long NativeBytes { get; private set; }
    internal long BackingBytes { get; private set; }
    internal long EmptyBytes { get; private set; }
    internal long AllocationCount { get; private set; }
    internal long ReuseCount { get; private set; }
    internal long ReleaseCount { get; private set; }

    internal void AttachDiagnostics(DiagnosticBuffer diagnostics)
    {
        _diagnostics = diagnostics;
        foreach (var backing in _byAge)
            diagnostics.AllocatePooledChunk(backing.Id, backing.NativeBytes, PinnedBytes);
    }

    internal Backing Rent(int nativeBytes, LinkedList<Backing> localReserve, out bool reused)
    {
        var backing = localReserve.Last?.Value;
        if (backing == null)
        {
            var largest = nativeBytes + (long)nativeBytes * _options.MaxOversizePercent / 100;
            for (var i = FindSize(nativeBytes); i < _sizes.Count && _sizes.Keys[i] <= largest; i++)
            {
                backing = _sizes.Values[i].Last?.Value;
                if (backing != null) break;
            }
        }

        reused = backing != null;
        if (backing == null) return Allocate(nativeBytes);
        Unlink(backing);
        ReuseCount++;
        return backing;
    }

    internal void Return(Backing backing, LinkedList<Backing>? localReserve)
    {
        _byAge.AddLast(backing.AgeNode);
        backing.SizeClass.AddLast(backing.SizeNode);
        localReserve?.AddLast(backing.LocalNode);
        EmptyBytes += backing.NativeBytes + PinnedBytes;
    }

    internal int Trim(long tableBytes)
    {
        var released = 0;
        while (released < _options.ReleaseBatchSize && BackingBytes + tableBytes > _options.TargetRetainedBytes &&
            _byAge.First is { } oldest)
        {
            Unlink(oldest.Value);
            Release(oldest.Value);
            _diagnostics?.ReleasePooledChunk(oldest.Value.Id);
            released++;
        }
        return released;
    }

    internal void Release(Backing backing)
    {
        backing.Dispose();
        NativeBytes -= backing.NativeBytes;
        BackingBytes -= backing.NativeBytes + PinnedBytes;
        ReleaseCount++;
    }

    public void Dispose()
    {
        while (_byAge.First is { } oldest)
        {
            Unlink(oldest.Value);
            Release(oldest.Value);
            _diagnostics?.ReleasePooledChunk(oldest.Value.Id);
        }
    }

    private void Unlink(Backing backing)
    {
        _byAge.Remove(backing.AgeNode);
        backing.SizeClass.Remove(backing.SizeNode);
        backing.LocalNode.List?.Remove(backing.LocalNode);
        EmptyBytes -= backing.NativeBytes + PinnedBytes;
    }

    private int FindSize(int nativeBytes)
    {
        var first = 0;
        var last = _sizes.Count;
        while (first < last)
        {
            var middle = first + (last - first) / 2;
            if (_sizes.Keys[middle] < nativeBytes) first = middle + 1;
            else last = middle;
        }
        return first;
    }

    private Backing Allocate(int nativeBytes)
    {
        if (!_sizes.TryGetValue(nativeBytes, out var sizeClass))
        {
            sizeClass = new LinkedList<Backing>();
            _sizes.Add(nativeBytes, sizeClass);
        }

        var backing = new Backing(AllocationCount + 1, nativeBytes, _chunkCapacity, sizeClass);
        NativeBytes += nativeBytes;
        BackingBytes += nativeBytes + PinnedBytes;
        AllocationCount++;
        return backing;
    }

    internal sealed unsafe class Backing : IDisposable
    {
        private ChunkMemory _memory;

        internal Backing(long id, int nativeBytes, int capacity, LinkedList<Backing> sizeClass)
        {
            Id = id;
            NativeBytes = nativeBytes;
            SizeClass = sizeClass;
            AgeNode = new LinkedListNode<Backing>(this);
            SizeNode = new LinkedListNode<Backing>(this);
            LocalNode = new LinkedListNode<Backing>(this);
            _memory.Allocate(nativeBytes, capacity);
        }

        internal long Id { get; }
        internal int NativeBytes { get; }
        internal LinkedList<Backing> SizeClass { get; }
        internal LinkedListNode<Backing> AgeNode { get; }
        internal LinkedListNode<Backing> SizeNode { get; }
        internal LinkedListNode<Backing> LocalNode { get; }
        internal ref readonly ChunkMemory Memory => ref _memory;

        public void Dispose() => _memory.Release();
    }
}
