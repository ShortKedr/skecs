using System.Runtime.InteropServices;
using System.Text;

namespace SKECS.GA.Backend.Vulkan;

public sealed class GaVulkanException : InvalidOperationException
{
    internal GaVulkanException(string message) : base(message) { }
}

internal sealed unsafe class GaVulkanNative : SafeHandle
{
    private const int ErrorCapacity = 2048;
    private readonly delegate* unmanaged[Cdecl]<nint*, byte*, uint, int> _open;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _close;
    private readonly delegate* unmanaged[Cdecl]<nint, uint*, byte*, uint, int> _deviceCount;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, DeviceProperties*, byte*, uint, int> _getDevice;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, int> _probe;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, uint*, uint, byte*, uint, Location*, nint*, byte*, uint, int> _createKernel;
    private readonly delegate* unmanaged[Cdecl]<nint, uint, uint*, uint, uint*, uint, uint*, uint, byte*, uint, int> _dispatch;
    private readonly delegate* unmanaged[Cdecl]<nint, void> _releaseKernel;

    public GaVulkanNative(string libraryPath) : base(0, ownsHandle: true)
    {
        SetHandle(NativeLibrary.Load(Path.GetFullPath(libraryPath)));
        try
        {
            _open = (delegate* unmanaged[Cdecl]<nint*, byte*, uint, int>)Export("open");
            _close = (delegate* unmanaged[Cdecl]<nint, void>)Export("close");
            _deviceCount = (delegate* unmanaged[Cdecl]<nint, uint*, byte*, uint, int>)Export("device_count");
            _getDevice = (delegate* unmanaged[Cdecl]<nint, uint, DeviceProperties*, byte*, uint, int>)Export("get_device");
            _probe = (delegate* unmanaged[Cdecl]<nint, uint, byte*, uint, int>)Export("probe");
            _createKernel = (delegate* unmanaged[Cdecl]<nint, uint, uint*, uint, byte*, uint, Location*, nint*, byte*, uint, int>)
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

    private nint Export(string operation) => NativeLibrary.GetExport(handle, "skecs_ga_vk_" + operation);

    internal Lease OpenContext()
    {
        nint context;
        byte* error = stackalloc byte[ErrorCapacity];
        Check(_open(&context, error, ErrorCapacity), error, "open a Vulkan instance");
        return new Lease(this, context, _close);
    }

    internal uint GetDeviceCount(Lease context)
    {
        uint count;
        byte* error = stackalloc byte[ErrorCapacity];
        var status = _deviceCount(context.DangerousGetHandle(), &count, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "enumerate Vulkan devices");
        return count;
    }

    internal DeviceProperties GetDevice(Lease context, uint index)
    {
        DeviceProperties properties;
        byte* error = stackalloc byte[ErrorCapacity];
        var status = _getDevice(context.DangerousGetHandle(), index, &properties, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "read Vulkan device capabilities");
        return properties;
    }

    internal void Probe(Lease context, uint index)
    {
        byte* error = stackalloc byte[ErrorCapacity];
        var status = _probe(context.DangerousGetHandle(), index, error, ErrorCapacity);
        GC.KeepAlive(context);
        Check(status, error, "run the Vulkan startup probe");
    }

    internal Lease CreateKernel(
        Lease context, uint index, ReadOnlySpan<byte> bytecode, string entryPoint, uint workgroupX, ReadOnlySpan<Location> locations)
    {
        var entry = Encoding.UTF8.GetBytes(entryPoint + "\0");
        byte* error = stackalloc byte[ErrorCapacity];
        nint kernel;
        fixed (byte* shader = bytecode)
        fixed (byte* entryName = entry)
        fixed (Location* resourceLocations = locations)
        {
            var status = _createKernel(context.DangerousGetHandle(), index, (uint*)shader, (uint)bytecode.Length / sizeof(uint),
                entryName, workgroupX, resourceLocations, &kernel, error, ErrorCapacity);
            GC.KeepAlive(context);
            Check(status, error, "prepare the Vulkan kernel");
        }
        return new Lease(this, kernel, _releaseKernel);
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
            Check(status, error, "dispatch and read back the Vulkan kernel");
        }
    }

    private static void Check(int status, byte* error, string operation)
    {
        if (status == 0)
            return;
        ReadOnlySpan<byte> message = new(error, ErrorCapacity);
        var terminator = message.IndexOf((byte)0);
        var detail = terminator >= 0 ? Encoding.UTF8.GetString(message[..terminator]) : string.Empty;
        throw new GaVulkanException(detail.Length != 0 ? $"Could not {operation}. {detail}" : $"Could not {operation} ({status}).");
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly struct Location(uint set, uint binding)
    {
        public readonly uint Set = set;
        public readonly uint Binding = binding;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct DeviceProperties
    {
        public uint VendorId, DeviceId, ApiVersion, DriverVersion, DeviceType;
        public uint MaxGroupsX, MaxWorkgroupX, MaxWorkgroupInvocations, MaxStorageBufferRange;
        public uint MaxBoundDescriptorSets, MaxPerStageStorageBuffers, MaxDescriptorSetStorageBuffers;
        public fixed byte Name[256];
        public fixed byte DeviceUuid[16];

        public string GetName()
        {
            fixed (byte* value = Name)
            {
                ReadOnlySpan<byte> bytes = new(value, 256);
                var end = bytes.IndexOf((byte)0);
                return Encoding.UTF8.GetString(end >= 0 ? bytes[..end] : bytes);
            }
        }

        public string GetUuid()
        {
            fixed (byte* value = DeviceUuid)
                return Convert.ToHexString(new ReadOnlySpan<byte>(value, 16)).ToLowerInvariant();
        }
    }

    internal sealed class Lease : SafeHandle
    {
        private readonly GaVulkanNative _library;
        private readonly delegate* unmanaged[Cdecl]<nint, void> _release;

        public Lease(GaVulkanNative library, nint nativeHandle, delegate* unmanaged[Cdecl]<nint, void> release)
            : base(0, ownsHandle: true)
        {
            if (nativeHandle == 0)
                throw new GaVulkanException("Vulkan runtime returned a null handle.");
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
