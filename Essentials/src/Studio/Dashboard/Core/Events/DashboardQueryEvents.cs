using Elsa.Events.Core.Contracts;
using Elsa.Studio.Dashboard.Core.Models;

namespace Elsa.Studio.Dashboard.Core.Events;

public sealed class DashboardDatasetsCollecting(DashboardRequestScope scope) : IEvent
{
    public DashboardRequestScope Scope { get; } = scope;
    public ICollection<DashboardDatasetDeclaration> Datasets { get; } = [];
}

public sealed class DashboardQueryValidating(DashboardQueryContext query) : IEvent
{
    public DashboardQueryContext Query { get; } = query;
    public ICollection<DashboardQueryValidation> Results { get; } = [];
}

public sealed class DashboardQueryExecuting(DashboardQueryContext query, DashboardQueryLimits limits) : IEvent
{
    public DashboardQueryContext Query { get; } = query;
    public DashboardQueryLimits Limits { get; } = limits;
    public ICollection<DashboardQueryResult> Results { get; } = [];
}

public sealed class DashboardQueryDrilling(DashboardQueryContext query, DashboardDrillRequest request, DashboardQueryLimits limits) : IEvent
{
    public DashboardQueryContext Query { get; } = query;
    public DashboardDrillRequest Request { get; } = request;
    public DashboardQueryLimits Limits { get; } = limits;
    public ICollection<DashboardQueryResult> Results { get; } = [];
}
