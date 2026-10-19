# Tasks: native compensation scopes (M1)

## Runtime (PR 2)

- [x] T001 Create `Elsa.Activities.Compensation` project, add it to `Elsa.Server.slnx` and the solution filters.
- [x] T002 Authored and executable structure models, plus `CompensationScopeStructureHandler` (project, replace,
  compile, remap) with round-trip tests.
- [x] T003 Draft validator: blank or duplicate step names, missing forward activity (error). Tests for each message and
  path. The `ReplaySafe` warning (FR-014) is deferred to M5: design time has no side-effect profile to read.
- [x] T004 `CompensationScopeState` v1 and its persister (`InstanceInline`).
- [x] T005 `CompensationScope` activity: execute, child completed, child faulted per
  [contracts/semantics.md](contracts/semantics.md). Unit tests for each transition and FR-009.
- [x] T006 `ActivitiesCompensationFeature` registration and registration test.
- [x] T007 Reservation participant with failure injection (test `Support/Participant`; the Workbench fixture package
  `Essentials/tests/Workbench/Fixtures/Reservations`).
- [x] T008 Integration tests: happy path `Done`; recovery `Compensated`; `faultAfterCompensation`; P1–P4 in
  three modes; pinned handler across republish; repeat deliveries; negative controls.

## Inspection and Studio (PR 3)

- [x] T009 Inspection view model derived in Studio from run executions and incidents (no endpoint, FR-019), with
  tests for every recovery state.
- [x] T010 Studio `compensation` designer mode and `CompensationScopeDesigner` (rows, add/rename/move/delete,
  add/replace/remove compensation, drill-in), with keyboard and accessible names.
- [x] T011 Captured values: `IActivityStructureHandler.ChildrenCompletedBefore` ranks the paired step's results first
  in expression authoring; the Workbench test binds a handler to its step's result and to a captured variable.
- [x] T012 Validation path mapping for scope rows ("Go to").
- [x] T013 Run view: read-only paired list with states, Recovery panel, handler "Compensates" link, timeline tag,
  scope badge.
- [x] T014 Vitest coverage for T010–T013, and a mocked Playwright fixture with axe.

## End-to-end (PR 4)

- [x] T015 Owned-process restart test `Essentials/tests/Workbench/Tests/WorkbenchCompensationRecoveryTests.cs` (owns
  its Workbench; P2 crash). A C# Workbench test rather than a PowerShell script, so it runs in CI.
- [x] T016 Opt-in live browser journey `Studio/tests/live/compensation-journey.spec.ts` (`pnpm test:live` with
  `ELSA_STUDIO_URL`): authors, publishes, runs and inspects. Restart is proven by T015, not in the browser.
- [x] T017 Reference docs, glossary, `EXTENSION_POINTS.md`, maps; spec status `Implemented`; program checkpoint.
