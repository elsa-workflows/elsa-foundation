# Tasks: Bounded Workflow Executable Cache

**Input**: Design documents from `specs/092-workflow-executable-cache/`

**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/executable-cache.md`, `quickstart.md`

**Tests**: Required by FR-014/SC-006 and the repository constitution. Author focused behavior tests first and confirm the intended failures before implementation.

## Phase 1: Setup and Evidence

- [x] T001 Mark GitHub issue #625 in progress and link the spec 091 formal-review evidence that makes it a merge prerequisite
- [x] T002 Record the 200-request warm p95, first-after-ready p95, and rematerialization comparison in `docs/reports/shell-activation-performance-2026-07.md`
- [x] T003 Confirm the durable-store registration and all SQLite/PostgreSQL runtime/unified composition call sites

## Phase 2: Foundational Tests

- [x] T004 [P] Add options validation/default tests in `tests/Elsa/Workflows/Runtime/Tests/CachingWorkflowExecutableStoreTests.cs`
- [x] T005 [P] Add bounded telemetry contract tests for hit, miss, eviction, and provider-load outcomes in `tests/Elsa/Workflows/Runtime/Tests/CachingWorkflowExecutableStoreTests.cs`
- [x] T006 Add a controllable counting executable-store fake supporting delayed, null, failed, and cancelled loads in `tests/Elsa/Workflows/Runtime/Tests/CachingWorkflowExecutableStoreTests.cs`

## Phase 3: User Story 1 - Reuse Immutable Executables (Priority: P1)

**Goal**: Reuse positive immutable executable lookups and coalesce concurrent misses.

**Independent Test**: Repeated and concurrent lookup of one provider artifact performs exactly one provider load while all callers receive the expected executable.

### Tests

- [x] T007 [US1] Add repeated-hit and different-key lookup tests and confirm they fail before implementation
- [x] T008 [US1] Add same-key concurrent miss, per-waiter cancellation, synchronous completion, null retry, and failure retry tests and confirm they fail before implementation

### Implementation

- [x] T009 [US1] Add validated defaults in `src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutableCacheOptions.cs`
- [x] T010 [US1] Add bounded counters/histogram vocabulary in `src/Elsa/Workflows/Runtime/Core/Diagnostics/WorkflowExecutableCacheTelemetry.cs`
- [x] T011 [US1] Implement positive lookup reuse and same-key in-flight coalescing in shared `WorkflowExecutableCache` state with scoped `CachingWorkflowExecutableStore` adapters

## Phase 4: User Story 2 - Bounded and Correct Lifecycle (Priority: P2)

**Goal**: Bound memory and preserve durable mutation/restart authority.

**Independent Test**: Capacity, recency, save/delete, list, and provider-recreation tests prove no stale or cross-lifetime state.

### Tests

- [x] T012 [US2] Add deterministic LRU promotion/capacity-one eviction tests and confirm they fail before implementation
- [x] T013 [US2] Add provider-authoritative successful/failed save and delete race tests, retention lease/deletion-guard pass-through coverage, plus list-does-not-populate tests and confirm they fail before implementation
- [x] T014 [US2] Add Groundwork DI wrapping, disabled-mode, cross-request-scope reuse, persistence-partition isolation, privileged-mutation invalidation, and replacement-cache empty-state tests and confirm they fail before registration changes

### Implementation

- [x] T015 [US2] Complete locked bounded LRU admission/promotion/eviction in `WorkflowExecutableCache.cs` and provider-authoritative mutation/invalidation semantics in its store adapters
- [x] T016 [US2] Register the concrete Groundwork store, optional cache adapter, independently scoped miss loader, and privileged/global invalidation adapter in the shared Groundwork runtime-store registration
- [x] T017 [US2] Thread enabled/capacity settings through SQLite/PostgreSQL runtime and unified provider features without changing custom or in-memory stores

## Phase 5: User Story 3 - Observable and Configurable Operation (Priority: P3)

**Goal**: Expose safe controls and bounded evidence for tuning and rollback.

**Independent Test**: Settings select direct/decorated stores, invalid capacity fails, and every cache path emits only approved bounded dimensions.

- [x] T018 [US3] Complete telemetry emission and verify no high-cardinality dimensions
- [x] T019 [US3] Document knobs, defaults, provider scope, route-cache relationship, and rollback in `contracts/executable-cache.md` and the shared performance report
- [x] T020 [US3] Run focused Runtime and Groundwork behavior/registration tests with all required branches green

## Phase 6: Combined Performance and Delivery

- [ ] T021 Build a warning-free Release server and run the final 20-boot frozen-data lane
- [ ] T022 Run the final 200-request warm lane and verify first-after-ready ≤750 ms p95 and warm ≤50 ms p95
- [ ] T023 Update raw provenance, results, residual costs, and follow-up recommendations in `docs/reports/shell-activation-performance-2026-07.md`
- [x] T024 Run every affected solution test lane plus full `Elsa.Server.slnx` build
- [x] T025 Run up to five formal review/fix iterations across specs 091/092, resolving all critical/high findings
- [ ] T026 Complete both task lists, run `speckit-analyze`, and resolve all critical/high cross-artifact findings
- [ ] T027 Push the branch, open one PR with `Closes #624` and `Closes #625`, converge required automated reviews and CI, and merge without bypassing protections
- [ ] T028 Audit main, both issue states, and the merged benchmark/documentation artifacts

## Dependencies and Execution Order

- T001-T003 establish tracking and evidence.
- T004-T006 block story implementation.
- US1 supplies lookup/coalescing; US2 adds lifecycle and provider composition; US3 completes operations.
- T021-T028 require all stories and also close spec 091's remaining review/delivery tasks.

## Commit Discipline

- Commit the work-unit documents separately from implementation.
- Preserve RED evidence before each implementation slice.
- Keep cache core, provider composition, and final evidence as reviewable commits.
- Do not merge while either spec's performance budget or required review findings remain open.
