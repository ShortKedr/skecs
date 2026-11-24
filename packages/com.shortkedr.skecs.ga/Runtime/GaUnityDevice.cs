using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using static SKECS.GA.GaUnityComputeAbi;

namespace SKECS.GA.Unity
{
    /// <summary>GA device for Unity's active graphics API, owned by the main thread.</summary>
    public sealed class GaUnityDevice : IGaDevice
    {
        private readonly int thread = Environment.CurrentManagedThreadId;
        private readonly Dictionary<string, GaUnityKernelAsset> assets;
        private readonly long maxBufferBytes;
        private bool paused;

        private GaUnityDevice(IEnumerable<GaUnityKernelAsset> registeredAssets)
        {
            assets = new Dictionary<string, GaUnityKernelAsset>(StringComparer.Ordinal);
            foreach (var asset in registeredAssets)
            {
                if (!asset || string.IsNullOrEmpty(asset.ContentId))
                    continue;
                if (assets.TryGetValue(asset.ContentId, out var existing) && existing != asset)
                    throw new ArgumentException($"Multiple Unity GA kernel assets have ID {asset.ContentId}.", nameof(registeredAssets));
                assets[asset.ContentId] = asset;
            }

            Info = new GaDeviceInfo("Unity", SystemInfo.graphicsDeviceType.ToString(), SystemInfo.graphicsDeviceVersion,
                SystemInfo.graphicsDeviceName, "Unavailable through Unity SystemInfo");
            maxBufferBytes = Math.Min(SystemInfo.maxGraphicsBufferSize, int.MaxValue);
            Profiles = new[] { Profile(GaComputeProfiles.Portable32), Profile(GaComputeProfiles.WideFixedEmulated) };
        }

        /// <summary>Call on the main thread. Select with <see cref="GaStartup.SelectAsync"/>.</summary>
        public static IReadOnlyList<IGaDevice> Discover(IEnumerable<GaUnityKernelAsset> assets)
        {
            if (assets is null)
                throw new ArgumentNullException(nameof(assets));
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders ||
                !SystemInfo.supportsAsyncGPUReadback)
                return Array.Empty<IGaDevice>();
            if (SynchronizationContext.Current is null)
                throw new InvalidOperationException("Discover Unity GA devices on Unity's main thread.");
            return new IGaDevice[] { new GaUnityDevice(assets) };
        }

        public GaDeviceInfo Info { get; }
        public GaArtifactFormat Format => GaArtifactFormat.UnityCompute;
        public IReadOnlyList<GaDeviceProfile> Profiles { get; }

        /// <summary>Pause submissions after pausing the runner's Advance calls.</summary>
        public void SetPaused(bool value)
        {
            AssertMainThread();
            paused = value;
        }

        public GaPreparation Prepare(GaArtifactBundle artifact) =>
            new GaPreparation(null, GaStartupReason.PipelineCreationFailed, "Unity GA startup requires SelectAsync.");

        public async ValueTask<GaPreparation> PrepareAsync(GaArtifactBundle artifact)
        {
            AssertMainThread();
            if (paused)
                return new GaPreparation(null, GaStartupReason.PipelineCreationFailed, "The Unity GA device is paused.");
            if (!assets.TryGetValue(artifact.ContentId, out var asset) || !asset || !asset.Shader)
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest, "The imported Unity ComputeShader is missing.");

            GaUnityPackedKernel kernel;
            try
            {
                int wordsPerRow = ValidateArtifact(artifact);
                var group = (uint)artifact.Manifest.Requirements.WorkgroupX;
                ValidateKernel(asset.Shader, MainKernel, group);
                ValidateKernel(asset.Shader, ProbeKernel, group);
                var contract = artifact.Manifest.Contract;
                kernel = new GaUnityPackedKernel(this, asset.Shader, group, (uint)maxBufferBytes,
                    artifact.DecodeSourceMap(), contract, wordsPerRow);
            }
            catch (Exception error) when (error is InvalidDataException || error is ArgumentException ||
                error is UnityException || error is JsonException || error is KeyNotFoundException ||
                error is InvalidOperationException || error is FormatException)
            {
                return new GaPreparation(null, GaStartupReason.IncompatibleManifest, error.Message);
            }

            try
            {
                await kernel.ProbeAsync();
                return new GaPreparation(kernel, GaStartupReason.None);
            }
            catch (Exception error)
            {
                await kernel.DisposeAsync();
                return new GaPreparation(null, GaStartupReason.ProbeFailed, error.Message);
            }
        }

        internal static void ValidateKernel(ComputeShader shader, string kernel, uint group)
        {
            if (!shader.HasKernel(kernel))
                throw new InvalidDataException($"Unity GA shader is missing the kernel '{kernel}'.");
            int index = shader.FindKernel(kernel);
            if (!shader.IsSupported(index))
                throw new InvalidDataException($"Unity GA kernel '{kernel}' is unsupported by the active graphics API.");
            shader.GetKernelThreadGroupSizes(index, out uint x, out uint y, out uint z);
            if (x != group || y != 1 || z != 1)
                throw new InvalidDataException($"Unity kernel '{kernel}' group ({x}, {y}, {z}) differs from ({group}, 1, 1).");
        }

        internal static int ValidateArtifact(GaArtifactBundle artifact)
        {
            var manifest = artifact.Manifest;
            if (manifest.Toolchain.Format != GaArtifactFormat.UnityCompute ||
                manifest.Toolchain.TargetProfile != TargetProfile || manifest.Toolchain.SlangVersion != "none" ||
                manifest.Toolchain.DownstreamVersion != $"Unity {Application.unityVersion} Compute Shader importer" ||
                manifest.Toolchain.Options.Count != 1 || manifest.Toolchain.Options[0] != CodegenOption ||
                manifest.Toolchain.Defines.Count != 0 || manifest.Toolchain.Specializations.Count != 0 ||
                manifest.EntryPoint != MainKernel ||
                manifest.Requirements.WorkgroupY != 1 || manifest.Requirements.WorkgroupZ != 1 ||
                manifest.Requirements.SharedMemoryBytes != 0 || manifest.Requirements.RequiredFeatures.Count != 0 ||
                manifest.DeclaredBindings.Count != 6 || manifest.EntryPointUsage.Count != 6 ||
                manifest.EntryPointUsage.Any(use => !use.Used))
                throw new InvalidDataException("The artifact does not use the Unity GA packed compute ABI.");

            ValidateBinding(manifest, 0, InputBuffer, GaResourceAccess.Read);
            ValidateBinding(manifest, 1, OutputBuffer, GaResourceAccess.ReadWrite);
            ValidateBinding(manifest, 2, FaultBuffer, GaResourceAccess.ReadWrite);
            ValidateBinding(manifest, 3, InputWords, GaResourceAccess.Read);
            ValidateBinding(manifest, 4, OutputWords, GaResourceAccess.Read);
            ValidateBinding(manifest, 5, FaultWords, GaResourceAccess.Read);

            using var document = JsonDocument.Parse(artifact.Reflection.ToArray());
            var layout = document.RootElement;
            int wordsPerRow = layout.GetProperty("wordsPerRow").GetInt32();
            if (layout.GetProperty("version").GetInt32() != 1 ||
                layout.GetProperty("mainKernel").GetString() != MainKernel ||
                layout.GetProperty("probeKernel").GetString() != ProbeKernel ||
                layout.GetProperty("workgroupX").GetInt32() != manifest.Requirements.WorkgroupX ||
                layout.GetProperty("faultWordsPerRow").GetInt32() != GaPackedDispatchResult.FaultStride ||
                layout.GetProperty("properties").GetArrayLength() != manifest.DeclaredBindings.Count ||
                wordsPerRow < 1)
                throw new InvalidDataException("The Unity compute shader layout does not match the GA manifest.");
            var properties = layout.GetProperty("properties");
            foreach (var binding in manifest.DeclaredBindings)
            {
                var property = properties[binding.Index];
                if (property.GetProperty("name").GetString() != binding.LogicalResourceId ||
                    property.GetProperty("kind").GetString() != binding.Kind.ToString() ||
                    property.GetProperty("access").GetString() != binding.Access.ToString() ||
                    property.GetProperty("elementStride").GetInt32() != binding.Layout.ElementStride)
                    throw new InvalidDataException($"The Unity compute shader layout differs at '{binding.LogicalResourceId}'.");
            }
            return wordsPerRow;
        }

        private static void ValidateBinding(GaArtifactManifest manifest, int index, string name, GaResourceAccess access)
        {
            var binding = manifest.DeclaredBindings.SingleOrDefault(entry => entry.LogicalResourceId == name);
            bool storage = index < 3;
            var kind = storage ? GaResourceKind.StorageBuffer : GaResourceKind.UniformBuffer;
            if (binding is null || binding.Namespace != GaBindingNamespace.UnityComputeProperty || binding.Index != index ||
                binding.Space != 0 || binding.Count != 1 || binding.Kind != kind || binding.Access != access ||
                binding.Layout is null || binding.Layout.Size != WordStride || binding.Layout.Alignment != WordStride ||
                binding.Layout.ElementStride != (storage ? WordStride : 0) || binding.Layout.Fields.Count != 0)
                throw new InvalidDataException($"Invalid Unity GA binding '{name}'.");
        }

        internal void AssertMainThread()
        {
            if (thread != Environment.CurrentManagedThreadId)
                throw new InvalidOperationException("Unity GA graphics operations require the owning main thread.");
        }

        internal void AssertCanSubmit()
        {
            AssertMainThread();
            if (paused)
                throw new InvalidOperationException("The Unity GA device is paused.");
        }

        private GaDeviceProfile Profile(string name) => new GaDeviceProfile(name, GaQualification.Experimental,
            Array.Empty<string>(), SystemInfo.maxComputeWorkGroupSizeX, SystemInfo.maxComputeWorkGroupSizeY,
            SystemInfo.maxComputeWorkGroupSizeZ, SystemInfo.maxComputeWorkGroupSize, 0, maxBufferBytes);
    }
}
