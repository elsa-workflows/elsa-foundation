using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Design.Reconciliation.Services;

/// <summary>
/// Runs one activity version reconciliation pass at shell start, on every node.
/// </summary>
/// <remarks>
/// It takes no lock and is not a <c>[SingleNodeTask]</c> (#2192). Its inputs are node-local, the assemblies and catalogs
/// this node loaded, so a node that skipped the pass because another node held a lock would never reconcile its own. Two
/// nodes running it at once converge instead (#2189): the second one's writes replay or read back the first one's.
/// The pass is host-wide catalog maintenance, so it binds <see cref="PersistenceAccessContext.PrivilegedGlobal"/> in a
/// dedicated scope rather than inheriting the shell's ordinary default partition.
/// </remarks>
[Order(1)]
public sealed class ActivityVersionReconcilerStartupTask(IServiceScopeFactory scopeFactory) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(
            PersistenceAccessContext.PrivilegedGlobal(new("activity-version-reconciliation")));
        await services.GetRequiredService<IActivityVersionReconciler>().Reconcile(cancellationToken);
    }
}
