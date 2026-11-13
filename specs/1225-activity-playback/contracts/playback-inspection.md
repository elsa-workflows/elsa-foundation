# Proposed dependency and UI contract

**Status**: proposed boundary for planning; no endpoint, registration, storage model or recording support is implemented. This is the prerequisite required by spec1225 FR026, to be reviewed with the spec1211 runtime-history owner.

## Runtime-owned occurrence sequence facet

Use capability-discovered supported reads, preserving the existing runtime read/structure authorization and independent value permissions. Do not make the legacy live activity-browser contract claim snapshot support.

Admission must identify exact run/scope/filter, a committed/settled upper bound, authoritative occurrence-entry order and a retained contiguous segment. Report capture-from-start/partial/disabled/purged/gap/unknown states accurately. An opaque authenticated session/continuation binds purpose, context, filters, bounds and authorization profile; it is not authority and is revalidated on every read.

Read operations must support:

1. Admit a bounded sequence and locate an existing exact occurrence anchor when eligible.
2. Read a bounded page around a known ordinal, adjacent position or exact identity within that same frozen membership.
3. Return exact occurrence IDs, positions, neighbor identities, optional known count, page/segment bounds and explicit gap/unavailable reasons. No arbitrary whole-history browser preload.
4. Detect newer committed history separately. Explicit paused refresh creates/revalidates a new bound and preserves an eligible selected identity.

The owning reader must prove stable membership despite appends and late materialization; a last timestamp, count, offset or hash-derived execution sequence is not such proof. Retention changes must invalidate/disclose the affected segment rather than shift ordinals silently. Count, selected rows, bounds and coverage must refer to a coherent authorized read. Existing records without the necessary order/coverage do not get fabricated backfill.

Concrete endpoint/signature/provider changes belong to a separately reviewed owner implementation. Required controls include provider restart, concurrent append/retention, equal/out-of-order timestamps, nested/parallel identity, denied scope, cursor-purpose mismatch and bounded distant seek. Preserve existing history capture/checkpoint budgets; do not add another journal or force recording modes.

## Private Studio boundary

`ActivityPlaybackSequenceSource` (working name, private) adapts the delivered facet into admission/page/new-record observations and supports cancellation. Unavailable admission renders an explanation; it is not a playable empty run. It never falls back to timestamp-sorted current projections.

`useActivityPlayback` owns context, bounds, cursor/pending target, timer generation and transport state. `confirmSelection(entry, generation)` resolves exact pinned scope through the existing workbench and returns confirmation or explicit failure; another tick cannot outrun that confirmation. A committed cursor never switches to the latest occurrence by authored node alone.

Transport intents: Previous, Next, Play/Pause, speed, ordinal seek, explicit bound refresh and optional Replay. Manual timeline/diagram/scope/incident/detail navigation pauses first. All responses are context/generation checked. Automatic ticks preserve Timeline and control focus; explicit Activity selection is allowed only after pausing.

The controls expose a named group, visible position/timestamp/state, separate live-update status, native buttons/select/range, accurate range labels and meaningful accessible value text. Unknown total/segment bounds are not rounded into a fictional full-run percentage. Use Studio public UI primitives and tokens.

## Readiness gate

The legacy `BrowseActivityExecutions` endpoint currently supplies neither this admission nor this seek/coverage contract. Spec1211 proposals and source-clean queued PRs do not satisfy it. The feature cannot be production-enabled or accepted until the owner facet exists on the integration base and its actual composed behavior passes the prerequisite controls.
