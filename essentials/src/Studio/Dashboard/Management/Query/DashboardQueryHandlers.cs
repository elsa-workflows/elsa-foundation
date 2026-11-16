using Elsa.Events.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Events;
using Elsa.Studio.Dashboard.Core.Exceptions;

namespace Elsa.Studio.Dashboard.Management.Query;

internal sealed class CollectDashboardDatasets(IEnumerable<IDashboardDatasetExtension> extensions) : IEventHandler<DashboardDatasetsCollecting>
{
    public async Task Handle(DashboardDatasetsCollecting context, CancellationToken cancellationToken)
    {
        var sources = extensions.ToArray();
        DashboardOwnerIndex.Build(sources, source => source.Ownership);
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var contribution = new DashboardDatasetsCollecting(context.Scope);
            await source.CollectAsync(contribution, cancellationToken);
            foreach (var dataset in contribution.Datasets)
            {
                if (dataset is null || !source.Ownership.Any(owner => owner.DatasetId == dataset.Id && owner.OwnerModuleId == dataset.OwnerModuleId))
                    throw new DashboardQueryException("DATASET_OWNER_CONFLICT", "A dataset declaration does not match its registered owner.");
                if (context.Datasets.Any(existing => existing.Id == dataset.Id))
                    throw new DashboardQueryException("DATASET_OWNER_CONFLICT", "Dataset ownership configuration is invalid or ambiguous.");
                context.Datasets.Add(dataset);
            }
        }
    }
}

internal sealed class ValidateDashboardQuery(IEnumerable<IDashboardQueryValidationExtension> extensions) : IEventHandler<DashboardQueryValidating>
{
    public async Task Handle(DashboardQueryValidating context, CancellationToken cancellationToken)
    {
        var owner = DashboardOwnerIndex.Resolve(extensions, source => source.Ownership, context.Query.Dataset);
        cancellationToken.ThrowIfCancellationRequested();
        await owner.ValidateAsync(context, cancellationToken);
    }
}

internal sealed class ExecuteDashboardQuery(IEnumerable<IDashboardQueryExecutionExtension> extensions) : IEventHandler<DashboardQueryExecuting>
{
    public async Task Handle(DashboardQueryExecuting context, CancellationToken cancellationToken)
    {
        var owner = DashboardOwnerIndex.Resolve(extensions, source => source.Ownership, context.Query.Dataset);
        cancellationToken.ThrowIfCancellationRequested();
        await owner.ExecuteAsync(context, cancellationToken);
    }
}

internal sealed class DrillDashboardQuery(IEnumerable<IDashboardQueryDrillExtension> extensions) : IEventHandler<DashboardQueryDrilling>
{
    public async Task Handle(DashboardQueryDrilling context, CancellationToken cancellationToken)
    {
        var owner = DashboardOwnerIndex.Resolve(extensions, source => source.Ownership, context.Query.Dataset);
        cancellationToken.ThrowIfCancellationRequested();
        await owner.DrillAsync(context, cancellationToken);
    }
}
