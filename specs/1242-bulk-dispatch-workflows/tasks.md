# Tasks: Elsa 4 counterpart for Elsa 3 BulkDispatchWorkflows

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

Implementation starts after the coordinator approves the spec. Phases 1–3 do not depend on spec 1243. Phase 4 waits
for spec 1243's parallel ForEach.

## Phase 1: Rewrite skeleton (US1)

- [ ] T001 [US1] Finish research item R-1 (plan §1): can a rewrite change the node's target type and give it a
  synthesized body child? Record the answer in the PR, and move the lowering into `Elsa3FlowchartGraphBuilder` if it
  cannot. The outcome handling goes in the graph builder either way (plan §5).
- [ ] T002 [US1] Add `Elsa3NodeRewriteKind.BulkDispatchAsLoop`, register the rewrite in `Elsa3MappingFeature`, and
  replace the `NoCounterpart("BulkDispatchWorkflows")` entry in
  `extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs` with the entry shape of plan §1: every
  member classified once, the non-loop inputs as `Structure`, and the two ports as `Elsa3MemberKind.Port`. Add
  `ChildCompleted` and `ChildFaulted` (kind `port`) to `extensions/Elsa3/tests/Mapping/Tests/Fixtures/elsa3-activity-surface.json`.
- [ ] T003 [US1] Add `Elsa3BulkDispatchAsLoopRewrite`.
  - Classify `WaitForCompletion` as FR-002 does: missing, `null`, a valueless literal, a case-insensitive literal, or
    an expression.
  - For a literal false, emit a `ForEach` with a `DispatchWorkflow` body (FR-002, FR-004, ids from plan §2). Drop the
    `ChildCompleted`/`ChildFaulted` branches here, by not adding them as children, with
    `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED`.
  - For every other wait, raise `NoCounterpart` with guidance naming spec 1243.
- [ ] T004 [US1] Outcomes, in the graph builder (plan §5): `Done` → loop `Done`. Drop the `Canceled` and `Completed`
  connections with the warning `ELS3-BEHAVIOUR-DEAD-CONNECTION-REMOVED` (FR-012, the literal-false rows). Refuse
  `Canceled` or `Completed` toward a `WaitAll` join with `ELS3-ACTIVITY-BULK-DISPATCH-OUTCOME-ROUTE`; add that code
  here.
- [ ] T005 [P] [US1] Tests in `Elsa3BulkDispatchLoweringTests`:
  - the ForEach shape;
  - kept ids;
  - outcomes and dropped branches, including the `WaitAll` refusal;
  - every `WaitForCompletion` classification, including the corpus literal `"False"`.

## Phase 2: Members (US3, US4)

- [ ] T006 [US3] Per-item `Inputs` expression (FR-006, plan §3):
  - for a known target, and a refusal with `ELS3-ACTIVITY-BULK-DISPATCH-TARGET-UNKNOWN` (added here) for an unknown
    target;
  - with a static `Input` that is a literal, an Object expression or JavaScript, and a refusal for any other language;
  - with warning `ELS3-BEHAVIOUR-BULK-DISPATCH-ITEM-INPUTS` (FR-011). Add that code to `Elsa3ActivityDiagnosticCodes`
    and to `raisedByRewrites` in `Elsa3ActivityMapCoverageTests` here.
  - The generated script uses `Object.entries`/`filter`/`Object.fromEntries` with literal names, with no computed
    member access (plan §3).
- [ ] T007 [US3] Correlation function: handle the absent and literal cases (FR-008). Resolve
  `get{DefaultItemInputKey}()` and `args.<camelKey>` through name resolution as plan §4 says, and register the loop scope so `ResolutionDiffers` fires for outer-loop reads.
- [ ] T008 [US4] Member rules: `WorkflowDefinitionId` literal only; `DefaultItemInputKey` literal, null or absent
  (`Item`); `Items` null as unbound, any non-null literal refused; a missing wait emits literal true on the body dispatch;
  `StartNewTrace`/`ChannelName` Removed. `CancelChildOnParentCancellation` is synthetic false (FR-005, FR-007,
  FR-009).
- [ ] T009 [P] [US3] [US4] Tests:
  - inputs for map, scalar, null and array items, run through the Elsa 4 evaluator and through publish validation
    (no VF-STATE-002);
  - a case-mismatched member gets the declared spelling;
  - an undeclared default key is omitted;
  - correlation with `getItem()`, with `args.item`, and with a custom key;
  - a workflow variable named `Item`: `getItem()` reads that variable, as in Elsa 3, and `args.item` still reads the item;
  - a read of an enclosing loop's current value inside the lowered loop, from a branch, the correlation function or a
    JavaScript static `Input` → `ResolutionDiffers`;
  - each static `Input` form;
  - a non-null literal `Items` (array, object, string);
  - each member refusal.

## Phase 3: Diagnostics and docs

- [ ] T010 In `Elsa3ConversionDiagnosticsTests`, add a literal-false BulkDispatchWorkflows case that converts. Keep
  the minimal `no-counterpart:BulkDispatchWorkflows` case (no `WaitForCompletion`, so the waiting path) until T014, and
  keep ParallelForEach. Each new code is added in the task that first raises it (T004, T006, T016), so the
  map-coverage test holds at every phase (FR-013).
- [ ] T011 [P] Update `extensions/Elsa3/src/Mapping/README.md`, `specs/236-elsa3-activity-map/member-map.md` and
  `specs/236-elsa3-activity-map/contracts/diagnostic-codes.md` (FR-013, FR-014).
- [ ] T012 [P] Add the "one child per item" section to `essentials/src/Activities/DispatchWorkflow/Runtime/README.md`
  (FR-015).
- [ ] T013 Convert `performance-concurrency.json` from the S-15 corpus locally, confirm that it publishes, and record
  the result. Hand FR-016 over to S-15 through the shared notes. The hand-over asks S-15 to run the concurrency suite on
  the Elsa 4 host, with its child `simple-write-hello-world.json` in the same import, and to record in its ledger
  whether all 300 children reach `Finished` (SC-001, the run-time part).

## Phase 4: Waiting path (US2) — after spec 1243

- [ ] T014 [US2] For a wait that is missing, a literal true or an expression, emit the parallel ForEach with a flowchart
  body: dispatch `Completed`/`Faulted` → the converted branches (FR-003). Use 1243's current-item read in T007's
  resolution if it differs from `variables.currentItem`. Remove the `no-counterpart:BulkDispatchWorkflows` test case
  (T010) here.
- [ ] T015 [US2] Outcomes in the waiting case:
  - the `Completed` rows of FR-012: drop without a diagnostic when `Done` reaches every target and none is a `WaitAll`
    join, without duplicate connections; refuse with `ELS3-ACTIVITY-BULK-DISPATCH-OUTCOME-ROUTE` (added in T004) when
    it leads to a target `Done` does not reach;
  - raise spec 1243's `ELS3-BEHAVIOUR-PARALLEL-FOREACH-BOUNDED` with the bulk-dispatch text (FR-010).
- [ ] T016 [US2] Child-result reads in the branches → `ELS3-ACTIVITY-BULK-DISPATCH-CHILD-RESULT-READ`, raised in name
  resolution (FR-012, plan §6). Add the code here.
- [ ] T017 [P] [US2] Tests:
  - the parallel shape;
  - an expression wait that evaluates false runs no branch;
  - a cancelled child runs no branch;
  - each `Completed` row;
  - each child-result read.
- [ ] T018 [US2] End-to-end conversion of a synthetic `acme.*` waiting definition. Hand the adjusted composition root
  over to S-15 (SC-002).

## Phase 5: Finish

- [ ] T019 Run the scoped tests and the gate checker, then set the spec status to Implemented in the PR that finishes
  phase 4. If phase 4 is split off, set the status in the PR that finishes phase 3, with phase 4 noted as pending.
