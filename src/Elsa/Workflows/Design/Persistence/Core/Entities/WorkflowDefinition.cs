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
    /// <summary>
    /// Optional organizational placement. This is mutable persistence metadata; it is deliberately
    /// not part of <see cref="IWorkflowDefinition"/> or authored workflow state.
    /// </summary>
    public string? FolderId { get; set; }

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
    public static WorkflowDefinition From(IWorkflowDefinition source)
    {
        var definition = new WorkflowDefinition
        {
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            // DeletedAt is on IWorkflowDefinition (a definition-level lifecycle metadatum), so a
            // source-driven soft-delete propagates through the reconciler's create path (spec 085 FR-008).
            DeletedAt = source.DeletedAt,
        };

        // DeletedReason is persistence-only (not on IWorkflowDefinition), so it can only be carried
        // across when the source is itself a materialised entity.
        if (source is WorkflowDefinition entity)
        {
            definition.DeletedReason = entity.DeletedReason;
            // Reconciliation reads an existing persistence entity before applying source content.
            // Preserve the server-owned placement in that path; new source imports remain Unfiled.
            definition.FolderId = entity.FolderId;
        }

        return definition;
    }
}
