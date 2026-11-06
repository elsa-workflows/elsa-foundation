using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Services;

internal sealed class DistributedRuntimeTestRoutingBoundary : IRuntimeTestExecutionRoutingBoundary
{
    public ValueTask<bool> CanRouteTestEntryAsync(WorkflowTestEntry entry, IWorkflowExecutionActorProvider actors,
        CancellationToken cancellationToken = default) => actors is DistributedWorkflowExecutionActorProvider actual
        ? actual.CanRouteTestEntryAsync(entry, cancellationToken) : ValueTask.FromResult(false);
}

internal sealed record RuntimeTestTransportRegistrations(Func<ServiceDescriptor[]> Read);

internal sealed class ValidateRuntimeTestTransportComposition(RuntimeTestTransportRegistrations registrations) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (registrations.Read().Count(x => x.ServiceType == typeof(IExecutionCommandTransportTestBoundary)) > 1)
            throw new InvalidOperationException("Selected test routing permits at most one command-transport qualification owner.");
        return Task.CompletedTask;
    }
}
