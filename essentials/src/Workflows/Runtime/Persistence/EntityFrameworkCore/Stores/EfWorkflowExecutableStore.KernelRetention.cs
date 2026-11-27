using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableStore
{
    async ValueTask<IReadOnlyCollection<string>> IWorkflowExecutableStore.ListMaterialRetentionRootIdsAsync(
        CancellationToken cancellationToken)
    {
        var scope = accessContextAccessor.Current.RequireScope(admitPrivileged: true);
        var births = await EfRuntimeKernelExecutablePinStore.ReadPinnedExecutableIdsAsync(context, scope, cancellationToken);
        var occurrences = await EfRuntimeKernelScheduleOccurrenceStore.ReadPinnedExecutableIdsAsync(context, scope.Value, cancellationToken);
        var eventStarts = await EfRuntimeKernelEventPublicationStore.ReadPinnedExecutableIdsAsync(context, scope, cancellationToken);
        return [.. births, .. occurrences, .. eventStarts];
    }
}
