using CShells.Lifecycle;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Reports the detached-dispatch durability assessment at the shell readiness boundary.</summary>
public sealed class WorkflowDispatchReadinessInitializer(
    IServiceProvider services,
    ILogger<WorkflowDispatchReadinessInitializer> logger) : IShellInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var scopedServices = scope.ServiceProvider;
        var dispatchStore = scopedServices.GetRequiredService<IWorkflowDispatchStore>();
        if (dispatchStore is not IWorkflowDispatchAdmissionStore ||
            dispatchStore is not IWorkflowDispatchCancellationStore)
        {
            throw new InvalidOperationException(
                $"DispatchWorkflow requires '{nameof(IWorkflowDispatchAdmissionStore)}' and " +
                $"'{nameof(IWorkflowDispatchCancellationStore)}' on the configured workflow-dispatch store.");
        }
        var outboxStore = scopedServices.GetService<IRuntimePostCommitOutboxStore>();
        if (outboxStore is not IRuntimePostCommitOutboxClaimStore ||
            outboxStore is not IRuntimePostCommitOutboxClaimCompletionStore ||
            scopedServices.GetService<IWorkflowDispatchRedriveStore>() is null)
        {
            throw new InvalidOperationException(
                $"DispatchWorkflow finite delivery recovery requires '{nameof(IRuntimePostCommitOutboxClaimStore)}', " +
                $"'{nameof(IRuntimePostCommitOutboxClaimCompletionStore)}', and " +
                $"'{nameof(IWorkflowDispatchRedriveStore)}' from the configured atomic persistence provider.");
        }

        var report = await scopedServices
            .GetRequiredService<IWorkflowDispatchReadinessAssessor>()
            .AssessAsync(cancellationToken);
        if (report.Guarantee is WorkflowDispatchReadinessGuarantee.DurableReady or
            WorkflowDispatchReadinessGuarantee.DistributedReady)
        {
            logger.LogInformation("DispatchWorkflow readiness is {Guarantee}", report.Guarantee);
            return;
        }

        logger.LogWarning(
            "DispatchWorkflow readiness is {Guarantee}; reason codes: {ReasonCodes}",
            report.Guarantee,
            string.Join(",", report.ReasonCodes));
    }
}
