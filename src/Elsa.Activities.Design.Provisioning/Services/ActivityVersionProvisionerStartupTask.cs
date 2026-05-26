using Elsa.Activities.Design.Provisioning.Core;
using Elsa.Activities.Design.Provisioning.Options;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Microsoft.Extensions.Options;

namespace Elsa.Activities.Design.Provisioning.Services;

[SingleNodeTask]
[Order(1)]
public sealed class ActivityVersionProvisionerStartupTask(IActivityVersionProvisioner provisioner, IDistributedLockProvider distributedLockProvider, IOptions<ActivityVersionProvisionerStartupTaskOptions> options)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(options.Value.LockTimeoutMs);
        await using var @lock = await distributedLockProvider.TryAcquireLockAsync(nameof(ActivityVersionProvisionerStartupTask), timeout, cancellationToken);

        if (@lock is null)
        {
            return;
        }

        await provisioner.Provision(cancellationToken);
    }
}