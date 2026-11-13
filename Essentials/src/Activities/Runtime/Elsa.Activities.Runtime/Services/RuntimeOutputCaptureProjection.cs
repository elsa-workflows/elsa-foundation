using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// The two destinations an activity's output captures project into (#972): durable-value rows for
/// workflow-output targets, and workflow-scope variable writes (keyed by variable reference key)
/// destined for the canonical root variable frame. Both are committed in the same checkpoint as the
/// activity completion that produced them.
/// </summary>
public sealed record RuntimeOutputCaptureProjection(
    IReadOnlyCollection<RuntimeStateChange<DurableValueState>> DurableValues,
    IReadOnlyDictionary<string, RuntimeDurableValueEncoding> WorkflowVariableWrites)
{
    public static RuntimeOutputCaptureProjection Empty { get; } = new(
        [],
        new Dictionary<string, RuntimeDurableValueEncoding>(StringComparer.Ordinal));
}

/// <summary>
/// Applies workflow-variable output captures onto the canonical root variable frame (#972). The frame
/// is the single runtime truth for workflow-scope variables: a capture behaves like a committed
/// <c>Set</c>, so a later <c>VariableRead</c> (or Set) observes the captured value. The changed
/// <see cref="WorkflowExecutionState"/> must ride the same checkpoint commit as the completion that
/// produced the captures — mirroring how the Set intrinsic commits its changed frame.
/// </summary>
public static class RuntimeWorkflowVariableCaptureWriteBack
{
    /// <summary>
    /// Loads the current workflow execution state, applies the workflow-variable capture writes onto its
    /// root frame, and wraps the result as the checkpoint state change the completion commit carries in
    /// its <c>workflowExecution</c> slot. Returns <see langword="null"/> when there is nothing to write.
    /// </summary>
    public static async ValueTask<RuntimeStateChange<WorkflowExecutionState>?> BuildStateChangeAsync(
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        string workflowExecutionId,
        string executableNodeId,
        IReadOnlyDictionary<string, RuntimeDurableValueEncoding> writes,
        IReadOnlyDictionary<string, string> metadata,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(writes);

        if (writes.Count == 0)
            return null;

        var workflowState = await workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Output capture on executable node '{executableNodeId}' targets a workflow variable, but workflow execution '{workflowExecutionId}' has no durable state.");
        var changed = Apply(workflowState, executableNodeId, writes);
        return new RuntimeStateChange<WorkflowExecutionState>(
            StateId: workflowExecutionId,
            Operation: RuntimeStateChangeOperation.Upsert,
            State: changed,
            Metadata: RuntimeModelMetadata.Snapshot(metadata));
    }

    /// <summary>
    /// Commits an invocation's workflow-variable captures under the commit rule (spec 1221 FR-005, FR-009): each capture
    /// is judged against the base the invocation's input read set holds for that variable (none → a blind write), in the
    /// node's lost-update mode. Returns the checkpoint state change, one write-history record per capture, and the
    /// <c>Fault</c> lost update that refuses the completion, if any. When a capture faults, no capture applies.
    /// </summary>
    public static async ValueTask<RuntimeWorkflowVariableCaptureCommit> CommitCapturesAsync(
        IWorkflowExecutionStateStore workflowExecutionStateStore,
        string workflowExecutionId,
        ExecutableNode executableNode,
        ActivityExecutionState invocation,
        IReadOnlyDictionary<string, RuntimeDurableValueEncoding> writes,
        IReadOnlyDictionary<string, string> metadata,
        VariableWriteSource source,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workflowExecutionStateStore);
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(executableNode);
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(writes);

        if (writes.Count == 0)
            return RuntimeWorkflowVariableCaptureCommit.None;

        var workflowState = await workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Output capture on executable node '{executableNode.ExecutableNodeId}' targets a workflow variable, but workflow execution '{workflowExecutionId}' has no durable state.");
        var writer = new VariableWriter(source, executableNode.ExecutableNodeId, invocation.InvocationId, invocation.Attempts?.LastOrDefault()?.AttemptId);
        var result = Apply(
            workflowState,
            executableNode.ExecutableNodeId,
            writes,
            invocation.InputSnapshot?.InputReadSet,
            LostUpdateModes.Of(executableNode),
            writer,
            occurredAt);
        if (result.LostUpdateFault is not null)
            return result with { Change = null };

        return result with
        {
            Change = new RuntimeStateChange<WorkflowExecutionState>(
                StateId: workflowExecutionId,
                Operation: RuntimeStateChangeOperation.Upsert,
                State: result.State!,
                Metadata: RuntimeModelMetadata.Snapshot(metadata))
        };
    }

    /// <summary>Applies captures as blind writes (no base, no history), the form callers without an invocation use.</summary>
    public static WorkflowExecutionState Apply(
        WorkflowExecutionState workflowState,
        string executableNodeId,
        IReadOnlyDictionary<string, RuntimeDurableValueEncoding> writes) =>
        Apply(workflowState, executableNodeId, writes, readSet: null, LostUpdateMode.Default, writer: null, DateTimeOffset.MinValue).State!;

    private static RuntimeWorkflowVariableCaptureCommit Apply(
        WorkflowExecutionState workflowState,
        string executableNodeId,
        IReadOnlyDictionary<string, RuntimeDurableValueEncoding> writes,
        VariableInputReadSet? readSet,
        LostUpdateMode mode,
        VariableWriter? writer,
        DateTimeOffset occurredAt)
    {
        ArgumentNullException.ThrowIfNull(workflowState);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentNullException.ThrowIfNull(writes);

        var records = new List<VariableWriteRecord>();
        var frame = workflowState.RootVariableFrame
            ?? throw new InvalidOperationException(
                $"Output capture on executable node '{executableNodeId}' targets a workflow variable, but workflow execution '{workflowState.WorkflowExecutionId}' has no canonical root variable frame.");

        foreach (var (variableKey, encoding) in writes.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            if (!frame.Values.TryGetValue(variableKey, out var declared))
            {
                throw new InvalidOperationException(
                    $"Output capture on executable node '{executableNodeId}' targets undeclared workflow variable '{variableKey}' in frame '{frame.FrameId}'.");
            }

            // The frame keeps the DECLARED type; the value's policy reflects where the payload actually
            // lives (a custom driver may externalize, which the envelope represents natively). A withheld
            // capture is written as its marker, with the policy that required it, as Set
            // writes one.
            var envelope = encoding switch
            {
                { Withheld: { } withheld } => withheld.Retype(declared.Type),
                { ExternalReference: { } external } => ValueEnvelope.External(
                    declared.Type,
                    external,
                    new ValueProtectionPolicy(
                        declared.Policy.Lifecycle,
                        DurableValueStorage.External,
                        declared.Policy.IsSensitive,
                        declared.Policy.RequiresEncryption,
                        declared.Policy.RedactionMode,
                        declared.Policy.RetentionPolicy,
                        declared.Policy.Metadata)),
                { InlineValue: { ValueKind: not (JsonValueKind.Null or JsonValueKind.Undefined) } inline } =>
                    ValueEnvelope.Inline(declared.Type, inline, declared.Policy),
                _ => ValueEnvelope.Null(declared.Type, declared.Policy)
            };
            var write = frame.Commit(variableKey, envelope, frame.Revision, readSet?.BaseFor(frame.ScopeId, variableKey), mode, writer);
            if (writer is not null)
            {
                var record = VariableWriteRecord.From(frame, write, variableKey, writer, occurredAt);
                if (write.Decision.Outcome == VariableCommitOutcome.LostUpdateFault)
                    return new RuntimeWorkflowVariableCaptureCommit(null, null, [.. records, record with { RejectedValue = envelope }], record with { RejectedValue = envelope });
                records.Add(record);
            }

            frame = write.Frame;
        }

        return new RuntimeWorkflowVariableCaptureCommit(null, workflowState with { RootVariableFrame = frame }, records, null);
    }
}

/// <summary>
/// The outcome of committing an invocation's workflow-variable captures: the checkpoint state change (null when nothing
/// is written or a capture faulted), the write-history records, and the <c>Fault</c> lost update that refused the
/// completion.
/// </summary>
public sealed record RuntimeWorkflowVariableCaptureCommit(
    RuntimeStateChange<WorkflowExecutionState>? Change,
    WorkflowExecutionState? State,
    IReadOnlyList<VariableWriteRecord> Records,
    VariableWriteRecord? LostUpdateFault)
{
    public static RuntimeWorkflowVariableCaptureCommit None { get; } = new(null, null, [], null);
}
