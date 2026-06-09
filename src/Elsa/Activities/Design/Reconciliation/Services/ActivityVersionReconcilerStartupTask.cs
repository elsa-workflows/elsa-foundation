using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Reconciliation.Services;

[SingleNodeTask]
[Order(1)]
public sealed class ActivityVersionReconcilerStartupTask(ILogger<ActivityVersionReconcilerStartupTask> logger, IActivityVersionReconciler reconciler, IDistributedLockProvider distributedLockProvider, IOptions<ActivityVersionReconcilerStartupTaskOptions> options)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var lockKey = nameof(ActivityVersionReconcilerStartupTask);
        var timeout = TimeSpan.FromMilliseconds(options.Value.LockTimeoutMs);
        await using var @lock = await distributedLockProvider.TryAcquireLockAsync(lockKey, timeout, cancellationToken);

        if (@lock is null)
        {
            if(logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Could not retrieve lock '{key}'; because it was claimed by another instance", lockKey);

            return;
        }

        await reconciler.Reconcile(cancellationToken);
    }
}
