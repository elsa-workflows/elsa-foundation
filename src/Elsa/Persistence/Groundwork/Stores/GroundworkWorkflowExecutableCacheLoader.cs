using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Stores;

/// <summary>
/// Loads an executable in an independently owned persistence scope so a coalesced cache miss never
/// retains the request scope that initiated it.
/// </summary>
internal sealed class GroundworkWorkflowExecutableCacheLoader(
    IPersistenceOperationScopeFactory operationScopeFactory)
{
    public async ValueTask<WorkflowExecutable?> LoadAsync(
        PersistenceScope persistenceScope,
        string artifactId,
        CancellationToken cancellationToken)
    {
        await using var operationScope = await operationScopeFactory.CreateAsync(
            persistenceScope,
            cancellationToken);
        var store = operationScope.ServiceProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(
            GroundworkRuntimeStoreRegistration.WorkflowExecutableProviderKey);
        return await store.FindAsync(artifactId, cancellationToken);
    }
}
