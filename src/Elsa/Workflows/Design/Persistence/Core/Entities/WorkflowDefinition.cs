using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>
/// Represents a versioned workflow definition. Drafts of this Definition point back via
/// <c>WorkflowDefinitionDraft.WorkflowDefinitionId</c> — the relationship is held on the
/// child side (1 Definition : many Drafts), no inverse pointer is kept on the parent.
/// </summary>
public sealed class WorkflowDefinition : TenantEntity, IWorkflowDefinition
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Timestamp at which this logical workflow definition was soft-deleted.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }

    /// <summary>
    /// Optional human/system authored reason for soft deletion.
    /// </summary>
    public string? DeletedReason { get; set; }

    /// <summary>
    /// Creates and returns a shallow copy of the workflow definition.
    /// </summary>
    public IWorkflowDefinition ShallowClone() => (WorkflowDefinition)MemberwiseClone();

    /// <summary>Builds the persistence entity from any <see cref="IWorkflowDefinition"/> (e.g. a factory read-model).</summary>
    public static WorkflowDefinition From(IWorkflowDefinition source) =>
        new()
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
        };
}
