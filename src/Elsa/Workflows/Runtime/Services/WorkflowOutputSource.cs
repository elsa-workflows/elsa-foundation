using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>Projects the durable workflow-output read model through the configured payload capture policy.</summary>
public sealed class WorkflowOutputSource(
    IDurableValueStateStore durableValueStateStore,
    IRuntimePayloadCapturePolicy payloadCapturePolicy) : IWorkflowOutputSource
{
    public async ValueTask<IReadOnlyCollection<RuntimeWorkflowOutput>> ReadAsync(
        string workflowExecutionId,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var stored = await durableValueStateStore.ListAsync(workflowExecutionId, cancellationToken);
        var effective = stored.ToDictionary(value => value.DurableValueId, StringComparer.Ordinal);
        foreach (var change in pendingDurableValueChanges ?? [])
        {
            if (!StringComparer.Ordinal.Equals(change.State.WorkflowExecutionId, workflowExecutionId))
            {
                throw new ArgumentException(
                    "Pending durable-value changes must belong to the requested workflow execution.",
                    nameof(pendingDurableValueChanges));
            }

            if (change.Operation == RuntimeStateChangeOperation.Delete)
                effective.Remove(change.StateId);
            else
                effective[change.StateId] = change.State;
        }

        return RuntimeWorkflowOutputStateProjection
            .Project(effective.Values, payloadCapturePolicy)
            .Select(output => new RuntimeWorkflowOutput(
                output.Name,
                output.Value,
                output.IsRedacted,
                output.RedactionReason,
                output.Type,
                output.CapturedAt))
            .ToArray();
    }
}
