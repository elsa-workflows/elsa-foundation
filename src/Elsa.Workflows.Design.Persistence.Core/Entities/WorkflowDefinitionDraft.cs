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
    /// The deserialized <see cref="StateSource"/>
    /// </summary>
    [NotMapped]
    public WorkflowDefinitionState State { get; set; } = default!;

    /// <summary>
    /// Shadow property that contains the serialized state of this draft
    /// </summary>
    public string? StateSource { get; set; }
}
