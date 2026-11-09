# UI contract: Studio list context and identity

This is a browser-visible behavior contract for existing Studio lists. It adds no backend endpoint or public SDK type.

## List hierarchy and semantics

Workflow Definitions, Activity Definitions, and Runs use a consistent page inset, title/action order, search and filter hierarchy, readable table density, and truthful loading/empty/error treatment. Existing domain-specific controls remain in their current list. Workflow and Activity collection tables retain meaningful table/row/column semantics; an implementation does not replace them with generic grid semantics.

## Identity and nested interaction

The default row prioritizes the human-readable name and primary status/context. Secondary technical identifiers are available through an accessible progressive disclosure. Each identifier remains exact and copyable by keyboard; visual truncation never changes the copied value. Activating or using a disclosure/copy control must not activate the containing Activity Definition row by pointer, Enter, or Space. Existing row navigation and workflow menus/selection keep their own behavior.

## Search, filters, and paging

- Search remains live and retains the existing settled request behavior.
- Workflow Definitions preserve the accepted #3068 route/history-backed browse session, lifecycle views, folders/unfiled scope, selection, and Move behavior.
- Activity Definitions preserve Designed/Built-in/All authority, provider selection, page sizes 10/25/50, and server-issued opaque continuation scoped to the existing authorization and filter context.
- No new cursor URL representation is introduced.

## Activity Definition return context

Opening a workbench/editor from an Activity Definition collection associates the collection context with that specific browser history entry. Editor Back and browser Back return to the entry with its search and provider input/settled values, authority, page size, and valid cursor position. Opening from multiple distinct entries and returning through browser history restores the matching context each time; one origin cannot overwrite another. A direct workbench entry has a safe collection fallback.

On reload, authority change, or invalid/expired server continuation, retain valid filters and page size and use the existing first-page restart/recovery behavior. Per-entry browser state is not authorization proof. On auth-session or base-URL changes, restoration passes through the existing local redaction seam and cannot recommit previously displayed rows. Denied/unavailable reads retain existing redaction. A failed refresh retains the last confirmed page only while it remains authorized; an expired snapshot restarts instead of presenting stale continuation data. This contract does not imply a global query-cache or authentication rewrite.

## Domain action boundaries

- Workflow Definitions retain selection, Move, Delete, Restore, permanent Delete, permission checks, and confirmations.
- Activity Definitions gain no collection selection, Delete, Restore, or other lifecycle actions.
- Runs retain their permission-gated Delete behavior and confirmation; the refinement is visual styling and row identity density.

## Responsive and themed behavior

At laptop and narrow widths, in light and dark themes, long names and IDs remain scannable without hiding exact accessible/copyable values or obscuring actions. Empty and error states state the actual outcome; they do not conflate an empty result with a denied, unavailable, or failed read.
