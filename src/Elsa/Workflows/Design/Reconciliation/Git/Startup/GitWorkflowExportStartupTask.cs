using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Design.Reconciliation.Git.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Design.Reconciliation.Git.Startup;

/// <summary>
/// Runs the Writer-only export reconciler once at startup, under a single-node distributed lock so the
/// single writer stays honest across instances. Ordered after the import reconcile task ([Order(2)]) so
/// a bootstrap import seeds a fresh catalog before the first export. Registered only when Role=Writer.
/// </summary>
[SingleNodeTask]
[Order(3)]
public sealed class GitWorkflowExportStartupTask(
    ILogger<GitWorkflowExportStartupTask> logger,
    IGitWorkflowExporter exporter,
    IDistributedLockProvider distributedLockProvider)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var lockKey = nameof(GitWorkflowExportStartupTask);
        await using var @lock = await distributedLockProvider.TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(30), cancellationToken);

        if (@lock is null)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Could not retrieve lock '{key}'; it was claimed by another instance", lockKey);
            return;
        }

        await exporter.ExportAsync(cancellationToken);
    }
}
