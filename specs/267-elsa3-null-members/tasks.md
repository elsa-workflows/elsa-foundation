# Tasks: Elsa 3 converter: a null activity member converts to what Elsa 3 ran with

**Input**: Design documents from `specs/267-elsa3-null-members/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/null-member-diagnostic.md, quickstart.md

**Tests**: required. Spec SC-003, SC-004 and FR-008 ask for them; framework §2.23 requires behaviour tests for the
normaliser change. The application declares no TDD cadence, so tests follow implementation within each story; the
PR's bite-proof shows each new test red without the change.

**Build rule**: build and test only `Extensions/Elsa3/tests/Mapping/Tests` and
`Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests` (shared machine).

## Phase 1: Setup

- [x] T001 Re-create the throwaway corpus measurement from the session scratchpad as
  `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/ThrowawayJTestCorpusMeasure.cs`, run it on this
  branch before any code change, and confirm the 8 / 2 / 2 baseline in spec.md still holds (never committed)

## Phase 2: Foundational (blocks every story)

- [x] T002 Add `NullUnconvertible = "ELS3-ACTIVITY-NULL-UNCONVERTIBLE"` with its summary to
  `Extensions/Elsa3/src/Models/Elsa3ActivityDiagnosticCodes.cs` (contract: contracts/null-member-diagnostic.md)
- [x] T003 Add `Elsa3NullRule` (`NoBinding`, `TypedNull`, `Emit`, `Refuse`), the `Elsa3NullConversion(Rule, Value,
  Reason, WhenBound)` record and the optional trailing `OnNull` parameter of `Elsa3MemberRule` in
  `Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3MemberRule.cs` (data-model.md)
- [x] T004 Add the null helpers (`NullUnbound`, `NullAsNull`, `NullEmits(value)`, `NullRefused(reason, whenBound)`)
  to `Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Rules.cs`
- [x] T005 Add `CreateNull(typeAlias)` and `IsNullLiteral(value)` to
  `Extensions/Elsa3/src/Mapping/Conversion/Elsa3TypedValues.cs` (research R-3)
- [x] T006 Set `OnNull` on every Carry and Narrowed-contract input in
  `Extensions/Elsa3/src/Mapping/ActivityMap/Elsa3BuiltInActivityMap.Entries.cs`, row for row from the spec's
  per-member table (R-6, R-8, R-9)

## Phase 3: User Story 1 - null members publish and run as in Elsa 3 (P1) 🎯 MVP

**Goal**: a JSON `null` member never reaches the state mapper as an untyped binding.

**Independent test**: import, publish and run a workflow with RunJavaScript `possibleOutcomes: null`, `result: null`
and WriteLine `text: null`.

- [x] T007 [US1] In `Extensions/Elsa3/src/Mapping/Conversion/Elsa3ActivityNormalizer.cs`, `ApplyRule`: rewrite a
  deliberate literal null to JSON `null` (R-5); for an input rule with `OnNull`, apply it before the rule (no binding,
  typed null with the Elsa 4 input's alias, emitted literal, or refusal with `NullUnconvertible`, honouring
  `WhenBound`); a null Carry output gets no binding (R-10)
- [x] T008 [US1] In the same file, `BindByName`: a null member gets no binding (R-10); `ApplyDefaults`: leave a typed
  null alone (R-8); `IsEmittedDefault`: accept an `OnNull` emitted value (R-9)
- [x] T009 [US1] In `Extensions/Elsa3/src/Mapping/Mappings/Elsa3ActivityToState.cs`: map a literal holding null to
  `new ArgumentValue(null, "Literal")`, and drop any other value it cannot read, null included (D-3, D-4)
- [x] T010 [P] [US1] Fixture `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/Fixtures/null-members.json`:
  a flowchart with a RunJavaScript (`possibleOutcomes: null`, `result: null`) connected by `Done` to a WriteLine
  (`text: null`) and a SetName (`value: null`)
- [x] T011 [US1] `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests/NullMemberPublishTests.cs`:
  import, publish and run the fixture through `Elsa3ImportHost`; the run completes with no incident and runs the
  WriteLine after the RunJavaScript (SC-003)
- [x] T012 [P] [US1] `Extensions/Elsa3/tests/Mapping/Tests/Elsa3NullMemberTests.cs`: typed-null rows (one case per
  row: the input is a `Literal` holding null), no-binding rows (no input), a null output (no output), a null member
  on a member resolved by name (no input), and the deliberate-null shape converting like JSON `null`

## Phase 4: User Story 2 - Elsa 3's value where Elsa 4's default differs (P1)

**Goal**: emitted rows carry Elsa 3's value, with no warning.

**Independent test**: convert each emit row with the member `null`.

- [x] T013 [US2] Extend `Extensions/Elsa3/tests/Mapping/Tests/Elsa3NullMemberTests.cs` with one case per emit row
  (For.OuterBoundInclusive, Delay, Timer, WriteLine, Fault code/category/type, FlowSendHttpRequest
  ExpectedStatusCodes `[200]`, Correlate and SetName `""`), each asserting the literal and no warning diagnostic; plus a flowchart in which a
  FlowSendHttpRequest with `expectedStatusCodes: null` connects its `200` and `Unmatched status code` outcomes

## Phase 5: User Story 3 - refusals with guidance (P2)

**Goal**: each refuse row reports `ELS3-ACTIVITY-NULL-UNCONVERTIBLE` once, naming the member.

**Independent test**: convert each refuse row with the member `null`.

- [x] T014 [US3] Extend `Extensions/Elsa3/tests/Mapping/Tests/Elsa3NullMemberTests.cs` with one case per refuse row:
  the code once, the member in the path, no `ELS3-ACTIVITY-REQUIRED-INPUT-UNBOUND` for the same member; and
  SendHttpRequest ContentType null converting to a typed null when Content is unbound

## Phase 6: Polish & cross-cutting

- [x] T015 [P] `Extensions/Elsa3/tests/Mapping/Tests/Elsa3ActivityMapCoverageTests.cs`: every Carry and
  Narrowed-contract input records `OnNull` or an emitted Elsa 3 default; against the real catalog, a `TypedNull`
  input is nullable and optional and a `NoBinding` input is optional (FR-008, R-7)
- [x] T016 [P] `Extensions/Elsa3/src/Mapping/README.md`: null members and the new code
- [x] T017 [P] `specs/236-elsa3-activity-map/member-map.md`: a "Null members (spec 267)" section with every mapped
  member's null conversion (FR-009, SC-006)
- [x] T018 Run both test projects in full (`Extensions/Elsa3/tests/Mapping/Tests`,
  `Extensions/Elsa3/tests/Activities/Design/Import/Integration/Tests`) and fix fallout in existing tests
- [x] T019 Bite-proof: revert T007–T009 locally, show the new tests red, restore, show them green (record for the PR)
- [x] T020 Re-run T001's measurement, record converts / publishes / runs and each definition's next blocker in
  spec.md, then delete the throwaway test (SC-001, SC-002, SC-005)
- [x] T021 Update the per-definition table and "Where it stands" in epic #3052
- [x] T022 Check the diff outside `Extensions/Elsa3` and `specs/` is empty (SC-007); set spec Status to Implemented

## Dependencies

- T002–T006 block every story. T006 needs T003–T004.
- US1 (T007–T012) needs Phase 2; US2 and US3 extend US1's test class and need T007–T008.
- Phase 6 needs all stories; T019–T022 run last.

## Parallel opportunities

- T010 and T012 (different files) once T007–T009 are in.
- T015, T016, T017 in parallel.

## Implementation strategy

MVP is US1: it fixes #3200's publish error. US2 and US3 are mostly the table (T006) plus tests, so all three land in
this PR.
