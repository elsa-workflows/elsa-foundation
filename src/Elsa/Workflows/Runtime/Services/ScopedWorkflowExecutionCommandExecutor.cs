using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Host-lifetime bridge from an in-process actor mailbox to one scoped runtime execution graph per command.
/// </summary>
/// <remarks>
/// The actor provider and its mailboxes intentionally outlive request scopes. This bridge is therefore the only
/// runtime command executor they retain: every command opens a fresh asynchronous scope, resolves the scoped
/// <see cref="WorkflowSchedulerCommandRouter"/>, awaits the complete enqueue/drain operation, and disposes the scope.
/// </remarks>
internal sealed class ScopedWorkflowExecutionCommandExecutor(IPersistenceOperationScopeFactory scopeFactory) : IWorkflowExecutionCommandExecutor
{
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
        var executor = scope.ServiceProvider.GetRequiredService<WorkflowSchedulerCommandRouter>();
        return await executor.ProcessAsync(envelope, options, cancellationToken);
    }
}
