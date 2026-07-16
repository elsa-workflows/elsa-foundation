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
        var report = await scope.ServiceProvider
            .GetRequiredService<IWorkflowDispatchReadinessAssessor>()
            .AssessAsync(cancellationToken);
        if (report.Guarantee == WorkflowDispatchReadinessGuarantee.DurableReady)
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
