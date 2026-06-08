using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using System.ComponentModel.DataAnnotations.Schema;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

public sealed class WorkflowDefinitionDraft : TenantEntity, IWorkflowDefinitionDraft
{
    /// <summary>
    /// Foreign key to the owning <see cref="WorkflowDefinition"/>. Many Drafts may belong to one
    /// Definition (cardinality enforced at the data-model level; multi-Draft semantics arrive in
    /// a subsequent unit). Replaces the prior <c>WorkflowDefinition.DraftId</c> inverse pointer.
    /// </summary>
    public string WorkflowDefinitionId { get; set; } = default!;

    /// <summary>
    /// Navigation property to the parent <see cref="WorkflowDefinition"/>.
    /// </summary>
    public WorkflowDefinition? WorkflowDefinition { get; set; }

    /// <summary>
    /// Optional foreign key to the <see cref="WorkflowDefinitionVersion"/> this Draft was cloned
    /// from. <c>null</c> for a fresh Draft (the very first Draft of a Definition, with no version
    /// to copy from). Write-once — a Draft's provenance never changes; immutability enforced via
    /// <c>PropertySaveBehavior.Throw</c> in the EF Core entity configuration. There is
    /// deliberately no navigation property: a Draft only needs to know which version it descended
    /// from, not to traverse into it.
    /// </summary>
    public string? SourceVersionId { get; set; }

    /// <summary>
    /// The deserialized <see cref="StateSource"/>
    /// </summary>
    [NotMapped]
    public WorkflowDefinitionState State { get; set; } = default!;

    /// <summary>
    /// Shadow property that contains the serialized state of this draft
    /// </summary>
    public string? StateSource { get; set; }

    /// <summary>
    /// Builds the persistence entity from any <see cref="IWorkflowDefinitionDraft"/> (e.g. a factory
    /// read-model). The saving handler serialises <see cref="State"/> into <c>StateSource</c>.
    /// </summary>
    public static WorkflowDefinitionDraft From(IWorkflowDefinitionDraft source) =>
        new()
        {
            Id = source.Id,
            WorkflowDefinitionId = source.WorkflowDefinitionId,
            State = source.State,
        };
}
