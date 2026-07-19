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
    int PageSize = WorkflowDefinitionListQuery.DefaultPageSize,
    int? Offset = null)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 100;

    public int Skip => Offset ?? checked((Page - 1) * PageSize);

    public void Validate()
    {
        ArgumentNullException.ThrowIfNull(Filter);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(Page);
        if (Offset < 0)
            throw new ArgumentOutOfRangeException(nameof(Offset), "Offset cannot be negative.");
        if (PageSize is <= 0 or > MaximumPageSize)
            throw new ArgumentOutOfRangeException(nameof(PageSize), PageSize, $"Page size must be between 1 and {MaximumPageSize}.");
        if (Offset is null && (long)(Page - 1) * PageSize > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(Page), Page, "Page and page size produce an unsupported offset.");
        if (Filter.MarkerTagClauses is { Count: > 16 })
            throw new ArgumentOutOfRangeException(nameof(Filter.MarkerTagClauses), "At most 16 marker tag clauses are supported.");
        foreach (var clause in Filter.MarkerTagClauses ?? [])
        {
            clause.Validate();
            if (clause.TagDefinitionId.Contains('|', StringComparison.Ordinal))
                throw new ArgumentException("Marker tag identities cannot contain the projection delimiter.", nameof(Filter.MarkerTagClauses));
        }
        if (Filter.ControlledTagClauses is { Count: > 20 })
            throw new ArgumentOutOfRangeException(nameof(Filter.ControlledTagClauses), "At most 20 controlled tag clauses are supported.");
        foreach (var clause in Filter.ControlledTagClauses ?? [])
        {
            clause.Validate();
            if (clause.TagDefinitionId.ContainsAny('|', '=')
                || (clause.ControlledValueIds ?? []).Any(value => value.ContainsAny('|', '=')))
                throw new ArgumentException("Controlled tag identities cannot contain projection delimiters.", nameof(Filter.ControlledTagClauses));
        }
    }
}

public sealed record WorkflowDefinitionPage(IReadOnlyList<WorkflowDefinition> Items, int TotalCount);
