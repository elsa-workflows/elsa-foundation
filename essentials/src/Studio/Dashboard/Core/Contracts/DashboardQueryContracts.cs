using Elsa.Studio.Dashboard.Core.Events;
using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Core.Contracts;

/// <summary>Single scoped replacement contract for the native query entry point.</summary>
public interface IDashboardQueryService
{
    ValueTask<IReadOnlyList<DashboardDatasetDeclaration>> DiscoverAsync(DashboardRequestScope scope, CancellationToken cancellationToken = default);
    ValueTask<DashboardQueryValidation> ValidateAsync(DashboardRequestScope scope, DashboardQueryRequest request, CancellationToken cancellationToken = default);
    ValueTask<DashboardQueryResult> ExecuteAsync(DashboardRequestScope scope, DashboardQueryRequest request, CancellationToken cancellationToken = default);
    ValueTask<DashboardQueryResult> DrillAsync(DashboardRequestScope scope, DashboardDrillRequest request, CancellationToken cancellationToken = default);
}

public interface IDashboardDatasetExtension
{
    IReadOnlyCollection<DashboardDatasetOwner> Ownership { get; }
    ValueTask CollectAsync(DashboardDatasetsCollecting context, CancellationToken cancellationToken = default);
}

public interface IDashboardQueryValidationExtension
{
    IReadOnlyCollection<DashboardDatasetOwner> Ownership { get; }
    ValueTask ValidateAsync(DashboardQueryValidating context, CancellationToken cancellationToken = default);
}

public interface IDashboardQueryExecutionExtension
{
    IReadOnlyCollection<DashboardDatasetOwner> Ownership { get; }
    ValueTask ExecuteAsync(DashboardQueryExecuting context, CancellationToken cancellationToken = default);
}

public interface IDashboardQueryDrillExtension
{
    IReadOnlyCollection<DashboardDatasetOwner> Ownership { get; }
    ValueTask DrillAsync(DashboardQueryDrilling context, CancellationToken cancellationToken = default);
}
