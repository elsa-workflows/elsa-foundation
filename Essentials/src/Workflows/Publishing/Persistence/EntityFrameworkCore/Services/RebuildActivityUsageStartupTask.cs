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
    ILogger<RebuildActivityUsageStartupTask> logger) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Hosts may enumerate startup contributors without composing the task runner or
            // locking. Resolve optional maintenance collaborators only inside its failure boundary.
            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
                PersistenceAccessContext.PrivilegedAcrossScopes(new("activity-usage-startup-rebuild")));
            var locks = scope.ServiceProvider.GetRequiredService<IDistributedLockProvider>();
            var settings = scope.ServiceProvider.GetRequiredService<ShellSettings>();
            // Optional maintenance owns its lock failure policy; the generic SingleNodeTask
            // wrapper would fail shell activation before this error boundary could run.
            await using var handle = await locks.AcquireLockAsync(
                $"elsa:activity-usage-rebuild:{settings.Id.Name}", cancellationToken: cancellationToken);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, handle.HandleLostToken);
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
