using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Design.Provisioning.Core.Contracts;
using Elsa.Workflows.Design.Provisioning.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Provisioning.Services;

[SingleNodeTask]
[Order(2)]
public sealed class WorkflowsVersionProvisionerStartupTask(IWorkflowVersionProvisioner provisioner, IDistributedLockProvider distributedLockProvider, IOptions<WorkflowVersionProvisionerStartupTaskOptions> options)
    : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var timeout = TimeSpan.FromMilliseconds(options.Value.LockTimeoutMs);
        await using var @lock = await distributedLockProvider.TryAcquireLockAsync(nameof(WorkflowsVersionProvisionerStartupTask), timeout, cancellationToken);

        if (@lock is null)
        {
            return;
        }

        await provisioner.Provision(cancellationToken);
    }
}
