# Tasks: native compensation scopes (M1)

## Runtime (PR 2)

- [ ] T001 Create `Elsa.Activities.Compensation` project, add it to `Elsa.Server.slnx` and the solution filters.
- [ ] T002 Authored and executable structure models, plus `CompensationScopeStructureHandler` (project, replace,
  compile, remap) with round-trip tests.
- [ ] T003 Draft validator: blank or duplicate step names, missing forward activity (error); `ReplaySafe` step or
  handler (warning). Tests for each message and path.
- [ ] T004 `CompensationScopeState` v1 and its persister (`InstanceInline`).
- [ ] T005 `CompensationScope` activity: execute, child completed, child faulted per
  [contracts/semantics.md](contracts/semantics.md). Unit tests for each transition and FR-009.
- [ ] T006 `ActivitiesCompensationFeature` registration and registration test.
- [ ] T007 Reservation participant sample with failure injection.
- [ ] T008 Integration tests: happy path `Done`; recovery `Compensated`; `faultAfterCompensation`; P1–P4 in
  three modes; pinned handler across republish; negative controls.

## Inspection and Studio (PR 3)

- [ ] T009 Inspection view model derived in Studio from run executions and incidents (no endpoint, FR-019), with
  tests for every recovery state.
- [ ] T010 Studio `compensation` designer mode and `CompensationScopeDesigner` (rows, add/rename/move/delete,
  add/replace/remove compensation, drill-in), with keyboard and accessible names.
- [ ] T011 Captured values: paired step outputs listed first in the handler reference picker.
- [ ] T012 Validation path mapping for scope rows ("Go to").
- [ ] T013 Run view: read-only paired list with states, Recovery panel, handler "Compensates" link, timeline tag,
  scope badge.
- [ ] T014 Vitest coverage for T010–T013, and a mocked Playwright fixture with axe.

## End-to-end (PR 4)

- [ ] T015 `e2e-tests/compensation/Test-CompensationRestart.ps1` (owns its server; P2 crash).
- [ ] T016 Opt-in live browser journey against the real backend, with restart.
- [ ] T017 Reference docs, glossary, `EXTENSION_POINTS.md`, maps; spec status `Implemented`; program checkpoint.
