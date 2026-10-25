using CShells.Lifecycle;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

internal sealed class WorkflowDispatchControlBoundaryInitializer(IServiceProvider services) : IShellInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetServices<IRuntimeWorkflowControlBoundary>().Count() != 1)
            throw new InvalidOperationException("DispatchWorkflow requires exactly one workflow control-boundary provider.");
    }
}
