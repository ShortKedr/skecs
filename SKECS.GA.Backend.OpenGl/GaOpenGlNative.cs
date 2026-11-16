using System.Runtime.InteropServices;
using System.Text;

namespace SKECS.GA.Backend.OpenGl;

public sealed class GaOpenGlException : InvalidOperationException
{
    internal GaOpenGlException(string message) : base(message) { }
}

internal sealed unsafe class GaOpenGlNative : SafeHandle
{
    private const int ErrorCapacity = 2048;
    private readonly delegate* unmanaged[Cdecl]<nint*, byte*, uint, int> _open;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _close;
    private readonly delegate* unmanaged[Cdecl]<nint, uint*, byte*, uint, int> _deviceCount;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, DeviceProperties*, byte*, uint, int> _getDevice;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, int> _probe;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, uint, uint*, nint*, byte*, uint, int> _createKernel;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, uint*, uint, uint*, uint, uint*, uint, byte*, uint, int> _dispatch;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _releaseKernel;

    internal GaOpenGlNative(string libraryPath) : base(0, ownsHandle: true)
    {
        SetHandle(NativeLibrary.Load(Path.GetFullPath(libraryPath)));
        try
        {
            _open = (delegate* unmanaged[Cdecl]<nint*, byte*, uint, int>)Export("open");
            _close = (delegate* unmanaged[Cdecl]<nint, void>)Export("close");
            _deviceCount = (delegate* unmanaged[Cdecl]<nint, uint*, byte*, uint, int>)Export("device_count");
            _getDevice = (delegate* unmanaged[Cdecl]<nint, uint, DeviceProperties*, byte*, uint, int>)Export("get_device");
            _probe = (delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, int>)Export("probe");
            _createKernel = (delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, uint, uint*, nint*, byte*, uint, int>)
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

    private nint Export(string operation) => NativeLibrary.GetExport(handle, "skecs_ga_gl_" + operation);

    internal Lease OpenContext()
    {
        nint context;
        byte* error = stackalloc byte[ErrorCapacity];
        Check(_open(&context, error, ErrorCapacity), error, "open the OpenGL runtime");
        return TakeLease(context, _close);
    }

    internal Lease OpenEsContext(string angleDirectory, GaAngleRenderer renderer, (ushort Vendor, ushort Device)? pciId)
    {
        nint context;
        byte* error = stackalloc byte[ErrorCapacity];
        int status;
        fixed (char* directory = angleDirectory)
        {
            if (pciId is { } adapter)
            {
                var openDevice = (delegate* unmanaged[Cdecl]<char*, uint, uint, uint, nint*, byte*, uint, int>)
                    Export("open_es_device");
                status = openDevice(directory, (uint)renderer, adapter.Vendor, adapter.Device, &context, error, ErrorCapacity);
            }
            else
            {
                var openEs = (delegate* unmanaged[Cdecl]<char*, uint, nint*, byte*, uint, int>)Export("open_es");
                status = openEs(directory, (uint)renderer, &context, error, ErrorCapacity);
            }
        }
        Check(status, error, "open the ANGLE OpenGL ES runtime");
        return TakeLease(context, _close);
    }

    internal uint GetDeviceCount(Lease context)
    {
        uint count;
        byte* error = stackalloc byte[ErrorCapacity];
        var status = _deviceCount(context.DangerousGetHandle(), &count, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "enumerate OpenGL adapters");
        return count;
    }

    internal DeviceProperties GetDevice(Lease context, uint index)
    {
        DeviceProperties properties;
        byte* error = stackalloc byte[ErrorCapacity];
        var status = _getDevice(context.DangerousGetHandle(), index, &properties, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "read OpenGL adapter capabilities");
        return properties;
    }

    internal void Probe(Lease context, uint index)
    {
        byte* error = stackalloc byte[ErrorCapacity];
        var status = _probe(context.DangerousGetHandle(), index, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "run the OpenGL startup probe");
    }

    internal Lease CreateKernel(
        Lease context, uint index, ReadOnlySpan<byte> artifact, uint workgroupX, ReadOnlySpan<uint> bindings)
    {
        nint kernel;
        byte* error = stackalloc byte[ErrorCapacity];
        fixed (byte* shader = artifact)
        fixed (uint* resourceBindings = bindings)
        {
            var status = _createKernel(context.DangerousGetHandle(), index, shader, (uint)artifact.Length,
                workgroupX, resourceBindings, &kernel, error, ErrorCapacity);
            GC.KeepAlive(context);
            Check(status, error, "prepare the OpenGL kernel");
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
            Check(status, error, "dispatch and read back the OpenGL kernel");
        }
    }

    private Lease TakeLease(nint nativeHandle, delegate* unmanaged[Cdecl]<nint, void> release)
    {
        if (nativeHandle == 0)
            throw new GaOpenGlException("The OpenGL runtime returned a null handle.");
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
        throw new GaOpenGlException(detail.Length != 0 ? $"Could not {operation}. {detail}" : $"Could not {operation} ({status}).");
    }

    private static string ReadString(ReadOnlySpan<byte> text)
    {
        var end = text.IndexOf((byte)0);
        return end < 0 ? string.Empty : Encoding.UTF8.GetString(text[..end]);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceProperties
    {
        public uint GlMajor, GlMinor, MaxGroupsX, MaxWorkgroupX, MaxWorkgroupInvocations;
        public uint MaxStorageBufferRange, MaxStorageBindings;
        public fixed byte Vendor[128];
        public fixed byte Renderer[256];
        public fixed byte Version[256];

        public readonly bool SupportsCompute(bool isEs)
        {
            uint major = isEs ? 3u : 4u;
            uint minor = isEs ? 1u : 3u;
            return (GlMajor > major || GlMajor == major && GlMinor >= minor) && MaxStorageBindings >= 3;
        }

        public string GetVendor()
        {
            fixed (byte* value = Vendor)
                return ReadString(new ReadOnlySpan<byte>(value, 128));
        }

        public string GetRenderer()
        {
            fixed (byte* value = Renderer)
                return ReadString(new ReadOnlySpan<byte>(value, 256));
        }

        public string GetVersion()
        {
            fixed (byte* value = Version)
                return ReadString(new ReadOnlySpan<byte>(value, 256));
        }
    }

    internal sealed class Lease : SafeHandle
    {
        private readonly GaOpenGlNative _library;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _release;

        public Lease(GaOpenGlNative library, nint nativeHandle, delegate* unmanaged[Cdecl]<nint, void> release)
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
