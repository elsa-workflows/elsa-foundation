using CShells;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Services;

/// <summary>Reconstructs derived usage after the activity and workflow catalogs have reconciled.</summary>
[Order(6)]
internal sealed class RebuildActivityUsageStartupTask(
    IServiceScopeFactory scopeFactory,
    IDistributedLockProvider locks,
    ShellSettings settings,
    ILogger<RebuildActivityUsageStartupTask> logger) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Optional maintenance owns its lock failure policy; the generic SingleNodeTask
            // wrapper would fail shell activation before this error boundary could run.
            await using var handle = await locks.AcquireLockAsync(
                $"elsa:activity-usage-rebuild:{settings.Id.Name}", cancellationToken: cancellationToken);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.HandleLostToken);
            // The projection holds every tenant's facts. Isolate maintenance authority from the
            // startup scope and bind it before resolving any of the rebuild's scoped stores.
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.PrivilegedAcrossScopes(new("activity-usage-startup-rebuild")));
            await scope.ServiceProvider.GetRequiredService<IActivityDependencyProjectionRebuildCoordinator>()
                .RebuildAsync(linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Usage is derived evidence: failure must not prevent authoring or execution. A
            // missing snapshot remains unavailable, rather than being replaced with empty facts.
            logger.LogError(exception, "Activity usage could not be rebuilt at shell start. The previous snapshot, if any, remains available.");
            cancellationToken.ThrowIfCancellationRequested();
        }
    }
}
