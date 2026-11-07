# R2 contract preparation: nested trigger ownership

**Status:** Integration code in progress, unbuilt/unexecuted and unpublished. Source-shape preparation r2 had three clean independent reviews. The current auxiliary diff adds concrete Core/provider/runtime-retention consumers, canonical EF source installation, fingerprints, strict structural wire reads and an existing-material-family retention reader. This expanded diff still requires independent contract review, migrations, provider qualification and actual native/producer integration. The published baseline remains `d9678af6f859cefa5f307f22f2a806857bf94a0e`. The new store is registered only in the complete EF composition; no router or delivery producer calls it yet. Source claims/seals, terminal retention/compaction and the native target protocol remain outstanding. No public/persisted ABI adoption, completed R2 capability or final native-placement decision is claimed.

## Concrete composition case

The accepted scope combines ordinary activities/composites as triggers with StateMachine as an ordinary root or nested activity. A nested StateMachine can therefore be the body of an outer trigger. Its waiting Event activity is physically resumed once, yet its causal progress may affect two receivers: the inner machine's trigger and the outer machine's composite trigger. Treating ownership as one replaceable receiver loses the outer obligation. Dispatching once per consumer instead duplicates the same physical wait.

Example: an outer child trigger contains an inner machine; a matching outer parent fallback is also waiting. Input B resumes an inner wait. If the inner reaction immediately reaches its final state and completes the outer child trigger, that child readiness must be included before outer fallback is considered. If the inner reaction reaches a genuine durable wait first, the outer composite has not completed for this input. A missing/faulted continuation is not that durable wait and cannot authorize fallback.

This is an inferred composition consequence of the accepted decisions, not an additional owner preference or a newly approved mechanism.

## Proposed concrete representation and handoff

1. Capture an ordered native trigger-ownership chain for each wait, rather than overwriting an enclosing trigger owner when an inner machine starts its own triggers. Every membership names its exact receiver, visit, operation and activation. The chain comes from committed scheduling lineage and pinned capability; caller metadata cannot create it.
2. Keep one physical resume recipient. Its source manifest can include several consumer memberships only along that actual nested receiver chain. Reject unrelated membership, missing enclosing captures, cycles, foreign scope/pin and conflicting generations. An inner receiver belongs to one exact enclosing trigger activation. The candidate cohort validates shape; actual native ancestry still requires the runtime/provider boundary.
3. Admit a source batch **per target workflow execution**, containing every captured consumer group and each unique physical resume. One normal fenced checkpoint installs all those groups, records their queued obligations and enqueues each physical resume once. Per-consumer admissions that independently construct the same work with different headers would conflict and are insufficient for this case. The batch remains atomic only within its target execution; cross-execution delivery retains ordinary partial/retryable outcomes.
4. Native continuation work carries all applicable group/slot memberships. One activity decision accounts for the same physical work in every relevant group atomically. A group's result and closure remain receiver-specific.
5. Closing an inner group must not create a zero-debt gap in an enclosing group. The inner receiver's admission/wake-up and causally runnable semantic work remain owed to applicable enclosing groups. Exclude the closed group itself from that continuation; retain still-open enclosing memberships. Resolve inner closure/transfer before considering enclosing closure in that same checkpoint.
6. A genuine committed suspension, delayed schedule or other reviewed durable handoff can park the appropriate enclosing segment. A queued callback, Defer result, empty scheduler or drain limit alone cannot. Normal fault/recovery retains owed responsibility rather than treating the child as ineligible.

The physical native-input callback must distinguish a runnable queued evaluation from one dependent on a real durable phase wait. A machine being busy is not sufficient parking evidence. If evaluation really parks behind such a wait, later readiness uses the subsequent actual continuation occurrence and retains the earlier input as provenance; it cannot reopen an already closed group. Missing dependencies or incident recovery remain owed. This boundary needs concrete callback/checkpoint implementation and proof before enabling the shape; this document does not claim that the candidate DTOs solve it.

## Origin and request consistency

Retain the trusted origin independently of recipients: persistence scope, explicit nullable tenant/partition facts, run kind and full test scope. An empty cohort still has that origin. A real provider must key retry/lookup by that full immutable origin and client key, compare every retained fact under current permissions and refuse missing/contradictory authority; it must not recover those facts by inspecting selected recipients. The candidate `MatchesOrigin` comparison is a shape helper, not authorization or a physical lookup proof.

Bind each retained legacy resolution mode to the typed request’s explicit broadcast flag. An all-match resolution cannot be installed for a singleton request, nor may a singleton ambiguity silently suppress an explicit broadcast. Captured routed waits must also satisfy the declared global correlation; the supplemental ordinary ambiguity set can legitimately be larger.

## Legacy correlation case

The ordinary `BookmarkStimulusLookup` matches execution/type/hash/expiry without a correlation predicate. The global lookup applies correlation first. An execution selected globally can therefore have additional ordinary matches outside that global correlation result. Preserve its full ordinary pre-effect match set for legacy ambiguity, in addition to the routed recipient captures. Do not restrict the legacy match set to the correlated cohort or subtract tracked waits to manufacture a singleton. Skip the legacy branch when every routed wait is tracked. Explicit all-match broadcasts retain their existing different cardinality rule.

## Required discriminating proof

- Inner immediate completion plus matching outer fallback: one physical resume; fixed outer ready set includes child completion before selection.
- Inner genuine durable wait: outer group can settle/park without waiting for the entire inner lifecycle; later real cause retains earlier result provenance.
- Inner queued evaluation behind runnable phase work: enclosing debt remains until that runnable work settles or reaches an actual durable boundary.
- Inner evaluation behind a real phase wait versus fault/poison: only the former can establish the reviewed parking boundary; no queue-emptiness shortcut.
- Same-input multiple inner regions, three nested receiver levels, sibling receivers and shared original waits: correct chain membership, single physical work, no unrelated fan-out and atomic predecessor/successor transfer.
- Source seal before admission, duplicate/stale callbacks, failed target batch, unknown commit, re-entry/cancellation/recovery and bounded admission: no partial group installation or duplicate resume; no stale visit reactivation.
- Correlated global recipient plus additional non-correlated ordinary matches: retained ambiguity; explicit broadcast and wholly tracked cases remain distinct.

The candidate constructor cases are unexecuted contract preparation. These source/target/native/provider/cold proofs remain required even if those cases later pass.


## Canonical installation integration now in the auxiliary diff

The source owns one real EF row, without a publishing workflow. Full-origin keyed contention returns the committed original request/selection, including empty selection. Normal root-set grants, exact closure checking, workflow-deletion ownership and test-scope admission share the source's transaction. Mutable material coordination and source-reference envelopes acquire the new retention-reader version under those grants; executable content-addressed bytes stay untouched. Source-reference revisions are locked before final ordinary source validation and source acceptance. Missing or contradictory evidence refuses before a new source is accepted.

A separate ingress table is not automatically visible to an old maintainer. The new RuntimeArtifact reader accounts for canonical ingress roots and their full material closure in guard creation, guarded/unguarded reference cleanup and actual material deletion. Old readers cannot read the marked retained coordination/reference envelopes. Ordinary compatible writes use the gate's observed predecessor until normal finalization; ingress explicitly requires the new material reader and the ingress family to be admitted. The v1 material upcast preserves content; no old fingerprint algorithm is changed.

The initial implementation conservatively retains material for every canonical ingress row. It has no release/compaction writer yet, so it does not claim the final retention policy. Source claims, sealed delivery, terminal target proof and receipt retention must complete that lifecycle before this capability is enabled. Actual migrations and old/new-host/provider/race/cold proof remain required; constructor/fingerprint cases cannot substitute for them.

Input presence is explicit so absent input and JSON null survive storage as different values. New semantic fingerprints normalize JSON property order/numbers and typed timestamp instants while preserving opaque UTF-16 identities. Existing checkpoint/intent/model encodings remain untouched. Core API entries are source-derived and still need compiler/analyzer verification. All current new tests remain unexecuted.


## Installation source-review corrections (not executed qualification)

Generated source starts use a separate `v3` derivation from existing published caller keys (`v1`/`v2`). Within v3, full origin and a generated-source versus scoped-caller domain tag are part of the canonical digest. Artifact-scoped and occurrence-scoped starts are explicit. A sender cannot alias an unkeyed source by supplying the string that an earlier draft used as its generated prefix. Published keyed routing retains its original frozen derivation.

Canonical ingress rows also retain their original execution dependencies in normal execution deletion. Installation requires the actual finalized `RuntimeWorkflowExecution` v3 reader alongside the material v2/source v1 gates. Existing dependent execution envelopes are marked v3 under their normal root lock and ownership CAS, in sorted execution order, inside source acceptance. Content is preserved. Future starts use the gate's admitted version. The v2-to-v3 upcast changes no payload; ordinary writers preserve observed versions. This is the implementer's concrete compatibility correction, not a claim of shared-owner adoption or old-host qualification.

The provider test preparation covers committed empty-winner reuse after reopening, conflicting keyed requests, refusal before source/ownership writes under either predecessor retention gate, and install-then-delete for existing and subsequently started executions. These are unexecuted. Actual concurrency/deadlock controls, predecessor binaries, migrations, lost acknowledgement and cold host delivery remain required.
