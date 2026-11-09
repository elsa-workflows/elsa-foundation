# Data Model: Connection Creation Disclosure

This change introduces no persisted entity or public request shape. The following are the existing in-form concepts needed to describe the behavior.

## Connection draft

- **Identity**: connection name, selected connection type, and selected authentication scheme.
- **Authored settings**: the operator-entered HTTP address and optional setting values. Blank values and untouched runtime defaults remain absent from the outgoing settings.
- **Effective placement**: the placement used for presentation when the setting is absent or whitespace-only. For the built-in HTTP API-key contract this is Header; a nonblank supported value is recognized case-insensitively without trimming.
- **Relevant name**: Header name for effective Header placement or Query name for Query placement. Both authored values remain in draft state while only the relevant optional control is displayed.
- **Advanced disclosure**: whether More options is open, whether an actual advanced error is forcing it open, and whether automatic reveal should remain open through correction until the operator manually collapses it. Automatic reveal is transient UI state and is not a persisted user preference.

## Credential source

- **Existing Secret**: an eligible Secret reference obtained from metadata. The stored value is never read for display.
- **New value**: a credential explicitly entered for this connection draft.
- **Submission invariant**: submit the selected source only; retain existing source switching, denial, unavailable, retry, cancellation, endpoint isolation, and duplicate-submit behavior.

## Descriptor metadata

- **Connection type and scheme**: determine whether the exact built-in HTTP/API-key specialization applies.
- **Setting metadata**: field name, requiredness, sensitivity, and any advertised metadata. Required fields remain visible and validated even when optional counterparts are hidden or grouped.
- **Boundary**: descriptor metadata is not expanded with placement choices or synthetic address fields. Runtime placement values are an existing contract; unfamiliar metadata continues through generic behavior.
