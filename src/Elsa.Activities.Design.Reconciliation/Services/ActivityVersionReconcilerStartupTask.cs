using Elsa.Activities.Design.Reconciliation.Core;
using Elsa.Activities.Design.Reconciliation.Options;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Reconciliation.Services;

[SingleNodeTask]
[Order(1)]
public sealed class ActivityVersionReconcilerStartupTask(IActivityVersionReconciler reconciler, IDistributedLockProvider distributedLockProvider, IOptions<ActivityVersionReconcilerStartupTaskOptions> options)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(options.Value.LockTimeoutMs);
        await using var @lock = await distributedLockProvider.TryAcquireLockAsync(nameof(ActivityVersionReconcilerStartupTask), timeout, cancellationToken);

        if (@lock is null)
        {
            return;
        }

        await reconciler.Reconcile(cancellationToken);
    }
}
