using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowDispatchReadinessAssessor(
    IEnumerable<IWorkflowDispatchDurabilityEvidence> evidence) : IWorkflowDispatchReadinessAssessor
{
    public ValueTask<WorkflowDispatchReadinessReport> AssessAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var contributions = evidence
            .GroupBy(item => item.Component, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Max(item => item.Level),
                StringComparer.Ordinal);
        var infrastructureComponents = WorkflowDispatchDurabilityComponents.Required
            .Where(component => component != WorkflowDispatchDurabilityComponents.Resumption)
            .ToArray();
        var hasAllInfrastructure = infrastructureComponents.All(contributions.ContainsKey);
        var infrastructureLevels = infrastructureComponents
            .Where(contributions.ContainsKey)
            .Select(component => contributions[component])
            .ToArray();
        var hasResumption = contributions.ContainsKey(WorkflowDispatchDurabilityComponents.Resumption);
        var baseGuarantee = hasResumption && hasAllInfrastructure && infrastructureLevels.All(level => level == WorkflowDispatchDurabilityLevel.Durable)
            ? WorkflowDispatchReadinessGuarantee.DurableReady
            : hasResumption && hasAllInfrastructure && infrastructureLevels.All(level => level == WorkflowDispatchDurabilityLevel.ProcessLocal)
                ? WorkflowDispatchReadinessGuarantee.ProcessLocal
                : WorkflowDispatchReadinessGuarantee.Unsafe;
        var hasDistribution = contributions.TryGetValue(
            WorkflowDispatchDurabilityComponents.Distribution,
            out var distributionLevel);
        var guarantee = (baseGuarantee, hasDistribution, distributionLevel) switch
        {
            (WorkflowDispatchReadinessGuarantee.DurableReady, false, _) =>
                WorkflowDispatchReadinessGuarantee.DurableReady,
            (WorkflowDispatchReadinessGuarantee.DurableReady, true, WorkflowDispatchDurabilityLevel.Durable) =>
                WorkflowDispatchReadinessGuarantee.DistributedReady,
            (WorkflowDispatchReadinessGuarantee.ProcessLocal, false, _) =>
                WorkflowDispatchReadinessGuarantee.ProcessLocal,
            (WorkflowDispatchReadinessGuarantee.ProcessLocal, true, WorkflowDispatchDurabilityLevel.ProcessLocal) =>
                WorkflowDispatchReadinessGuarantee.ProcessLocal,
            _ => WorkflowDispatchReadinessGuarantee.Unsafe
        };
        var componentNames = hasDistribution
            ? WorkflowDispatchDurabilityComponents.Required.Append(WorkflowDispatchDurabilityComponents.Distribution)
            : WorkflowDispatchDurabilityComponents.Required;
        var components = componentNames
            .Select(component => contributions.TryGetValue(component, out var level)
                ? new WorkflowDispatchReadinessComponent(
                    component,
                    component == WorkflowDispatchDurabilityComponents.Resumption && guarantee == WorkflowDispatchReadinessGuarantee.ProcessLocal
                        ? WorkflowDispatchDurabilityLevel.ProcessLocal
                        : level,
                    guarantee == WorkflowDispatchReadinessGuarantee.ProcessLocal || level == WorkflowDispatchDurabilityLevel.ProcessLocal
                        ? "process-local"
                        : "durable")
                : new WorkflowDispatchReadinessComponent(component, null, $"missing-{component}"))
            .ToArray();
        var reasonCodes = components
            .Where(component => component.ReasonCode != "durable")
            .Select(component => component.ReasonCode)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return ValueTask.FromResult(new WorkflowDispatchReadinessReport(
            guarantee,
            guarantee is WorkflowDispatchReadinessGuarantee.DurableReady or
                WorkflowDispatchReadinessGuarantee.DistributedReady,
            components,
            reasonCodes));
    }
}

internal sealed class ProcessLocalCheckpointEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Checkpoint;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.ProcessLocal;
}

internal sealed class ProcessLocalDispatchStoreEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.DispatchStore;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.ProcessLocal;
}

internal sealed class ProcessLocalOutboxEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Outbox;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.ProcessLocal;
}

internal sealed class ProcessLocalSchedulerEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Scheduler;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.ProcessLocal;
}
