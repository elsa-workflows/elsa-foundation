using Elsa.Primitives.Entities;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>
/// Write-once sibling of <see cref="WorkflowDefinitionVersion"/>. Owns the Version's
/// designer-layout records keyed by <c>NodeId</c>. FK to the owning Version (1:0..1).
/// Immutability enforced via <c>PropertySaveBehavior.Throw</c> in the EF Core entity
/// configuration (Unit C FR-006a) — re-laying out an already-promoted Version requires
/// minting a new Version. Layout is part of authoring, not a mutable side-channel.
/// </summary>
public sealed class WorkflowDefinitionVersionLayout : TenantEntity, IWorkflowDefinitionLayout
{
    public string WorkflowDefinitionVersionId { get; init; } = default!;

    public WorkflowDefinitionVersion? WorkflowDefinitionVersion { get; init; }

    public IEnumerable<DesignMetadataRecord> Records { get; init; } = [];

    IEnumerable<IDesignMetadataRecord> IWorkflowDefinitionLayout.Records => Records;

    public IEnumerable<ActivityPresentationRecord> ActivityPresentation { get; init; } = [];

    IEnumerable<IActivityPresentationRecord> IWorkflowDefinitionLayout.ActivityPresentation =>
        ActivityPresentation;
}
