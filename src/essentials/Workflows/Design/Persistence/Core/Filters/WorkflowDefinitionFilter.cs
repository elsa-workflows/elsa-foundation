using Elsa.Workflows.Design.Persistence.Core.Entities;

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

}
