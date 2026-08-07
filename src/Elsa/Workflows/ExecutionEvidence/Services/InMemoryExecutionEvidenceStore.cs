using Elsa.Workflows.ExecutionEvidence.Contracts;
using Elsa.Workflows.ExecutionEvidence.Models;

namespace Elsa.Workflows.ExecutionEvidence.Services;

/// <summary>
/// Process-local, bounded evidence buffer. A single lock guards every workflow's state because the store is also the
/// only place a cross-checkpoint variable comparison can be made race-free, and the write volume of a QA collector does
/// not justify per-workflow locks. Every capture decision was already made by the enricher; this type only sequences,
/// diffs comparands, and enforces the buffer caps.
/// </summary>
public sealed class InMemoryExecutionEvidenceStore(ExecutionEvidenceOptions? options = null) : IExecutionEvidenceStore
{
    private readonly ExecutionEvidenceOptions _options = options ?? new ExecutionEvidenceOptions();
    private readonly Lock _syncRoot = new();
    private readonly Dictionary<string, WorkflowEvidence> _workflows = new(StringComparer.Ordinal);

    /// <summary>Monotonic write clock used to pick the least-recently-appended workflow when evicting.</summary>
    private long _appendOrder;

    public void Append(ExecutionEvidenceCommitBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        lock (_syncRoot)
        {
            var evidence = GetOrAdd(batch.WorkflowExecutionId);

            // Checkpoint replay hands the same batch over again; the whole batch is dropped rather than re-sequenced,
            // so a replayed commit cannot fabricate duplicate facts or a spurious variable transition.
            if (evidence.SeenCheckpointIds.Contains(batch.CheckpointId))
                return;

            // Everything below projects into locals first. Committing only after the last projection succeeds means a
            // mid-batch failure leaves the buffer untouched rather than half-written with a stale variable snapshot and
            // the checkpoint already marked seen — which would make the NEXT checkpoint re-emit phantom changes.
            var sequence = evidence.NextSequence;
            var pending = new List<ExecutionEvidenceRecord>(batch.Records.Count);

            foreach (var record in batch.Records)
            {
                pending.Add(record with
                {
                    Sequence = ++sequence,
                    CorrelationId = record.CorrelationId ?? batch.CorrelationId
                });
            }

            if (batch.RootVariables is { } rootVariables)
                sequence = ProjectVariableFacts(evidence, batch, rootVariables, pending, sequence);

            Commit(evidence, batch, pending, sequence);
        }
    }

    public ExecutionEvidencePage List(string workflowExecutionId, long afterSequence)
    {
        if (string.IsNullOrWhiteSpace(workflowExecutionId))
            return EmptyPage(afterSequence);

        lock (_syncRoot)
        {
            if (!_workflows.TryGetValue(workflowExecutionId, out var evidence))
                return EmptyPage(afterSequence);

            var records = evidence.Records.Where(record => record.Sequence > afterSequence).ToArray();
            return new ExecutionEvidencePage(
                records,
                evidence.FirstRetainedSequence,
                LastSequenceOf(records, afterSequence),
                evidence.Terminal,
                MatchedWorkflows: 1);
        }
    }

    /// <summary>
    /// Sequences are assigned per workflow execution, so <paramref name="afterSequence"/> is an approximate cursor here:
    /// it is applied independently to every matched workflow rather than to one global order. Records are ordered by
    /// workflow execution id (ordinal) and then by sequence so repeated reads of the same buffer are identical.
    /// </summary>
    public ExecutionEvidencePage ListByCorrelation(string correlationId, long afterSequence)
    {
        if (string.IsNullOrWhiteSpace(correlationId))
            return EmptyPage(afterSequence);

        lock (_syncRoot)
        {
            var matches = _workflows
                .Where(entry => string.Equals(entry.Value.CorrelationId, correlationId, StringComparison.Ordinal))
                .OrderBy(entry => entry.Key, StringComparer.Ordinal)
                .Select(entry => entry.Value)
                .ToArray();

            if (matches.Length == 0)
                return EmptyPage(afterSequence);

            var records = matches
                .SelectMany(evidence => evidence.Records
                    .Where(record => record.Sequence > afterSequence)
                    .OrderBy(record => record.Sequence))
                .ToArray();

            return new ExecutionEvidencePage(
                records,
                matches.Min(evidence => evidence.FirstRetainedSequence),
                LastSequenceOf(records, afterSequence),
                matches.All(evidence => evidence.Terminal),
                matches.Length);
        }
    }

    public void Clear(string? workflowExecutionId)
    {
        lock (_syncRoot)
        {
            if (workflowExecutionId is null)
                _workflows.Clear();
            else
                _workflows.Remove(workflowExecutionId);
        }
    }

    private static long ProjectVariableFacts(
        WorkflowEvidence evidence,
        ExecutionEvidenceCommitBatch batch,
        IReadOnlyDictionary<string, ExecutionEvidenceVariableCapture> rootVariables,
        List<ExecutionEvidenceRecord> pending,
        long sequence)
    {
        // Ordinal key order keeps the emitted stream deterministic regardless of the frame's dictionary ordering.
        foreach (var (name, capture) in rootVariables.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (evidence.LastVariableComparands.TryGetValue(name, out var previous) &&
                string.Equals(previous, capture.Comparand, StringComparison.Ordinal))
                continue;

            pending.Add(new ExecutionEvidenceRecord
            {
                Sequence = ++sequence,
                WorkflowExecutionId = batch.WorkflowExecutionId,
                Kind = ExecutionEvidenceKinds.VariableSet,
                OccurredAt = batch.OccurredAt,
                CheckpointId = batch.CheckpointId,
                CorrelationId = batch.CorrelationId,
                Name = name,
                Value = capture.Value,
                ValueDisposition = capture.Disposition
            });
        }

        return sequence;
    }

    private void Commit(
        WorkflowEvidence evidence,
        ExecutionEvidenceCommitBatch batch,
        List<ExecutionEvidenceRecord> pending,
        long sequence)
    {
        foreach (var record in pending)
            evidence.Records.Enqueue(record);

        evidence.NextSequence = sequence;
        evidence.LastAppendOrder = ++_appendOrder;
        evidence.Terminal |= batch.Terminal;
        evidence.CorrelationId ??= batch.CorrelationId;

        if (batch.RootVariables is { } rootVariables)
        {
            evidence.LastVariableComparands = rootVariables.ToDictionary(
                entry => entry.Key,
                entry => entry.Value.Comparand,
                StringComparer.Ordinal);
        }

        evidence.SeenCheckpointIds.Add(batch.CheckpointId);
        evidence.SeenCheckpointOrder.Enqueue(batch.CheckpointId);

        while (evidence.Records.Count > _options.MaxRecordsPerWorkflow)
            evidence.Records.Dequeue();

        while (evidence.SeenCheckpointOrder.Count > _options.MaxTrackedCheckpointsPerWorkflow)
            evidence.SeenCheckpointIds.Remove(evidence.SeenCheckpointOrder.Dequeue());
    }

    private WorkflowEvidence GetOrAdd(string workflowExecutionId)
    {
        if (_workflows.TryGetValue(workflowExecutionId, out var evidence))
            return evidence;

        // Stamped before eviction so the entry being added is never the least-recently-appended one.
        evidence = new WorkflowEvidence { LastAppendOrder = ++_appendOrder };
        _workflows[workflowExecutionId] = evidence;

        while (_workflows.Count > _options.MaxWorkflows)
            _workflows.Remove(_workflows.MinBy(entry => entry.Value.LastAppendOrder).Key);

        return evidence;
    }

    private static ExecutionEvidencePage EmptyPage(long afterSequence) => new([], 0, afterSequence, false, 0);

    private static long LastSequenceOf(IReadOnlyList<ExecutionEvidenceRecord> records, long afterSequence) =>
        records.Count == 0 ? afterSequence : records.Max(record => record.Sequence);

    private sealed class WorkflowEvidence
    {
        public Queue<ExecutionEvidenceRecord> Records { get; } = new();
        public HashSet<string> SeenCheckpointIds { get; } = new(StringComparer.Ordinal);

        /// <summary>Insertion order for <see cref="SeenCheckpointIds"/>, so the dedupe window can be bounded FIFO.</summary>
        public Queue<string> SeenCheckpointOrder { get; } = new();

        public Dictionary<string, string> LastVariableComparands { get; set; } = new(StringComparer.Ordinal);
        public long NextSequence { get; set; }
        public long LastAppendOrder { get; set; }
        public bool Terminal { get; set; }
        public string? CorrelationId { get; set; }

        /// <summary>The lowest sequence still buffered; 0 when empty. Reveals evidence dropped by the record cap.</summary>
        public long FirstRetainedSequence => Records.Count == 0 ? 0 : Records.Peek().Sequence;
    }
}
