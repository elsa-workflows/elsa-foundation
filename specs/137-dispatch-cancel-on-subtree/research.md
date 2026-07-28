# Research: Cancel Waited Dispatches on Subtree Teardown

## Decision 1: Treat committed local activity cancellation as ownership teardown

**Decision**: A checkpoint locally owns cancellation responsibility for every activity-execution upsert whose resulting status is `Cancelled`.

**Rationale**: Seam-A subtree planning already writes these terminal states into the same atomic checkpoint. The dispatch record's `ParentActivityExecutionId` supplies an exact, durable ownership join. No separate intentional-detach cancellation status or marker exists.

**Alternatives considered**:

- Require BPMN seam-A metadata: couples a provider-neutral runtime lifecycle to one consumer and misses non-BPMN subtree teardown.
- Observe bookmark deletion: bookmarks are cleanup artifacts, not the dispatch ownership contract.
- Add a new activity cancellation intent: duplicates the already-committed authoritative state and changes the checkpoint schema.

## Decision 2: Broaden the existing cancellation enricher trigger

**Decision**: Extend `WorkflowDispatchCancellationEnricher`; do not add a DispatchWorkflow activity callback or a second cancellation service.

**Rationale**: The enricher already atomically derives canonical cancellation requests and post-commit intents, including policy checks, deterministic identity, conflict handling, provider resolution, and committed-outbox recovery. Only its trigger predicate is too narrow.

**Alternatives considered**:

- Have `DispatchWorkflow` observe host-token cancellation: no per-token callback exists and adding one would widen Runtime Core.
- Add a BPMN engine hook: creates a private side channel and fails other seam-A consumers.
- Cancel from subtree cleanup after commit: loses atomic responsibility and introduces a crash gap.

## Decision 3: Scan all parent dispatch pages and filter per record

**Decision**: Keep `ListForParentAsync` unchanged and filter each yielded record by whole-parent or exact local-owner cancellation.

**Rationale**: Query providers currently page by parent workflow, creation time, and dispatch ID. A matching activity may appear after pages containing no match. Continuing the established iterator preserves ordering and non-advancing-cursor protection.

**Alternatives considered**:

- Stop after the first page without a match: misses eligible later dispatches.
- Add a parent-activity query contract: expands every provider for a narrow internal optimization without evidence it is needed.
- Materialize all pages before filtering: adds avoidable memory without improving determinism.

## Decision 4: Preserve existing eligibility and recovery semantics byte-for-byte

**Decision**: After owner selection, retain existing wait-mode, effective propagation policy, nonterminal/marker, committed-outbox fallback, request/intent factory, timestamp, equivalence, conflict, and deduplication logic.

**Rationale**: Issue #998 is a reachability defect, not a redesign of cancellation races. The shipped machinery already handles admission winning, cancellation winning, terminal projection, duplicate delivery, and replay.

**Alternatives considered**:

- Cancel every locally-owned dispatch regardless of mode/policy: violates authored detached and opt-out behavior.
- Suppress terminal/marked replay work: can strand already-committed responsibility after a terminal race.
- Generate a new local-cancel intent identity: duplicates responsibility when whole-parent and local cancellation coincide.

## Decision 5: Prove the integration boundary at checkpoint state

**Decision**: Add branch-complete unit tests at the enricher boundary and rely on existing seam-A execution tests that prove subtree teardown writes `Cancelled` activity states.

**Rationale**: The implementation boundary is the checkpoint state-change set. Existing Activities Runtime tests already prove subtree cancellation and bookmark cleanup; existing BPMN tests prove boundary-interrupt seam-A teardown. Rewiring the separate BPMN and DispatchWorkflow integration harnesses would test shipped composition at disproportionate cost.

**Alternatives considered**:

- Build a new full BPMN-plus-dispatch harness: large composition-root expansion for no new behavior below the checkpoint contract.
- Test only the happy path: misses exact-owner isolation, replay, terminal, policy, and paging branches required by the constitution.

## Compatibility and Scope Findings

- No public contract, state schema, extension point, feature registration, or provider implementation changes.
- Whole-parent cancellation remains unchanged.
- Fire-and-forget and propagation opt-out remain unchanged.
- The separate carried `CancelLiveWork`-through-seam-A follow-up remains out of scope; this unit consumes any local activity cancellation already carried through seam A.
- The fix is provider-neutral and closes #998 for BPMN call activity without modifying the BPMN engine.
