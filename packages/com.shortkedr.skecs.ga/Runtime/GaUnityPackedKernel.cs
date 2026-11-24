using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;
using static SKECS.GA.GaUnityComputeAbi;

namespace SKECS.GA.Unity
{
    internal sealed class GaUnityPackedKernel : GaPackedKernel
    {
        // A portable per-axis dispatch limit. The runner may submit smaller batches of logical jobs.
        private const uint MaxGroupsX = 65535;
        private static readonly HashSet<GaUnityPackedKernel> Live = new HashSet<GaUnityPackedKernel>();
        private readonly GaUnityDevice device;
        private readonly ComputeShader shader;
        private readonly int main;
        private readonly int probe;
        private readonly int wordsPerRow;
        private readonly int eventCapacity;
        private readonly int eventRecordWords;
        private GraphicsBuffer inputBuffer;
        private GraphicsBuffer outputBuffer;
        private GraphicsBuffer faultBuffer;
        private Readback outputReadback;
        private Readback faultReadback;
        private bool released;

        internal GaUnityPackedKernel(GaUnityDevice device, ComputeShader source, uint group, uint maxBufferBytes,
            IReadOnlyList<GaSourceSpan> sourceMap, GaContractIdentity contract, int wordsPerRow)
            : base(group, MaxGroupsX, maxBufferBytes, sourceMap, contract.TickResourceWords,
                contract.BufferSchema is null ? (int?)null : contract.BufferRowWords, wordsPerRow)
        {
            this.device = device;
            this.wordsPerRow = wordsPerRow;
            eventCapacity = contract.EventCapacity;
            eventRecordWords = contract.UsesCommands || contract.UsesDomainOrder || contract.EventTypeCount != 0
                ? GaOutputRecord.Words + Math.Max(contract.AdditionPayloadWords, contract.EventPayloadWords) +
                    (contract.UsesDomainOrder ? GaOutputRecord.DomainWordCount : 0)
                : 1;
            shader = UnityEngine.Object.Instantiate(source);
            try
            {
                main = shader.FindKernel(MainKernel);
                probe = shader.FindKernel(ProbeKernel);
                Live.Add(this);
            }
            catch
            {
                DestroyShader();
                throw;
            }
        }

        protected override bool SupportsBlockingDispatch => false;

        internal async Task ProbeAsync()
        {
            const uint first = 0x21cfa5e6;
            const uint second = 0xf8014398;
            const uint canary = 0xaffec7ed;
            uint[] input = { 1, first, second };
            uint[] output = { canary, canary, canary };
            uint[] faults = { canary, canary, canary, canary, canary };
            await SendAsync(probe, 1, input, output, faults, probeDispatch: true);
            if (output[0] != (first ^ 0x9e3779b9u) || output[1] != second + 1 || output[2] != canary ||
                faults[0] != 1 || faults[1] != first || faults[2] != second || faults[3] != 0 || faults[4] != canary)
                throw new InvalidDataException("The Unity GA startup probe returned an unexpected result, fault or guard word.");
        }

        protected override Task DispatchAsync(uint groups, uint[] input, uint[] output, uint[] faults)
        {
            long rows = input[0];
            long expectedWords = rows * wordsPerRow;
            if (eventCapacity != 0)
                expectedWords += GaEventPacket.HeaderWords + rows * (1 + (long)eventCapacity * eventRecordWords);
            if (output.Length != expectedWords)
                throw new ArgumentException("The output length does not match the Unity GA kernel layout.", nameof(output));
            return SendAsync(main, (int)groups, input, output, faults, probeDispatch: false);
        }

        protected override void Dispatch(uint groups, uint[] input, uint[] output, uint[] faults) =>
            throw new NotSupportedException("Unity GA uses asynchronous readback on the main thread.");

        private Task SendAsync(int kernel, int groups, uint[] input, uint[] output, uint[] faults, bool probeDispatch)
        {
            device.AssertCanSubmit();
            if (released)
                throw new ObjectDisposedException(nameof(GaUnityPackedKernel));

            EnsureBuffer(ref inputBuffer, input.Length);
            EnsureBuffer(ref outputBuffer, output.Length);
            EnsureBuffer(ref faultBuffer, faults.Length);
            inputBuffer.SetData(input);
            if (probeDispatch)
            {
                outputBuffer.SetData(output);
                faultBuffer.SetData(faults);
            }

            shader.SetBuffer(kernel, InputBuffer, inputBuffer);
            shader.SetBuffer(kernel, OutputBuffer, outputBuffer);
            shader.SetBuffer(kernel, FaultBuffer, faultBuffer);
            shader.SetInt(InputWords, input.Length);
            shader.SetInt(OutputWords, probeDispatch ? output.Length - 1 : output.Length);
            shader.SetInt(FaultWords, probeDispatch ? faults.Length - 1 : faults.Length);
            shader.Dispatch(kernel, groups, 1, 1);

            outputReadback = new Readback(outputBuffer, output, "outputs");
            faultReadback = new Readback(faultBuffer, faults, "faults");
            return Task.WhenAll(outputReadback.Completion, faultReadback.Completion);
        }

        private static void EnsureBuffer(ref GraphicsBuffer slot, int words)
        {
            if (slot != null && slot.count >= words)
                return;
            var replacement = new GraphicsBuffer(GraphicsBuffer.Target.Structured, words, WordStride);
            slot?.Dispose();
            slot = replacement;
        }

        protected override void Release()
        {
            device.AssertMainThread();
            if (released)
                return;
            released = true;
            Live.Remove(this);
            inputBuffer?.Dispose();
            outputBuffer?.Dispose();
            faultBuffer?.Dispose();
            DestroyShader();
        }

        private void DestroyShader()
        {
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(shader);
            else
                UnityEngine.Object.DestroyImmediate(shader);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterShutdown()
        {
            Application.quitting -= Shutdown;
            Application.quitting += Shutdown;
        }

        // Unity may tear down the scripting domain before it schedules a pending continuation.
        // Drain each owned native request before releasing buffers in that case.
        internal static void Shutdown()
        {
            foreach (var kernel in new List<GaUnityPackedKernel>(Live))
            {
                kernel.outputReadback?.DrainForShutdown();
                kernel.faultReadback?.DrainForShutdown();
                kernel.Release();
            }
        }

        private sealed class Readback
        {
            private readonly uint[] destination;
            private readonly string label;
            private readonly TaskCompletionSource<bool> completion =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            private AsyncGPUReadbackRequest request;

            internal Readback(GraphicsBuffer buffer, uint[] destination, string label)
            {
                this.destination = destination;
                this.label = label;
                try
                {
                    request = AsyncGPUReadback.Request(buffer, checked(destination.Length * WordStride), 0, Finish);
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            }

            internal Task Completion => completion.Task;

            private void Finish(AsyncGPUReadbackRequest result)
            {
                if (Completion.IsCompleted)
                    return;
                try
                {
                    if (result.hasError)
                        throw new InvalidDataException($"Unity GA readback of {label} failed.");
                    var data = result.GetData<uint>();
                    if (data.Length != destination.Length)
                        throw new InvalidDataException($"Unity GA readback of {label} returned an unexpected length.");
                    data.CopyTo(destination);
                    completion.TrySetResult(true);
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }

            internal void DrainForShutdown()
            {
                if (Completion.IsCompleted)
                    return;
                request.WaitForCompletion();
                Finish(request);
            }
        }
    }
}
