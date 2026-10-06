# Tasks: Bounded Coalesced Runtime-Store Page Merging

**Input**: Design documents from `/specs/193-bounded-coalesced-pagination/`.

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/coalesced-page-merge.md`, and `quickstart.md`.

**Scope boundary**: These are the implementation tasks for follow-on issue #2393, after lead review and predecessor integration. They do not assign runtime implementation to #2392. The required #2392 pre-fix trace is captured and reviewed in [reference-trace.md](reference-trace.md); #2393 still requires red-before-green implementation proof and the same-fixture after capture.

## Phase 1: Setup

**Purpose**: Confirm exact baseline and scoped project.

- [x] T001 Confirm the reviewed scope, implementation branch, and focused test command in `specs/193-bounded-coalesced-pagination/plan.md`.

## Phase 2: Foundational

**Purpose**: Keep merge semantics and existing continuation behavior as the implementation baseline.

- [x] T002 Record baseline expectations for the existing coalesced paging cases in `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs` before changing production behavior.

## Phase 3: User Story 1 - Read merged pages without repeated inner probes (Priority: P1)

**Goal**: Bound durable-store reads per request and retain fetched candidates during selection and look-ahead.

**Independent Test**: For empty durable rows plus 128 overlay rows at limit 7, the first request reads the empty source no more than once. For multiple earlier overlays plus one durable candidate in the same output page, the candidate is read once and appears once.

### Tests for User Story 1

- [x] T003 [US1] Add the empty durable source with 128 overlays and exact call-count regression to `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs`; demonstrate the assertion fails on the unmodified merger.
- [x] T004 [US1] Add the overlay-before-durable candidate plus has-next retention regression to `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs`; assert exact candidate read count and row order.

### Implementation for User Story 1

- [x] T005 [US1] Implement a request-scoped bounded durable-page buffer and memoized terminal exhaustion in `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs`.

## Phase 4: User Story 2 - Preserve identity and ordering across continuations (Priority: P1)

**Goal**: Preserve ordinal union semantics, overlay replacement/deletion, and fetched-but-unemitted rows across output-page boundaries.

**Independent Test**: Traverse a fixed interleaved durable/overlay fixture with limit 2 until completion and compare all results with the exact sorted logical view; verify the fetched-but-unemitted candidate is not lost or duplicated.

### Tests for User Story 2

- [x] T006 [US2] Add a multi-page fixture with interleaved durable rows, a same-identity replacement, a tombstone, and overlay-only rows to `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs`.
- [x] T007 [US2] Add an output-boundary case that replays the bounded page from before an unconsumed row and filters by last-emitted identity in `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs`.

### Implementation for User Story 2

- [x] T008 [US2] Preserve the pre-batch provider position and last-emitted identity when returning a continuation in `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs`; commit terminal exhaustion only after buffered rows are emitted or suppressed.

## Phase 5: User Story 3 - Keep continuations and cancellation compatible (Priority: P1)

**Goal**: Keep existing continuation decoding/query binding and cancellation behavior while memoizing empty-terminal exhaustion and preserving the existing rejection of an empty page with a continuation.

**Independent Test**: Resume a valid pre-change continuation, reject malformed and wrong-query continuations as before, prove cancellation propagates, mark an empty terminal page exhausted once, and retain the `RuntimeStorePage<T>` constructor rejection for an empty page that carries a continuation.

### Tests for User Story 3

- [x] T009 [US3] Add pre-change token, malformed/wrong-binding, cancellation, and empty-terminal exhaustion cases to `tests/essentials/Workflows/Runtime/Tests/RuntimeCheckpointCoalescingTests.cs`; retain the existing empty-page-with-continuation rejection coverage in `tests/essentials/Workflows/Runtime/Tests/RuntimeStorePageTests.cs`.

### Implementation for User Story 3

- [x] T010 [US3] Keep the existing `crsp1` serialization envelope and memoize empty-terminal exhaustion in `src/essentials/Workflows/Runtime/Services/Coalescing/CoalescingRuntimeStateStores.cs` without changing the public token contract. Leave `CoalescingRuntimeStoreContinuation.cs` unchanged unless implementation review identifies a concrete codec requirement.

## Final Phase: Verification and cross-cutting concerns

**Purpose**: Prove algorithmic boundedness, host compatibility, and the intended before/after distinction.

- [x] T011 Run the focused whole runtime test project and the expected red-then-green mutation proof using `tests/essentials/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj`.
- [x] T012 Run the same representative PostgreSQL Workbench journey before and after the correction with the manually invoked `e2e-tests/http/Capture-RuntimeDbPaging2392Reference.ps1`; record exact output, terminal state, effective cadence, and command trace in `specs/193-bounded-coalesced-pagination/reference-trace.md`.
- [x] T013 Review changed source and docs against `specs/193-bounded-coalesced-pagination/contracts/coalesced-page-merge.md` and the scoped boundary in `specs/193-bounded-coalesced-pagination/plan.md`.

## Dependencies and execution order

- T001 → T002 → T003/T004 → T005 → T006/T007 → T008 → T009 → T010 → T011 → T012 → T013.
- User stories share one merger and one test file, so no tasks are marked parallelizable. Complete the P1 bounded-read slice first, then prove multi-page correctness and compatibility.
- Baseline before-trace execution under #2392 is a prerequisite for #2393's same-fixture after comparison; do not treat this tasks file as evidence that the trace has already run.

## Implementation strategy

1. Start with US1 and record both expected pre-fix call-count failures before editing production code.
2. Implement the bounded read buffer and exhaustion handling, then preserve cursor correctness and overlay semantics across continuation boundaries.
3. Keep the existing continuation envelope and public store interfaces; add only behavioral tests around compatibility and cancellation.
4. Run the affected test project as a whole, then the existing normal-host fixture with identical Coalesced settings/provider on both candidates.
5. Root performs integration/QA gates and owns program state transitions after reviewing the completed #2393 evidence.
