using Elsa.Persistence.Core.Queries;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Filters;

/// <summary>
/// A specification to use when finding workflow definitions. Only non-null fields will be included in the conditional expression.
/// </summary>
public class WorkflowDefinitionFilter
{
    /// <summary>
    /// Filter by the ID of the workflow definition.
    /// </summary>
    public string? Id { get; set; }

    /// <summary>
    /// Filter by the IDs of the workflow definitions.
    /// </summary>
    public ICollection<string>? Ids { get; set; }

    /// <summary>
    /// Filter by the name of the workflow definition.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Filter by the name or id of the workflow definition.
    /// </summary>
    public string? SearchTerm { get; set; }

    /// <summary>
    /// Filter by the description of the workflow definition.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Filter by the names of the workflow definitions.
    /// </summary>
    public ICollection<string>? Names { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to include tenant matching in the filter.
    /// </summary>
    public bool? TenantAgnostic { get; set; }

    /// <summary>
    /// Filter logical workflow definitions by Workflow Design-owned marker assignments.
    /// </summary>
    public IReadOnlyCollection<WorkflowDefinitionMarkerTagClause>? MarkerTagClauses { get; set; }

    /// <summary>
    /// Projects this filter onto the closed, provider-neutral <see cref="Query{TEntity}"/> spec. This is
    /// the shape every persistence provider can translate.
    /// </summary>
    public Query<WorkflowDefinition> ToQuery()
    {
        if (MarkerTagClauses is { Count: > 0 })
            throw new NotSupportedException(
                "Marker tag clauses require a workflow-definition store with native marker projection query support.");

        var query = Query<WorkflowDefinition>.All();

        if (Id != null) query.And(x => x.Id, QueryOp.Equal, Id);
        if (Ids != null) query.And(x => x.Id, QueryOp.In, Ids);
        if (Name != null) query.And(x => x.Name, QueryOp.Equal, Name);
        if (Names != null) query.And(x => x.Name, QueryOp.In, Names);
        if (!string.IsNullOrWhiteSpace(SearchTerm))
            query.And(x => x.Name, QueryOp.Contains, SearchTerm)
                .Or(x => x.Description, QueryOp.Contains, SearchTerm)
                .Or(x => x.Id, QueryOp.Contains, SearchTerm);
        if (Description != null) query.And(x => x.Description, QueryOp.Equal, Description);

        if (TenantAgnostic == true) query.IgnoreTenant();

        return query;
    }
}
