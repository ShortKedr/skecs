using System;

namespace SKECS.Serialization;

internal abstract class WireWriter : IDisposable
{
    internal abstract void BeginFile();
    internal abstract void BeginFrame(byte kind);
    internal abstract void EndFrame();
    internal abstract void BeginColumn();
    internal abstract void EndColumn();
    internal abstract void EndFile();
    internal abstract void WriteUInt32(uint value);
    internal abstract void WriteInt32(int value);
    internal abstract void WriteBoolean(bool value);
    internal abstract void WriteBytes(byte[] value);
    internal abstract void WriteField(FieldValue value);
    public abstract void Dispose();
}

internal abstract class WireReader : IDisposable
{
    internal abstract void BeginFile();
    internal abstract byte BeginFrame();
    internal abstract void EndFrame();
    internal abstract void BeginColumn();
    internal abstract void EndColumn();
    internal abstract void EndFile();
    internal abstract uint ReadUInt32();
    internal abstract int ReadInt32();
    internal abstract bool ReadBoolean();
    internal abstract byte[] ReadBytes();
    internal abstract FieldValue ReadField(FieldKind kind);
    public abstract void Dispose();
}
