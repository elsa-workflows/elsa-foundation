using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Design.Reconciliation.Core;
using Elsa.Workflows.Design.Reconciliation.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Services;

[SingleNodeTask]
[Order(2)]
public sealed class WorkflowsVersionReconcilerStartupTask(
    ILogger<WorkflowsVersionReconcilerStartupTask> logger,
    IWorkflowVersionReconciler reconciler,
    IDistributedLockProvider distributedLockProvider,
    IOptions<WorkflowVersionReconcilerStartupTaskOptions> options)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var lockKey = nameof(WorkflowsVersionReconcilerStartupTask);
        var timeout = TimeSpan.FromMilliseconds(options.Value.LockTimeoutMs);
        await using var @lock = await distributedLockProvider.TryAcquireLockAsync(lockKey, timeout, cancellationToken);

        if (@lock is null)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Could not retrieve lock '{key}'; because it was claimed by another instance", lockKey);

            return;
        }

        await reconciler.Reconcile(cancellationToken);
    }
}
