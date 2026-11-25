using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// The commit of one step's writes into closed-scope variable rows (spec 1221 T003, spec 1241; ADR 0092 D-9: frames and
/// rows follow one set of rules). Each write is judged by the commit rule against the version the row carries, and the row
/// stores the resulting version, digest and writer.
/// <para>
/// One instance serves one step commit. It keeps an overlay of the rows this commit already wrote, so a later write into the
/// same row (a capture after the step's own session write, or a second capture) is judged against the row as the previous
/// write left it, not against the stored row (spec 1241 FR-007).
/// </para>
/// </summary>
internal sealed class ClosedScopeRowCommit
{
    private readonly IDurableValueStateStore _store;
    private readonly Dictionary<string, DurableValueState> _overlay = new(StringComparer.Ordinal);

    public ClosedScopeRowCommit(IDurableValueStateStore store, IRuntimeDurableValueStorageDriverRegistry storageDrivers)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(storageDrivers);
        _store = store;
        StorageDrivers = storageDrivers;
    }

    public IRuntimeDurableValueStorageDriverRegistry StorageDrivers { get; }

    /// <summary>The step's session row writes, once applied (spec 1241 T005/T006); null until then.</summary>
    internal StepVariables.ClosedScopeSessionRowWrites? SessionWrites { get; set; }

    /// <summary>The row as this commit last wrote it, else as stored; null when it was never stored.</summary>
    public async ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken) =>
        _overlay.TryGetValue(durableValueId, out var written)
            ? written
            : await _store.FindAsync(workflowExecutionId, durableValueId, cancellationToken);

    /// <summary>
    /// Judges <paramref name="written"/>, the new state of one of <paramref name="boundary"/>'s variable rows, against the
    /// row as this commit sees it. Destination minima survive later writes, and overwritten history keeps the old value's
    /// own protection. A row has no frame revision: a record's commit order is the version its write produced. On a
    /// <c>Fault</c> lost update the record carries the rejected value and nothing enters the overlay.
    /// </summary>
    public async ValueTask<(RuntimeStateChange<DurableValueState> Row, VariableWriteRecord? Record)> CommitAsync(
        string workflowExecutionId,
        ExecutableNode node,
        DurableValueState written,
        ActivityExecutionState boundary,
        VariableWriteBase? writeBase,
        VariableWriter writer,
        ValueProtectionPolicy writtenPolicy,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(boundary);
        ArgumentNullException.ThrowIfNull(writer);

        var key = written.Metadata[RuntimeMetadataKeys.BoundaryReferenceKey];
        var current = await FindAsync(workflowExecutionId, written.DurableValueId, cancellationToken);
        var frame = ClosedScopeVariableRows.ReadFrame(boundary, current is null ? [] : [current]);
        // A variable the scope never stored reads as null at version 0, as an undeclared frame value would.
        var policy = frame.Values.TryGetValue(key, out var stored)
            ? ValuePolicyCombiner.Combine(writtenPolicy, stored.Policy, $"Graph variable '{key}'") : writtenPolicy;
        written = DurableValuePolicies.ApplyMinimum(written, policy, $"Graph variable '{key}'");
        var value = RuntimeInputBindingStateProjection.ProjectBoundaryEnvelopes([written], boundary.InvocationId, ActivityExecutionScopeValues.VariableRole)[key];
        var previous = stored ?? ValueEnvelope.Null(value.Type, policy);
        var hidesValue = policy.HidesValue();
        frame = new VariableFrameState(frame.FrameId, frame.ScopeId, frame.ActivationId, frame.ParentFrameId, frame.Kind,
            new Dictionary<string, ValueEnvelope>(frame.Values, StringComparer.Ordinal) { [key] = previous },
            frame.Revision, frame.Status, frame.Versions);
        var write = frame.Commit(key, value, frame.Revision, writeBase, LostUpdateModes.Of(node), writer);
        // Whether the value is hidden comes from the combined destination policy.
        var record = VariableWriteRecord.From(frame, write, key, writer, occurredAt);
        record = new VariableWriteRecord(record.ScopeId, record.VariableKey, record.Writer, record.Outcome, record.BaseVersion,
            record.CurrentVersion, record.NewVersion, record.NewVersion, record.OccurredAt, record.OverwrittenWriter, record.OverwrittenValue)
        {
            CommitSequence = record.CommitSequence,
            FrameId = record.FrameId,
            Digest = hidesValue ? null : record.Digest
        };
        var change = new RuntimeStateChange<DurableValueState>(written.DurableValueId, RuntimeStateChangeOperation.Upsert, written, written.Metadata);
        if (write.Decision.Outcome == VariableCommitOutcome.LostUpdateFault)
            return (change, record with { RejectedValue = value });

        // A write of the stored value changes nothing (C4): the version and writer stay, and no record is left.
        var versioned = ClosedScopeVariableRows.WithVersion(written, write.Frame.VersionOf(key), hidesValue);
        _overlay[versioned.DurableValueId] = versioned;
        return (change with { State = versioned, Metadata = versioned.Metadata }, VariableWriteRecord.IsRecorded(write.Decision) ? record : null);
    }
}

/// <summary>Creates a step's <see cref="ClosedScopeRowCommit"/>; null when the host stores no durable values.</summary>
internal static class ClosedScopeRowCommits
{
    public static ClosedScopeRowCommit? Create(IServiceProvider serviceProvider) =>
        serviceProvider.GetService<IDurableValueStateStore>() is { } store
            ? new ClosedScopeRowCommit(store, serviceProvider.GetRequiredService<IRuntimeDurableValueStorageDriverRegistry>())
            : null;
}
