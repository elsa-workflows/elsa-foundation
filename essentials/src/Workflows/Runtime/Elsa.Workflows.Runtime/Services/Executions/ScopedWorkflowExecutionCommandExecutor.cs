using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>
/// Host-lifetime bridge from an in-process actor mailbox to one scoped runtime execution graph per command.
/// </summary>
/// <remarks>
/// The actor provider and its mailboxes intentionally outlive request scopes. This bridge is therefore the only
/// runtime command executor they retain: every command opens a fresh asynchronous scope, resolves the scoped
/// <see cref="WorkflowSchedulerCommandRouter"/>, awaits the complete enqueue/drain operation, and disposes the scope.
/// </remarks>
internal sealed class ScopedWorkflowExecutionCommandExecutor(IPersistenceOperationScopeFactory scopeFactory) : IWorkflowExecutionCommandExecutor, IIngressCommandWitness
{
    public async ValueTask<bool> HasIngressWitnessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken)
    {
        if (NativeIngressWaitTargetEvidence.Origin(envelope) is { } waitOrigin)
        {
            await using var waitScope = await scopeFactory.CreateAsync(waitOrigin.Scope, cancellationToken);
            var waitStore = waitScope.ServiceProvider.GetRequiredService<IRuntimeIngressDeliveryStore>();
            var wait = await waitStore.ReadCommittedWaitAsync(waitOrigin, cancellationToken);
            RuntimeIngressWaitCommand.ValidateEnvelope(wait, envelope);
            return await waitScope.ServiceProvider.GetRequiredService<NativeIngressWaitTargetEvidence>()
                .ReconcileAsync(wait.Item, wait.Recipient, wait.Seal, cancellationToken) is not null;
        }
        if (NativeIngressTargetEvidence.Origin(envelope) is not { } origin) return false;
        await using var scope = await scopeFactory.CreateAsync(origin.Scope, cancellationToken);
        var ingress = scope.ServiceProvider.GetRequiredService<IRuntimeIngressDeliveryStore>();
        var committed = await ingress.ReadCommittedRecipientAsync(origin, cancellationToken);
        NativeIngressTargetEvidence.ValidateEnvelope(committed, envelope);
        return await scope.ServiceProvider.GetRequiredService<NativeIngressTargetEvidence>()
            .ReconcileAsync(committed.Item, committed.Recipient, committed.Seal, cancellationToken) is not null;
    }
    public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(
        WorkflowExecutionCommandEnvelope envelope,
        CancellationToken cancellationToken = default) =>
        ProcessAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);

    public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(
        WorkflowExecutionCommandEnvelope envelope,
        WorkflowExecutionCommandDispatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(options);

        await using var scope = await scopeFactory.CreateAsync(
            new PersistenceScope(envelope.Partition.Value),
            cancellationToken);
        if (scope.ServiceProvider.GetService<RuntimeExecutionProtocolRouter>() is { } protocols)
            return await protocols.ProcessAsync(envelope, options, cancellationToken);
        var executor = scope.ServiceProvider.GetRequiredService<WorkflowSchedulerCommandRouter>();
        return await executor.ProcessAsync(envelope, options, cancellationToken);
    }
}
