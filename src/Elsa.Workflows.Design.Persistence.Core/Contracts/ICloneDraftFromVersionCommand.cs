namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Creates a new <c>WorkflowDefinitionDraft</c> by deep-copying State + Layout from an existing
/// <c>WorkflowDefinitionVersion</c>. Per Unit C FR-028.
/// </summary>
/// <remarks>
/// <para>
/// <b>Semantics.</b> Generates a new DraftId; acquires the per-Draft distributed lock
/// (<c>workflow-draft:{NewDraftId}</c>) on the new id; copies the source Version's State and
/// the records from its <c>WorkflowDefinitionVersionLayout</c> sibling into a new
/// <c>WorkflowDefinitionDraftLayout</c>. NodeIds carry 1:1 from the Version into the new Draft
/// per FR-009a copy semantics. Publishes <c>OnDraftClonedFromVersion</c> with the new Draft id,
/// the source Version id, and the Definition id read off the source Version (cloning never
/// crosses Definitions — the target is always the source Version's own Definition).
/// </para>
/// <para>
/// <b>Back-pointer field deferred.</b> FR-028(d) names a provisional
/// <c>ClonedFromVersionId</c> FK on the new Draft entity but flags final field allocation as
/// Unit D's call. Unit C does NOT add the field; the audit trail lives on the
/// <c>OnDraftClonedFromVersion</c> lifecycle event (event-sourcing stream + audit
/// subscribers). Unit D may add a persistent back-pointer if the cardinality model requires it.
/// </para>
/// <para>
/// <b>Cardinality vs. existing Drafts.</b> Whether a new Clone-from-Version may coexist with an
/// existing Draft of the same Definition (replace? coexist? throw?) is Unit D's call per
/// FR-028. Unit C ships the command without enforcing a cardinality rule.
/// </para>
/// </remarks>
public interface ICloneDraftFromVersionCommand
{
    Task<string> Execute(string sourceVersionId, CancellationToken cancellationToken = default);
}
