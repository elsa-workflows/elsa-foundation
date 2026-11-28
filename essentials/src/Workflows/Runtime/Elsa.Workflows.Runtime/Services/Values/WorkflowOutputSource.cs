using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Projects the durable workflow-output read model through the configured payload capture policy, and reads the
/// committed output values themselves without that policy.
/// </summary>
public sealed class WorkflowOutputSource(
    IDurableValueStateStore durableValueStateStore,
    IRuntimePayloadCapturePolicy payloadCapturePolicy) : IWorkflowOutputSource
{
    public async ValueTask<IReadOnlyCollection<RuntimeWorkflowOutput>> ReadAsync(
        string workflowExecutionId,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null,
        CancellationToken cancellationToken = default)
    {
        var effective = await ReadEffectiveAsync(workflowExecutionId, pendingDurableValueChanges, cancellationToken);

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

    public async ValueTask<WorkflowOutputValues> ReadValuesAsync(
        string workflowExecutionId,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null,
        CancellationToken cancellationToken = default)
    {
        var effective = await ReadEffectiveAsync(workflowExecutionId, pendingDurableValueChanges, cancellationToken);
        return RuntimeWorkflowOutputStateProjection.ProjectValues(effective.Values);
    }

    private async ValueTask<Dictionary<string, DurableValueState>> ReadEffectiveAsync(
        string workflowExecutionId,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        var stored = await durableValueStateStore.ListAllDurableValueStatesAsync(workflowExecutionId, cancellationToken);
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

        return effective;
    }
}
