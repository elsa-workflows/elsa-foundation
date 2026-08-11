using Elsa.Persistence.Core.Design;

namespace Elsa.Workflows.Design.Persistence.Core.Contracts;

/// <summary>
/// Creates a new <c>WorkflowDefinitionDraft</c> by deep-copying State + Layout from an existing
/// <c>WorkflowDefinitionVersion</c>. Per Unit C FR-028.
/// </summary>
/// <remarks>
/// <para>
/// <b>Semantics.</b> Reads the source Version's State and the records from its
/// <c>WorkflowDefinitionVersionLayout</c> sibling, copies them, then enters the provider's shared
/// Draft-origination lifecycle path with the source Version id. That path generates the new
/// DraftId, acquires the per-Draft distributed lock (<c>workflow-draft:{NewDraftId}</c>), runs the
/// validation gate, flushes atomically, and publishes the lifecycle events. Providers need not
/// implement this by delegating to <see cref="ICreateDraftCommand"/>. NodeIds carry 1:1 from the
/// Version into the new Draft per FR-009a copy semantics. Cloning never crosses Definitions — the
/// target is always the source Version's own Definition.
/// </para>
/// <para>
/// <b>Provenance.</b> The source Version id is persisted as the immutable, optional
/// <c>WorkflowDefinitionDraft.SourceVersionId</c> column and surfaced on the single origination
/// event <c>DraftCreated.SourceVersionId</c>. There is no separate
/// <c>DraftClonedFromVersion</c> event: a fresh Draft and a cloned Draft share one origination
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
    Task<string> Execute(
        DesignOperationKey operationKey,
        string sourceVersionId,
        CancellationToken cancellationToken = default);
}
