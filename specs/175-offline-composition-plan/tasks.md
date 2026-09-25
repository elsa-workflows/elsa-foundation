# Tasks: Offline composition plan command

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [CLI contract](contracts/cli-v1.md).

## Phase 1: Setup

- [X] T001 Validate spec, plan, command contract, source links, and constitution gates in `specs/175-offline-composition-plan/` before code edits.

## Phase 2: Foundational shared result

- [X] T002 Add source-tagged `DependencyEvidence` model and sorted field on `SelectionPlan` in `src/essentials/Modularity/Planning/Models/SelectionPlan.cs`.
- [X] T003 Add focused tests for reviewed/runtime/manifest edge precedence, empty descriptor, optional advice, and stable order in `tests/essentials/Modularity/Planning/Tests/`.
- [X] T004 Populate dependency rows through `SelectionPlanner` and `HostAssessment` in `src/essentials/Modularity/Planning/Services/` without changing exact selection or existing findings.
- [X] T005 Add strict inventory and resource-hint v1 readers with safe-label validation in `src/essentials/Modularity/Planning/Json/`, and focused malformed/duplicate/null tests in `tests/essentials/Modularity/Planning/Tests/`.

## Phase 3: User Story 1 - Inspect exact selection (P1)

**Independent test**: File-only command with overlap/add/remove emits exactly `A,C,D`, accepted lock unchanged, and missing evidence findings.

- [X] T006 [US1] Add whole-process command tests for exact candidate, accepted lock, no side effects, output separation and exit codes in `tests/essentials/Cli/Tests/`.
- [X] T007 [US1] Add direct planning project reference to `src/essentials/Cli/Elsa.Cli.csproj` and register `composition plan` in `src/essentials/Cli/ElsaCli.cs`.
- [X] T008 [US1] Parse explicit catalog/composition/workspace files and run the shared planner in new `src/essentials/Cli/CompositionPlan*.cs` files; do not call `WorkerProcess`.
- [X] T009 [US1] Implement deterministic text and schema-v1 JSON safe projection in `src/essentials/Cli/CompositionPlan*.cs`, including source-coded reasons/findings and no raw rationale or opaque content.
- [X] T010 [US1] Fix generic parse-error help in `src/essentials/Cli/Program.cs` so composition users are directed to root help.

## Phase 4: User Story 2 - Inspect supplied evidence (P2)

**Independent test**: Dated supplied inventory with required/optional edges and resource `primary` shows source-tagged unresolved facts while persistence remains unchecked.

- [X] T011 [US2] Add command tests for inventory/source/time, descriptor-vs-manifest evidence, removed required edge, and unchecked resource hints in `tests/essentials/Cli/Tests/`.
- [X] T012 [US2] Connect optional inventory and resource-hint files to the strict readers and shared planner in `src/essentials/Cli/CompositionPlan*.cs`.
- [X] T013 [US2] Add sentinel-redaction and deterministic-output tests covering opaque objects, rationale, IDs, labels, parse errors, and paths in `tests/essentials/Cli/Tests/`.

## Phase 5: Polish and verification

- [X] T014 Run both whole affected test projects, canonical architecture restore/guard, maps refresh/check, solution-filter check, and diff review in the #2001 worktree.
- [X] T015 Mutate the new redaction or missing-edge behavior, show a focused test goes red, restore and show the complete affected suites green; record evidence for the PR.
- [X] T016 Mark `specs/175-offline-composition-plan/spec.md` Implemented only in the implementation PR, update `docs/program-goals/feature-composition-readiness.md`, and publish PR gate evidence before merge.

## Dependencies and delivery

T001 precedes code. T002–T004 establish shared dependency evidence; T005 establishes input readers. T006–T010 deliver a useful file-only command without optional evidence. T011–T013 add supplied evidence and security regression coverage. T014–T016 gate delivery. Model and test edits can be explored independently but are integrated sequentially before verification. The MVP is User Story 1; the issue closes only when both stories and the security boundary pass.
