using System.Runtime.InteropServices;
using System.Text;

namespace SKECS.GA.Backend.D3D12;

public sealed class GaD3D12Exception : InvalidOperationException
{
    internal GaD3D12Exception(string message) : base(message) { }
}

internal sealed unsafe class GaD3D12Native : SafeHandle
{
    private const int ErrorCapacity = 2048;
    private readonly delegate* unmanaged[Cdecl]<nint*, byte*, uint, int> _open;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _close;
    private readonly delegate* unmanaged[Cdecl]<nint, uint*, byte*, uint, int> _deviceCount;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, DeviceProperties*, byte*, uint, int> _getDevice;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, int> _probe;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, uint*, uint, uint, Location*, nint*, byte*, uint, int> _createKernel;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, uint*, uint, uint*, uint, uint*, uint, byte*, uint, int> _dispatch;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _releaseKernel;

    internal GaD3D12Native(string libraryPath) : base(0, ownsHandle: true)
    {
        SetHandle(NativeLibrary.Load(Path.GetFullPath(libraryPath)));
        try
        {
            _open = (delegate* unmanaged[Cdecl]<nint*, byte*, uint, int>)Export("open");
            _close = (delegate* unmanaged[Cdecl]<nint, void>)Export("close");
            _deviceCount = (delegate* unmanaged[Cdecl]<nint, uint*, byte*, uint, int>)Export("device_count");
            _getDevice = (delegate* unmanaged[Cdecl]<nint, uint, DeviceProperties*, byte*, uint, int>)Export("get_device");
            _probe = (delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, int>)Export("probe");
            _createKernel = (delegate* unmanaged[Cdecl]<nint, uint, uint*, uint, uint, Location*, nint*, byte*, uint, int>)
                Export("create_kernel");
            _dispatch = (delegate* unmanaged[Cdecl]<nint, uint, uint*, uint, uint*, uint, uint*, uint, byte*, uint, int>)
                Export("dispatch");
            _releaseKernel = (delegate* unmanaged[Cdecl]<nint, void>)Export("release_kernel");
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public override bool IsInvalid => handle == 0;

    protected override bool ReleaseHandle()
    {
        NativeLibrary.Free(handle);
        return true;
    }

    private nint Export(string operation) => NativeLibrary.GetExport(handle, "skecs_ga_d3d12_" + operation);

    internal Lease OpenContext()
    {
        nint context;
        byte* error = stackalloc byte[ErrorCapacity];
        Check(_open(&context, error, ErrorCapacity), error, "open the D3D12 runtime");
        return TakeLease(context, _close);
    }

    internal uint GetDeviceCount(Lease context)
    {
        uint count;
        byte* error = stackalloc byte[ErrorCapacity];
        var status = _deviceCount(context.DangerousGetHandle(), &count, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "enumerate D3D12 adapters");
        return count;
    }

    internal DeviceProperties GetDevice(Lease context, uint index)
    {
        DeviceProperties properties;
        byte* error = stackalloc byte[ErrorCapacity];
        var status = _getDevice(context.DangerousGetHandle(), index, &properties, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "read D3D12 adapter capabilities");
        return properties;
    }

    internal void Probe(Lease context, uint index)
    {
        byte* error = stackalloc byte[ErrorCapacity];
        var status = _probe(context.DangerousGetHandle(), index, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "run the D3D12 startup probe");
    }

    internal Lease CreateKernel(
        Lease context, uint index, ReadOnlySpan<byte> artifact, uint workgroupX, ReadOnlySpan<Location> locations)
    {
        nint kernel;
        byte* error = stackalloc byte[ErrorCapacity];
        fixed (byte* shader = artifact)
        fixed (Location* resourceLocations = locations)
        {
            var status = _createKernel(context.DangerousGetHandle(), index, (uint*)shader, (uint)artifact.Length / sizeof(uint),
                workgroupX, resourceLocations, &kernel, error, ErrorCapacity);
            GC.KeepAlive(context);
            Check(status, error, "prepare the D3D12 kernel");
        }
        return TakeLease(kernel, _releaseKernel);
    }

    internal void Dispatch(Lease kernel, uint groups, uint[] input, uint[] output, uint[] faults)
    {
        byte* error = stackalloc byte[ErrorCapacity];
        fixed (uint* inputWords = input)
        fixed (uint* outputWords = output)
        fixed (uint* faultWords = faults)
        {
            var status = _dispatch(kernel.DangerousGetHandle(), groups, inputWords, (uint)input.Length, outputWords,
                (uint)output.Length, faultWords, (uint)faults.Length, error, ErrorCapacity);
            GC.KeepAlive(kernel);
            Check(status, error, "dispatch and read back the D3D12 kernel");
        }
    }

    private Lease TakeLease(nint nativeHandle, delegate* unmanaged[Cdecl]<nint, void> release)
    {
        if (nativeHandle == 0)
            throw new GaD3D12Exception("The D3D12 runtime returned a null handle.");
        try
        {
            return new Lease(this, nativeHandle, release);
        }
        catch
        {
            release(nativeHandle);
            throw;
        }
    }

    private static void Check(int status, byte* error, string operation)
    {
        if (status == 0)
            return;
        var detail = ReadString(new ReadOnlySpan<byte>(error, ErrorCapacity));
        throw new GaD3D12Exception(detail.Length != 0 ? $"Could not {operation}. {detail}" : $"Could not {operation} ({status}).");
    }

    private static string ReadString(ReadOnlySpan<byte> text)
    {
        var end = text.IndexOf((byte)0);
        return end < 0 ? string.Empty : Encoding.UTF8.GetString(text[..end]);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Location(uint space, uint index)
    {
        public readonly uint Space = space;
        public readonly uint Index = index;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceProperties
    {
        public uint VendorId, DeviceId, ShaderModel, DriverVersionLow, DriverVersionHigh, DeviceType;
        public uint MaxGroupsX, MaxWorkgroupX, MaxWorkgroupInvocations, MaxStorageBufferRange;
        public fixed byte Name[256];
        public fixed byte DeviceLuid[8];

        public string GetName()
        {
            fixed (byte* value = Name)
                return ReadString(new ReadOnlySpan<byte>(value, 256));
        }

        public string GetLuid()
        {
            fixed (byte* value = DeviceLuid)
                return Convert.ToHexString(new ReadOnlySpan<byte>(value, 8)).ToLowerInvariant();
        }
    }

    internal sealed class Lease : SafeHandle
    {
        private readonly GaD3D12Native _library;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _release;

        public Lease(GaD3D12Native library, nint nativeHandle, delegate* unmanaged[Cdecl]<nint, void> release)
            : base(0, ownsHandle: true)
        {
            _library = library;
            _release = release;
            bool added = false;
            library.DangerousAddRef(ref added);
            SetHandle(nativeHandle);
        }

        public override bool IsInvalid => handle == 0;

        protected override bool ReleaseHandle()
        {
            _release(handle);
            _library.DangerousRelease();
            return true;
        }
    }
}
