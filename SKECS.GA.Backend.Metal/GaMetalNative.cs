using System.Runtime.InteropServices;
using System.Text;

namespace SKECS.GA.Backend.Metal;

public sealed class GaMetalException : InvalidOperationException
{
    internal GaMetalException(string message) : base(message) { }
}

internal sealed unsafe class GaMetalNative : SafeHandle
{
    private const int ErrorCapacity = 2048;
    private readonly delegate* unmanaged[Cdecl]<nint*, byte*, uint, int> _open;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _close;
    private readonly delegate* unmanaged[Cdecl]<nint, uint*, byte*, uint, int> _deviceCount;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, DeviceProperties*, byte*, uint, int> _getDevice;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, int> _probe;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, byte*, uint, uint*, nint*, byte*, uint, int> _createKernel;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, uint*, uint, uint*, uint, uint*, uint, byte*, uint, int> _dispatch;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _releaseKernel;

    public GaMetalNative(string libraryPath) : base(0, ownsHandle: true)
    {
        SetHandle(NativeLibrary.Load(Path.GetFullPath(libraryPath)));
        try
        {
            _open = (delegate* unmanaged[Cdecl]<nint*, byte*, uint, int>)Export("open");
            _close = (delegate* unmanaged[Cdecl]<nint, void>)Export("close");
            _deviceCount = (delegate* unmanaged[Cdecl]<nint, uint*, byte*, uint, int>)Export("device_count");
            _getDevice = (delegate* unmanaged[Cdecl]<nint, uint, DeviceProperties*, byte*, uint, int>)Export("get_device");
            _probe = (delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, int>)Export("probe");
            _createKernel = (delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, byte*, uint, uint*, nint*, byte*, uint, int>)
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

    private nint Export(string operation) => NativeLibrary.GetExport(handle, "skecs_ga_metal_" + operation);

    internal Lease OpenContext()
    {
        nint context;
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        Check(_open(&context, error, ErrorCapacity), error, "open the Metal runtime");
        return new Lease(this, context, _close);
    }

    internal uint GetDeviceCount(Lease context)
    {
        uint count;
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        var status = _deviceCount(context.DangerousGetHandle(), &count, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "enumerate Metal devices");
        return count;
    }

    internal DeviceProperties GetDevice(Lease context, uint index)
    {
        DeviceProperties properties;
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        var status = _getDevice(context.DangerousGetHandle(), index, &properties, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "read Metal device capabilities");
        return properties;
    }

    internal void Probe(Lease context, uint index)
    {
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        var status = _probe(context.DangerousGetHandle(), index, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "run the Metal startup probe");
    }

    internal Lease CreateKernel(
        Lease context, uint index, ReadOnlySpan<byte> bytecode, string entryPoint, uint workgroupX, ReadOnlySpan<uint> locations)
    {
        var entry = Encoding.UTF8.GetBytes(entryPoint + "\0");
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        nint kernel;
        fixed (byte* shader = bytecode)
        fixed (byte* entryName = entry)
        fixed (uint* resourceLocations = locations)
        {
            var status = _createKernel(context.DangerousGetHandle(), index, shader, (uint)bytecode.Length,
                entryName, workgroupX, resourceLocations, &kernel, error, ErrorCapacity);
            GC.KeepAlive(context);
            Check(status, error, "prepare the Metal kernel");
        }
        return new Lease(this, kernel, _releaseKernel);
    }

    internal void Dispatch(Lease kernel, uint groups, uint[] input, uint[] output, uint[] faults)
    {
        byte* error = stackalloc byte[ErrorCapacity];
        error[0] = 0;
        fixed (uint* inputWords = input)
        fixed (uint* outputWords = output)
        fixed (uint* faultWords = faults)
        {
            var status = _dispatch(kernel.DangerousGetHandle(), groups, inputWords, (uint)input.Length, outputWords,
                (uint)output.Length, faultWords, (uint)faults.Length, error, ErrorCapacity);
            GC.KeepAlive(kernel);
            Check(status, error, "dispatch and read back the Metal kernel");
        }
    }

    private static void Check(int status, byte* error, string operation)
    {
        if (status == 0)
            return;
        ReadOnlySpan<byte> message = new(error, ErrorCapacity);
        var terminator = message.IndexOf((byte)0);
        var detail = terminator >= 0 ? Encoding.UTF8.GetString(message[..terminator]) : string.Empty;
        throw new GaMetalException(detail.Length != 0 ? $"Could not {operation}. {detail}" : $"Could not {operation} ({status}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceProperties
    {
        public ulong RegistryId;
        public uint Platform, MaxWorkgroupX, MaxWorkgroupInvocations, MaxStorageBufferRange;
        public fixed byte Name[256];

        public string GetName()
        {
            fixed (byte* value = Name)
            {
                ReadOnlySpan<byte> bytes = new(value, 256);
                int end = bytes.IndexOf((byte)0);
                return Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes);
            }
        }
    }

    internal sealed class Lease : SafeHandle
    {
        private readonly GaMetalNative _library;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _release;

        public Lease(GaMetalNative library, nint nativeHandle, delegate* unmanaged[Cdecl]<nint, void> release)
            : base(0, ownsHandle: true)
        {
            if (nativeHandle == 0)
                throw new GaMetalException("Metal runtime returned a null handle.");
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
