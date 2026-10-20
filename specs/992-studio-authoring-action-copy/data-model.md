# Data Model: Studio Authoring Action Copy and Toolbar Consistency

This feature introduces no domain entity, persisted field, API payload, workflow contract, SDK type, or lifecycle state. It presents existing client state more truthfully. The canonical meanings of mutable authoring records and immutable versions remain in the [Elsa glossary](../../docs/glossary/elsa.md); this document does not redefine them.

## Existing Activity Definition action state

The authoring view uses existing Activity Definition, draft-list, and version-list responses. The action is derived from confirmed coverage, state, and allowed actions:

| UI action | Required confirmed evidence | Existing destination/operation |
|---|---|---|
| Continue editing draft | The active-draft list is complete and contains exactly one active draft; that exact draft allows edit-draft. This branch takes precedence even if the current head is unreadable or create-draft is denied. | Open the editor for that exact draftId. |
| Create draft from current version | Complete confirmed active-draft list contains zero active drafts; the definition names a current head; that exact immutable version is resolved and allows clone-draft; definition allows create-draft. | Clone using that exact versionId as sourceVersionId. |
| Create draft | Complete confirmed active-draft list contains zero active drafts; the definition explicitly has no current head; definition allows create-draft. | Open the existing starting point with no source version. |
| Open drafts | None of the direct branches above can be confirmed. | Open the existing drafts chooser and retain exact draft identities. |

A draft list is complete only when its existing request is for the initial page, has no query error or active fetch, has no privacy failure, and the response has neither hasMore nor a continuation token. An unconfirmed list or unreadable/unconfirmed current head must not be treated as an empty list or an absent head. These are presentation-selection rules over existing evidence; no record shape changes.

## Existing workflow Test Run state

- The transient Test Run action opens the existing setup flow. It uses a module-supplied submit label when present and otherwise the approved generic fallback.
- Existing workflow input values, trigger/start option and mode, module-provided field labels, and their field associations remain unchanged.
- The existing setup warning remains before submission.
- TestRunStatus consumes the existing typed workflow test-run view and exposes a direct callback into Runtime details. Its entire accessible status, stale-draft cue, and accepted-runtime-issue information remain available.
- Save/run/publication status variants affect presentation geometry only. They do not change TestRunView, requests, callback identity, runtime behavior, or persisted data.

## Existing publication and save state

- Workflow publication retains the current remembered mode: review or direct publish. “Review & publish” is the entry copy for the review mode; “Publish” mode remains distinct.
- Activity publication review retains its preflight, status, continuation, and completion phases. “Review & publish” is the entry copy; a prepared review continues as “Continue publication review.”
- “Save now” retains its existing immediate flush behavior. Pending, failed, stale, or unknown outcomes retain accurate non-success status.

## Data-contract decision

No contracts artifact is needed. The change consumes the existing client-side types and API behavior without modifying any externally visible interface.

