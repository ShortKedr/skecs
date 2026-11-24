using System.Diagnostics;

namespace SKECS.GA;

/// <summary>Timing for one successfully applied graphics batch, in <see cref="Stopwatch.GetTimestamp"/> units.</summary>
/// <remarks>Device time covers the native upload, dispatch, fence wait, and readback call.</remarks>
public readonly record struct GaBatchTimings(int Rows, long PackTicks, long DeviceTicks, long ApplyTicks);

/// <summary>Executes a range of canonical runner jobs through a prepared graphics kernel.</summary>
/// <remarks>Keep the startup result alive until the runner and its pending submissions have drained.</remarks>
public class GaBatchProcessor<TInput> : IDeterministicBatchProcessor<TInput> where TInput : unmanaged
{
    public delegate void PackJob(DeterministicBatchReadJob<TInput> job, Span<uint> destination);
    public delegate void ApplyJob(DeterministicJob<TInput> job, int successfulRows, ReadOnlySpan<uint> successfulPrefix);

    private readonly GaPackedKernel _kernel;
    private readonly string _systemId;
    private readonly int _wordsPerRow;
    private readonly int _eventCapacity;
    private readonly bool _usesCommands;
    private readonly bool _usesDomainOrder;
    private readonly GaComponentRemoval[] _componentRemovals;
    private readonly GaComponentAddition[] _componentAdditions;
    private readonly GaTypedEvent[] _eventTypes;
    private readonly int _outputPayloadWords;
    private readonly PackJob _pack;
    private readonly ApplyJob _apply;
    private readonly Action<GaBatchTimings>? _observe;
    private readonly GaTickSnapshot.Reader? _snapshot;
    private readonly GaTickResourceCodec<TInput> _tickResource;
    private readonly GaTickBuffer.Reader? _buffer;

    /// <summary>Binds the prepared artifact to the identity and methods emitted with its row codec.</summary>
    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, 0)
    {
    }

    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, eventCapacity,
            observe: null)
    {
    }

    /// <summary>Binds a generated row codec and optionally reports timing after each successfully applied batch.</summary>
    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity,
        Action<GaBatchTimings>? observe = null, GaTickSnapshot? snapshot = null)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, eventCapacity,
            observe, snapshot, usesCommands: false)
    {
    }

    /// <summary>Binds a generated row codec with the stream format declared by its artifact.</summary>
    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity,
        Action<GaBatchTimings>? observe, GaTickSnapshot? snapshot, bool usesCommands)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, eventCapacity,
            observe, snapshot, usesCommands, [])
    {
    }

    /// <summary>Binds a generated row codec and the component removals it declares.</summary>
    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity,
        Action<GaBatchTimings>? observe, GaTickSnapshot? snapshot, bool usesCommands,
        ReadOnlySpan<GaComponentRemoval> componentRemovals)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, eventCapacity,
            observe, snapshot, usesCommands, componentRemovals, [])
    {
    }

    /// <summary>Binds the codec and the typed structural command tables declared by its kernel.</summary>
    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity,
        Action<GaBatchTimings>? observe, GaTickSnapshot? snapshot, bool usesCommands,
        ReadOnlySpan<GaComponentRemoval> componentRemovals, ReadOnlySpan<GaComponentAddition> componentAdditions)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, eventCapacity,
            observe, snapshot, usesCommands, componentRemovals, componentAdditions, default)
    {
    }

    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity,
        Action<GaBatchTimings>? observe, GaTickSnapshot? snapshot, bool usesCommands,
        ReadOnlySpan<GaComponentRemoval> componentRemovals, ReadOnlySpan<GaComponentAddition> componentAdditions,
        GaTickResourceCodec<TInput> tickResource)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, eventCapacity,
            observe, snapshot, usesCommands, componentRemovals, componentAdditions, tickResource, null)
    {
    }

    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity,
        Action<GaBatchTimings>? observe, GaTickSnapshot? snapshot, bool usesCommands,
        ReadOnlySpan<GaComponentRemoval> componentRemovals, ReadOnlySpan<GaComponentAddition> componentAdditions,
        GaTickResourceCodec<TInput> tickResource, GaTickBuffer? buffer)
        : this(startup, systemId, sourceDigest, irDigest, componentSchemaDigest, wordsPerRow, pack, apply, eventCapacity,
            observe, snapshot, usesCommands, componentRemovals, componentAdditions, tickResource, buffer, [])
    {
    }

    public GaBatchProcessor(GaStartupResult startup, string systemId, string sourceDigest,
        string irDigest, string componentSchemaDigest, int wordsPerRow, PackJob pack, ApplyJob apply, int eventCapacity,
        Action<GaBatchTimings>? observe, GaTickSnapshot? snapshot, bool usesCommands,
        ReadOnlySpan<GaComponentRemoval> componentRemovals, ReadOnlySpan<GaComponentAddition> componentAdditions,
        GaTickResourceCodec<TInput> tickResource, GaTickBuffer? buffer, ReadOnlySpan<GaTypedEvent> eventTypes)
    {
        if (startup is null)
            throw new ArgumentNullException(nameof(startup));
        if (systemId is null)
            throw new ArgumentNullException(nameof(systemId));
        if (string.IsNullOrWhiteSpace(systemId))
            throw new ArgumentException("The value cannot be an empty string or composed entirely of whitespace.", nameof(systemId));
        if (wordsPerRow <= 0)
            throw new ArgumentOutOfRangeException(nameof(wordsPerRow), wordsPerRow, "Value must be positive.");
        if (pack is null)
            throw new ArgumentNullException(nameof(pack));
        if (apply is null)
            throw new ArgumentNullException(nameof(apply));
        if (tickResource.Pack is null
                ? tickResource.TypeId is not null || tickResource.WordCount != 0
                : string.IsNullOrWhiteSpace(tickResource.TypeId) || tickResource.WordCount < 0)
            throw new ArgumentException("The tick resource codec needs a type identity, width and packing method.", nameof(tickResource));
        if ((uint)eventCapacity > GaEventPacket.MaximumCapacityPerSource)
            throw new ArgumentOutOfRangeException(nameof(eventCapacity));
        foreach (var removal in componentRemovals)
            if (!removal.IsBound)
                throw new ArgumentException("The component removal table contains an unbound type.", nameof(componentRemovals));
        int additionPayloadWords = 0;
        foreach (var addition in componentAdditions)
        {
            if (!addition.IsBound)
                throw new ArgumentException("The component addition table contains an unbound type.", nameof(componentAdditions));
            additionPayloadWords = Math.Max(additionPayloadWords, addition.PayloadWords);
        }
        int eventPayloadWords = 0;
        int reductionTypeCount = 0;
        foreach (var type in eventTypes)
        {
            if (!type.IsBound)
                throw new ArgumentException("The typed event table contains an unbound type.", nameof(eventTypes));
            eventPayloadWords = Math.Max(eventPayloadWords, type.PayloadWords);
            if (type.IsContribution)
                reductionTypeCount++;
        }

        var decision = startup.Decisions.SingleOrDefault(item => item.Contract.SystemId == systemId);
        if (!startup.Succeeded || decision?.Route != GaRoute.Gpu)
            throw new InvalidOperationException($"GA startup did not select a GPU kernel for '{systemId}'.");

        var contract = decision.Contract;
        if (contract.SourceDigest != sourceDigest || contract.IrDigest != irDigest ||
            contract.ComponentSchemaDigest != componentSchemaDigest || contract.NumericVersion != GaProgram.NumericVersion ||
            contract.SubsetVersion != GaProgram.SubsetVersion || contract.IrVersion != GaProgram.IrVersion ||
            contract.EventCapacity != eventCapacity || contract.UsesSnapshot != (snapshot is not null) ||
            contract.TickResourceType != tickResource.TypeId || contract.TickResourceWords != tickResource.WordCount ||
            (contract.BufferSchema is not null) != (buffer is not null) ||
            contract.UsesCommands != usesCommands || contract.ComponentRemovalCount != componentRemovals.Length ||
            contract.EventTypeCount != eventTypes.Length || contract.EventPayloadWords != eventPayloadWords ||
            contract.ReductionTypeCount != reductionTypeCount ||
            contract.ComponentAdditionCount != componentAdditions.Length || contract.AdditionPayloadWords != additionPayloadWords)
            throw new InvalidDataException($"The generated row codec does not match the prepared GA artifact for '{systemId}'.");

        _kernel = startup.GetPreparedKernel(systemId) as GaPackedKernel
            ?? throw new InvalidOperationException($"The prepared GA kernel for '{systemId}' cannot execute packed rows.");
        _systemId = systemId;
        _wordsPerRow = wordsPerRow;
        _eventCapacity = eventCapacity;
        _usesCommands = usesCommands;
        _usesDomainOrder = contract.UsesDomainOrder;
        _componentRemovals = componentRemovals.ToArray();
        _componentAdditions = componentAdditions.ToArray();
        _eventTypes = eventTypes.ToArray();
        _outputPayloadWords = Math.Max(additionPayloadWords, eventPayloadWords);
        _pack = pack;
        _apply = apply;
        _observe = observe;
        _snapshot = snapshot is null ? null : new GaTickSnapshot.Reader(snapshot, contract.SnapshotSchema);
        _tickResource = tickResource;
        _buffer = buffer is null ? null : new GaTickBuffer.Reader(buffer, contract.BufferSchema!, contract.BufferRowWords);
    }

    public IDeterministicBatchSubmission<TInput> Begin(DeterministicBatch<TInput> batch)
    {
        if ((_usesDomainOrder || _eventCapacity != 0) && _usesDomainOrder != (batch.DomainOrder is not null))
            throw new InvalidOperationException("The graphics kernel and commit group declare different output order formats.");
        var measure = _observe is not null;
        var packStart = measure ? Stopwatch.GetTimestamp() : 0;
        var snapshot = _snapshot?.Get(batch.TickIndex, batch.Stage, batch.SystemOrder, batch.KernelOrder);
        var buffer = _buffer?.Get(batch.TickIndex);
        var bufferWords = buffer is null ? default : buffer.Words;
        var endJobId = checked(batch.FirstJobId + batch.JobCount);
        var rowCount = 0;
        for (var jobId = batch.FirstJobId; jobId < endJobId; jobId++)
            rowCount = checked(rowCount + batch.GetReadJob(jobId).Count);

        var payload = new uint[checked(1 + rowCount * _wordsPerRow + _tickResource.WordCount + bufferWords.Length)];
        payload[0] = (uint)rowCount;
        var wordOffset = 1;
        for (var jobId = batch.FirstJobId; jobId < endJobId; jobId++)
        {
            var job = batch.GetReadJob(jobId);
            var wordCount = checked(job.Count * _wordsPerRow);
            _pack(job, payload.AsSpan(wordOffset, wordCount));
            wordOffset += wordCount;
        }
        if (_tickResource.Pack is { } packResource)
        {
            var input = batch.Input;
            packResource(in input, payload.AsSpan(wordOffset, _tickResource.WordCount));
        }
        bufferWords.CopyTo(payload.AsSpan(wordOffset + _tickResource.WordCount));

        var packTicks = measure ? Stopwatch.GetTimestamp() - packStart : 0;
        var pending = new Submission(this, batch, rowCount, packTicks);
        pending.SetDeviceSubmission(_kernel.BeginDispatchWithOutput(payload, _wordsPerRow, EventWords(rowCount),
            cancellationToken: default, measureDevice: measure, snapshot: snapshot is null ? default : snapshot.Words));
        return pending;
    }

    private bool UsesPackedOutputs => _usesCommands || _usesDomainOrder || _eventTypes.Length != 0;

    private int OutputRecordWords => UsesPackedOutputs ? GaOutputRecord.Words + _outputPayloadWords +
        (_usesDomainOrder ? GaOutputRecord.DomainWordCount : 0) : 1;

    private int EventWords(int rowCount) =>
        _eventCapacity == 0 || rowCount == 0 ? 0 :
            checked(GaEventPacket.HeaderWords + rowCount * (1 + _eventCapacity * OutputRecordWords));

    private static int DecodeInt32Event(ReadOnlySpan<uint> words) => unchecked((int)words[0]);

    private sealed class Submission : IDeterministicBatchSubmission<TInput>
    {
        private readonly GaBatchProcessor<TInput> _owner;
        private readonly int _firstJobId;
        private readonly int _jobCount;
        private readonly long _tickIndex;
        private readonly SystemStage _stage;
        private readonly int _systemOrder;
        private readonly int _kernelOrder;
        private readonly int _rowCount;
        private readonly long _packTicks;
        private GaPackedSubmission? _deviceSubmission;

        internal Submission(GaBatchProcessor<TInput> owner, DeterministicBatch<TInput> batch, int rowCount, long packTicks)
        {
            _owner = owner;
            _firstJobId = batch.FirstJobId;
            _jobCount = batch.JobCount;
            _tickIndex = batch.TickIndex;
            _stage = batch.Stage;
            _systemOrder = batch.SystemOrder;
            _kernelOrder = batch.KernelOrder;
            _rowCount = rowCount;
            _packTicks = packTicks;
        }

        internal void SetDeviceSubmission(GaPackedSubmission submission) => _deviceSubmission = submission;

        public bool IsCompleted => GetDeviceSubmission().IsCompleted;

        public void Apply(DeterministicBatch<TInput> batch)
        {
            if (batch.FirstJobId != _firstJobId || batch.JobCount != _jobCount || batch.TickIndex != _tickIndex ||
                batch.Stage != _stage || batch.SystemOrder != _systemOrder || batch.KernelOrder != _kernelOrder)
                throw new InvalidOperationException("The runner batch does not match the submitted graphics work.");

            var submission = GetDeviceSubmission();
            if (!submission.IsCompleted)
                throw new InvalidOperationException("The graphics submission is still pending.");

            // The fence has completed even if readback raises an error. Consume the result once.
            _deviceSubmission = null;
            var result = submission.Drain();
            var observe = _owner._observe;
            var applyStart = observe is null ? 0 : Stopwatch.GetTimestamp();
            var wordsPerRow = _owner._wordsPerRow;
            int rowWords = checked(_rowCount * wordsPerRow);
            int eventWords = _owner.EventWords(_rowCount);
            if (result.RowCount != _rowCount || result.Output.Length != checked(rowWords + eventWords))
                throw new InvalidDataException("Graphics readback size differs from the submitted canonical rows.");
            GaEventPacket events = default;
            if (eventWords != 0)
            {
                events = new GaEventPacket(result.Output[rowWords..]);
                if (events.SourceCount != _rowCount || events.CapacityPerSource != _owner._eventCapacity ||
                    events.EventWords != _owner.OutputRecordWords)
                    throw new InvalidDataException("Graphics output readback does not match its declared stream.");
            }

            var firstFault = result.FindFirstFault();
            var successfulRows = firstFault?.Row ?? _rowCount;
            if (eventWords != 0 && _owner.UsesPackedOutputs)
                events.ValidateMixedRecords(successfulRows, _owner._componentRemovals, _owner._componentAdditions,
                    _owner._usesDomainOrder, _owner._eventTypes);
            var endJobId = _firstJobId + _jobCount;
            var rowOffset = 0;
            for (var jobId = _firstJobId; jobId < endJobId; jobId++)
            {
                var job = batch.GetJob(jobId);
                var prefix = Math.Min(job.Count, successfulRows - rowOffset);
                if (prefix != 0)
                {
                    _owner._apply(job, prefix, result.Output.Slice(rowOffset * wordsPerRow, prefix * wordsPerRow));
                    if (eventWords != 0)
                    {
                        if (_owner.UsesPackedOutputs)
                            events.ApplyMixedEntityRows(job, rowOffset, prefix, _owner._componentRemovals, _owner._componentAdditions,
                                _owner._usesDomainOrder, _owner._eventTypes);
                        else
                            events.ApplyEntityRows<TInput, int>(job, rowOffset, prefix, DecodeInt32Event);
                    }
                }
                if (prefix < job.Count && firstFault is { } failure)
                {
                    var entity = job.GetEntity(prefix);
                    throw new GaExecutionException(_owner._systemId, batch.TickIndex, batch.Stage, batch.SystemOrder,
                        batch.KernelOrder, job.GetSourceOrderOrdinal(prefix), entity.Id, entity.Version, failure.Fault,
                        job.GetSourceOrderKey(prefix));
                }
                rowOffset += job.Count;
            }

            if (observe is not null)
                observe(new GaBatchTimings(_rowCount, _packTicks, result.DeviceTicks, Stopwatch.GetTimestamp() - applyStart));
        }

        public void Discard()
        {
            var submission = _deviceSubmission;
            _deviceSubmission = null;
            submission?.Discard();
        }

        public void Dispose() => Discard();

        private GaPackedSubmission GetDeviceSubmission() =>
            _deviceSubmission ?? throw new ObjectDisposedException(nameof(Submission));
    }
}
