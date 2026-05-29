using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>
/// Mutable sibling of <see cref="WorkflowDefinitionDraft"/>. Owns the Draft's designer-layout
/// records keyed by <c>NodeId</c>. FK to the owning Draft (1:0..1). Mirrors the Draft's
/// mutability per Unit C FR-006a — mutates whenever an <c>OnActivityMovedInDraft</c> event
/// fires (the layout event folds into the Draft event stream per FR-018). Cascade-deletes
/// with the Draft per Unit C FR-029 atomicity and research item R5.
/// </summary>
public sealed class WorkflowDefinitionDraftLayout : TenantEntity, IWorkflowDefinitionLayout
{
    public string WorkflowDefinitionDraftId { get; set; } = default!;

    public WorkflowDefinitionDraft? WorkflowDefinitionDraft { get; set; }

    public ICollection<DesignMetadataRecord> Records { get; set; } = [];

    IEnumerable<IDesignMetadataRecord> IWorkflowDefinitionLayout.Records => Records.AsEnumerable();
}
