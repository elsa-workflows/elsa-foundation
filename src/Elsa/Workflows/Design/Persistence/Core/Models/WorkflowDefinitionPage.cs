using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>Provider-neutral bounded query for browsing workflow definitions.</summary>
public sealed record WorkflowDefinitionPageQuery(
    int PageSize,
    string? SearchTerm = null,
    WorkflowDefinitionPageState State = WorkflowDefinitionPageState.Active,
    string? ContinuationToken = null)
{
    public void Validate()
    {
        if (PageSize is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(PageSize), PageSize, "Workflow-definition page size must be between 1 and 100.");
        if (ContinuationToken is not null && string.IsNullOrWhiteSpace(ContinuationToken))
            throw new ArgumentException("The workflow-definition continuation token cannot be blank.", nameof(ContinuationToken));
    }
}

/// <summary>Lifecycle filter applied to a bounded workflow-definition browse.</summary>
public enum WorkflowDefinitionPageState
{
    Active,
    Deleted,
    All
}

/// <summary>A bounded workflow-definition page and its opaque forward continuation.</summary>
public sealed record WorkflowDefinitionPage(
    IReadOnlyList<WorkflowDefinition> Items,
    string? NextContinuationToken);
