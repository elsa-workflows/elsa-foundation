namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Creates a new <c>WorkflowDefinitionDraft</c> by deep-copying State + Layout from an existing
/// <c>WorkflowDefinitionVersion</c>. Per Unit C FR-028.
/// </summary>
/// <remarks>
/// <para>
/// <b>Semantics.</b> Reads the source Version's State and the records from its
/// <c>WorkflowDefinitionVersionLayout</c> sibling, deep-copies them, then delegates to
/// <see cref="ICreateDraftCommand"/> — the single Draft-origination path — passing the copied
/// State, copied layout, and the source Version id. The create command generates the new DraftId,
/// acquires the per-Draft distributed lock (<c>workflow-draft:{NewDraftId}</c>), runs the
/// validation gate, flushes atomically, and publishes the lifecycle events. NodeIds carry 1:1
/// from the Version into the new Draft per FR-009a copy semantics. Cloning never crosses
/// Definitions — the target is always the source Version's own Definition.
/// </para>
/// <para>
/// <b>Provenance.</b> The source Version id is persisted as the immutable, optional
/// <c>WorkflowDefinitionDraft.SourceVersionId</c> column and surfaced on the single origination
/// event <c>OnDraftCreated.SourceVersionId</c>. There is no separate
/// <c>OnDraftClonedFromVersion</c> event: a fresh Draft and a cloned Draft share one origination
/// event, distinguished by whether <c>SourceVersionId</c> is set.
/// </para>
/// <para>
/// <b>Cardinality vs. existing Drafts.</b> Whether a new Clone-from-Version may coexist with an
/// existing Draft of the same Definition (replace? coexist? throw?) is a later unit's call. This
/// command ships without enforcing a cardinality rule.
/// </para>
/// </remarks>
public interface ICloneDraftFromVersionCommand
{
    Task<string> Execute(string sourceVersionId, CancellationToken cancellationToken = default);
}
