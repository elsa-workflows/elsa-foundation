using Elsa.Primitives.Attributes;
using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>
/// Immutable sibling of <see cref="WorkflowDefinitionVersion"/>. Owns the Version's
/// designer-layout records keyed by <c>NodeId</c>. FK to the owning Version (1:0..1).
/// Mirrors the Version's <c>[Immutable]</c> regime per Unit C FR-006a — re-laying out an
/// already-promoted Version requires minting a new Version. Layout is part of authoring,
/// not a mutable side-channel. Unit C FR-006.
/// </summary>
public sealed class WorkflowDefinitionVersionLayout : TenantEntity, IWorkflowDefinitionLayout
{
    [Immutable]
    public string WorkflowDefinitionVersionId { get; init; } = default!;

    public WorkflowDefinitionVersion? WorkflowDefinitionVersion { get; init; }

    [Immutable]
    public List<DesignMetadataRecord> Records { get; init; } = new();

    IReadOnlyList<IDesignMetadataRecord> IWorkflowDefinitionLayout.Records => Records;
}
