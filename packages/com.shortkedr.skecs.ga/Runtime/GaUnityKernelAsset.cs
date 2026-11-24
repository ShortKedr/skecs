using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("SKECS.GA.Unity.Editor")]

namespace SKECS.GA.Unity
{
    /// <summary>A generated Unity compute shader and the verified GA artifact that produced it.</summary>
    public sealed class GaUnityKernelAsset : ScriptableObject
    {
        [SerializeField] private ComputeShader shader;
        [SerializeField] private string contentId;
        [SerializeField] private TextAsset computeSource;
        [SerializeField] private TextAsset manifest;
        [SerializeField] private TextAsset layout;
        [SerializeField] private TextAsset sourceMap;

        public ComputeShader Shader => shader;
        public string ContentId => contentId;

        /// <summary>Use the identity from the generated registration.</summary>
        public GaSystemRequest CreateRequest(GaContractIdentity contract)
        {
            if (!computeSource || !manifest || !layout || !sourceMap || string.IsNullOrEmpty(contentId))
                return new GaSystemRequest(contract, Array.Empty<GaArtifactCandidate>());

            var candidate = GaArtifactCandidate.Load(manifest.bytes, contentId,
                computeSource.bytes, layout.bytes, sourceMap.bytes);
            return new GaSystemRequest(contract, new[] { candidate });
        }

        internal bool Matches(ComputeShader expectedShader, string expectedId, TextAsset expectedSource,
            TextAsset expectedManifest, TextAsset expectedLayout, TextAsset expectedMap) =>
            shader == expectedShader && contentId == expectedId && computeSource == expectedSource &&
            manifest == expectedManifest && layout == expectedLayout && sourceMap == expectedMap;

        internal void Assign(ComputeShader value, string id, TextAsset source, TextAsset data,
            TextAsset bindings, TextAsset positions)
        {
            shader = value;
            contentId = id;
            computeSource = source;
            manifest = data;
            layout = bindings;
            sourceMap = positions;
        }
    }
}
