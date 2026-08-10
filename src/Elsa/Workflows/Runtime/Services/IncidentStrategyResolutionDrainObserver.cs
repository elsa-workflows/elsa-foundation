using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Applies ordinary incident strategies once an outer scheduler drain has reached causal quiescence.
///
/// <para>This is where an <b>activity</b> fault's outcome is decided: the workflow's authored strategy (pinned on the
/// executable, defaulting to <c>Fault/1</c>) chooses whether the blocking incident terminates the run.
/// <see cref="BlockingIncidentWorkflowFaultObserver"/> runs after this one and is the backstop for drains that did not
/// quiesce. See <c>docs/runtime-fault-behavior.md</c>.</para>
/// </summary>
public sealed class IncidentStrategyResolutionDrainObserver(
    IIncidentStateStore incidentStateStore,
    IncidentResolutionBatchExecutor batchExecutor) : IWorkflowSchedulerDrainObserver
{
    public async ValueTask OnDrainedAsync(
        WorkflowExecutionCommandEnvelope envelope,
        RuntimeSchedulerDrainResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(result);

        if (result.StopReason != RuntimeSchedulerDrainStopReason.Quiesced)
            return;

        var incidents = await incidentStateStore.ListBlockingAsync(envelope.WorkflowExecutionId, cancellationToken);
        var ordinaryIncidents = incidents
            .Where(incident => incident.ResolutionOutcome is null && incident.ActivityExecutionId is not null)
            .OrderBy(incident => incident.IncidentId, StringComparer.Ordinal)
            .ToArray();

        if (ordinaryIncidents.Length == 0)
            return;

        await batchExecutor.ExecuteAsync(envelope, ordinaryIncidents, cancellationToken);
    }
}
