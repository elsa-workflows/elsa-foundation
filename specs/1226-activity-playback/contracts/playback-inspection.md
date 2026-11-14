# Proposed dependency and UI contract

**Status**: dependency/UI boundary for spec1226 FR026. This documentation unit implements no endpoint or recording support. The [accepted S14a handoff](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6858916502) scopes separately authored [runtime source in draft #4384](https://github.com/elsa-workflows/elsa-foundation/pull/4384); that source is not delivered or qualified production support. Complete the provider and acceptance controls with the spec1211 owner before dependent playback implementation.

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

### Implementation controls from the existing history stack

Source inspection of S05b (`d90ce8549116`) and S06b (`84765a2320de`) identifies these additional controls for the coordinated implementation. These heads are review inputs, not a delivered or qualified playback base.

- Define eligible first committed occurrence observations explicitly. Intrinsic execution can first record completion; requiring `ActivityStarted` would omit it. Continuation/resume facts for the same exact execution do not add occurrences. Preserve the actual observation kind rather than invent a captured start.
- Freeze scope membership as well as event positions. The current in-memory reader consults `HistoryParents`, which later events can enrich. An ancestry link learned after admission cannot silently change an old sequence's count or members. Unknown ancestry and a known root are different states.
- Preserve or invalidate first-occurrence identity across retention. Recomputing a minimum over retained lifecycle rows can reintroduce an old execution at its later continuation. Existing event identity/position receipts alone do not retain the occurrence association.
- Do not infer located internal segments from the current global `HasGaps` flag. Admit only a range whose continuity the owner can prove; otherwise report its limitation explicitly.
- Distinguish a committed event prefix from source closure. Later materialized observations stay outside the admitted membership and appear through the separate newer-history indication; existing settlement fields do not prove that every expected source fact has arrived.
- Provide provider-owned bounded ordinal/count/anchor access. Repeatedly fetching event pages until enough distinct executions have accumulated is not ordinal seeking. Any derived index must share the existing history publication transaction/lock and replay identity.

Qualification must include parent enrichment after admission, purge across the original occurrence observation, repeated continuations, intrinsic-first completion, late occurrence materialization, and a distant ordinal lookup measured independently of the preceding event count. Reuse the existing history snapshot, cursor-binding, atomic replay, authorization-before-read and paired-capability controls. The durable provider must implement the same facet before normal composed-host readiness is claimed.

## Private Studio boundary

`ActivityPlaybackSequenceSource` (working name, private) adapts the delivered facet into admission/page/new-record observations and supports cancellation. Unavailable admission renders an explanation; it is not a playable empty run. It never falls back to timestamp-sorted current projections.

`useActivityPlayback` owns context, bounds, cursor/pending target, timer generation and transport state. `confirmSelection(entry, generation)` resolves exact pinned scope through the existing workbench and returns confirmation or explicit failure; another tick cannot outrun that confirmation. A committed cursor never switches to the latest occurrence by authored node alone.

Transport intents: Previous, Next, Play/Pause, speed, ordinal seek, explicit bound refresh and optional Replay. Manual timeline/diagram/scope/incident/detail navigation pauses first. All responses are context/generation checked. Automatic ticks preserve Timeline and control focus; explicit Activity selection is allowed only after pausing.

The controls expose a named group, visible position/timestamp/state, separate live-update status, native buttons/select/range, accurate range labels and meaningful accessible value text. Unknown total/segment bounds are not rounded into a fictional full-run percentage. Use Studio public UI primitives and tokens.

## Readiness gate

The legacy `BrowseActivityExecutions` endpoint currently supplies neither this admission nor this seek/coverage contract. Spec1211 proposals and source-clean queued PRs do not satisfy it. The feature cannot be production-enabled or accepted until the owner facet exists on the integration base and its actual composed behavior passes the prerequisite controls.
