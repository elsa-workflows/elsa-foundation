using Elsa.Workflows.Runtime.Services.Executables;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;

public sealed partial class EfWorkflowExecutableStore : IRuntimeKernelExecutableRetentionMaterial
{
    async ValueTask<IReadOnlyCollection<string>> IRuntimeKernelExecutableRetentionMaterial.ListPinnedKernelExecutableIdsAsync(
        CancellationToken cancellationToken)
    {
        var scope = accessContextAccessor.Current.RequireScope();
        var births = await EfRuntimeKernelExecutablePinStore.ReadPinnedExecutableIdsAsync(context, scope, cancellationToken);
        var occurrences = await EfRuntimeKernelScheduleOccurrenceStore.ReadPinnedExecutableIdsAsync(context, scope.Value, cancellationToken);
        return [.. births, .. occurrences];
    }
}
