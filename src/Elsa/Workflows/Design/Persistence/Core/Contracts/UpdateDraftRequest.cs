using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// The complete desired Draft state submitted to <see cref="IUpdateDraftCommand"/> (Unit 2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Full-state-always (FR-001).</b> The caller computes the entire desired Draft —
/// the same way the designer already holds it — and submits it wholesale. There is no
/// patch/partial mode: "move one activity" and "rewrite the whole graph" use the identical
/// call; the desired State + layout are assigned wholesale (last-writer-wins). Per-diff
/// mutation-event publication is retired until an event-sourcing consumer exists (spec 002
/// FR-017/FR-018), so no per-concept events fire on this path today.
/// </para>
/// <para>
/// <b>Layout is separate from State (§E2.9.2, FR-001a).</b> Designer-layout records live on
/// the <c>WorkflowDefinitionDraftLayout</c> sibling, never inside
/// <see cref="WorkflowDefinitionState"/>. <see cref="Layout"/> carries the complete desired
/// layout-record set beside <see cref="State"/>.
/// </para>
/// </remarks>
public sealed record UpdateDraftRequest(
    string DraftId,
    WorkflowDefinitionState State,
    IReadOnlyCollection<DesignMetadataRecord> Layout,
    IReadOnlyCollection<ActivityPresentationRecord>? ActivityPresentation = null
);
