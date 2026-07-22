using System.Text.Json;
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

    public static WorkflowExecutionState Apply(
        WorkflowExecutionState workflowState,
        string executableNodeId,
        IReadOnlyDictionary<string, RuntimeDurableValueEncoding> writes)
    {
        ArgumentNullException.ThrowIfNull(workflowState);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentNullException.ThrowIfNull(writes);

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
            // lives (a custom driver may externalize, which the envelope represents natively).
            var envelope = encoding.ExternalReference is { } external
                ? ValueEnvelope.External(
                    declared.Type,
                    external,
                    new ValueProtectionPolicy(
                        declared.Policy.Lifecycle,
                        DurableValueStorage.External,
                        declared.Policy.IsSensitive,
                        declared.Policy.RequiresEncryption,
                        declared.Policy.RedactionMode,
                        declared.Policy.RetentionPolicy,
                        declared.Policy.Metadata))
                : encoding.InlineValue is { } inline && inline.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                    ? ValueEnvelope.Inline(declared.Type, inline, declared.Policy)
                    : ValueEnvelope.Null(declared.Type, declared.Policy);
            frame = frame.Set(variableKey, envelope, frame.Revision);
        }

        return workflowState with { RootVariableFrame = frame };
    }
}
