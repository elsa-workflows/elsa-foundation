# Data model: Studio list identity and return context

## Persistence boundary

No persisted, backend, wire, or public SDK model is added. The model below describes browser-local context and presentation of existing Workflows domain records.

## Activity Definition collection entry

Each browser history entry that opens an Activity Definition collection owns its own view context:

- Search input as entered and the settled/debounced value currently used for requests.
- Provider selection as entered and the settled value currently used for requests.
- Authority filter: Designed, Built-in, or All.
- Page size: 10, 25, or 50.
- The observed opaque server cursor stack and the current cursor position, scoped to the existing authorization and filter/page-size context.
- The originating collection/history-entry identity needed to distinguish it from other entries.

This state is ephemeral. A cursor is never synthesized, decoded, or added to a new URL field. A reload or invalidated cursor may require restarting from the first page while retaining still-valid search, provider, authority, and page size. Per-entry state alone is not authorization proof. On auth-session or base-URL changes, restoration must pass through the existing local redaction seam and must not recommit previously displayed rows; this feature does not redesign global query caching or authentication.

## Existing row identity presentation

Each row continues to represent its existing workflow definition, activity definition, or workflow run. Its presentation may distinguish:

- Primary scan fields: human-readable name plus the current primary status/context.
- Secondary technical identities: exact existing identifiers, progressively disclosed and copied without transformation.
- Existing domain actions and capabilities, which remain owned by their current list and authorization checks.

Presentation state must not change the underlying ID, lifecycle, permission, paging, or action contracts. Nested disclosure/copy interaction is separate from row activation.

## Domain-specific boundaries

- Workflow Definitions keep their current folder/unfiled scope, lifecycle views, selection, Move, Delete, Restore, and permanent Delete behavior.
- Activity Definitions keep draft/published summaries, authority filtering, Create/Refresh/Tools and workbench operations; no selection or lifecycle collection actions are introduced.
- Runs keep filtering, selection, details, permission checks, confirmation, and query return behavior; only row action styling and identity density are in scope.
