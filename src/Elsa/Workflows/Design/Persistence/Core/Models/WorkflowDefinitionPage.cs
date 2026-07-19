using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

public enum WorkflowDefinitionLifecycleScope
{
    Active,
    Deleted,
    All
}

public enum WorkflowDefinitionSortBy
{
    Name,
    LastModifiedAt,
    CreatedAt
}

public enum WorkflowDefinitionSortDirection
{
    Asc,
    Desc
}

/// <summary>
/// Provider-neutral, bounded read request for the workflow-definition list.
/// </summary>
public sealed record WorkflowDefinitionListQuery(
    WorkflowDefinitionFilter Filter,
    WorkflowDefinitionLifecycleScope Scope = WorkflowDefinitionLifecycleScope.Active,
    WorkflowDefinitionSortBy SortBy = WorkflowDefinitionSortBy.Name,
    WorkflowDefinitionSortDirection SortDirection = WorkflowDefinitionSortDirection.Asc,
    int Page = 1,
    int PageSize = WorkflowDefinitionListQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;

    public int Skip => checked((Page - 1) * PageSize);

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Page);
        if (PageSize is <= 0 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(PageSize), PageSize, $"Page size must be between 1 and {MaximumPageSize}.");
        if ((long)(Page - 1) * PageSize > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Page), Page, "Page and page size produce an unsupported offset.");
    }
}

public sealed record WorkflowDefinitionPage(IReadOnlyList<WorkflowDefinition> Items, int TotalCount);
