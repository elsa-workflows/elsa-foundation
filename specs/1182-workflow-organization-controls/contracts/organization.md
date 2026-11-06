# Organization availability contract

## Configuration

The existing `WorkflowsDesignApi` feature exposes Boolean `WorkflowOrganizationEnabled`, default true, display label **Workflow organization**. It follows existing shell configuration precedence and typed Features draft/review/Apply authorization. Applying the setting is bound to the reviewed backend/shell/revision; another backend and unrelated configuration remain unchanged.

## Discovery and requests

With no library store, existing unsupported behavior remains. With a library store, `workflow-definitions-page` and `workflow-definitions-query` remain available in both settings. The query relation establishes extended option support; base paging alone does not. Existing library hosts may use `workflow-organization` as the compatibility signal for that grammar. When false, folder/tag/view/favorite/organization/move relations are not advertised. Their direct endpoints fail with structured 501 before an organization write. Definition creation with explicit folder placement fails before creating a definition; ordinary unfiled creation remains supported.

Paged requests retain search, lifecycle/publication state, sort, page size, continuation, trusted visibility and authorization. Disabled folder/Unfiled/descendant/tag/favorite predicates are rejected with structured 400. The legacy flat list also refuses recognized organization query keys while disabled; it does not offer organization filtering. Existing read response shapes stay compatible; organization-only values and permission hints are suppressed while authorization still consults stored organization where required.

## Studio

Availability is per operation and selected backend session. Missing organization operations hide their controls and metadata without disabling ordinary paging. Unavailable saved column choices remain stored and return when supported.

A folder/Unfiled/tag/favorite/view constraint that its backend cannot support pauses the scoped browse and displays an explanation plus **Clear unavailable organization filters**. Recovery clears only unsupported organization constraints and their selected-view association. It preserves search, lifecycle, supported publication state and sort, backend, unrelated query values and saved column choices. On older paged hosts, unavailable extended query options are also explained and require explicit recovery. Supported operations in a partially implemented backend remain usable.

Current composed-host capability refresh retires old routes/dialogs/results. Module-level context/authority changes also invalidate pending reads and close obsolete organization/move targets. Stale operations are never interpreted as successful organization changes.

## Retention and compatibility

Disabling changes availability only. Saved library data, resource permissions, ownership, publication/executable identities and appearance choices remain intact. Defaults retain today's UI and API behavior. Existing published execution does not depend on this Design API setting.
