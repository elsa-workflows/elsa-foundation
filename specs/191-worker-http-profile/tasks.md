# Tasks: Worker HTTP starting profile

Input: [spec](spec.md), [plan](plan.md), [contract](contracts/worker-profile.md). One delivery leaf #2326; no separate GitHub issue per task. Completed boxes require actual evidence.

## Phase 1 — Scope and preflight

- [X] T001 Refresh issue/PR/Project51/native dependency state; claim whole #2326; record current main/branch and ownership in `specs/191-worker-http-profile/research.md`.
- [X] T002 Author/review specification, assumptions, contract, plan and checklist using official SpecKit flow in `specs/191-worker-http-profile/`.
- [X] T003 Verify shared CLI/PTY helper dependencies, candidate JSON/settings preservation, actual child source ownership and existing ignore/build slots in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcHostFixture.cs`; record preflight in `specs/191-worker-http-profile/implementation-evidence.md`.

## Phase 2 — Published identity compatibility

- [X] T004 Preserve old resource bytes and add direct exact-v1/v2/fallback regression assertions in `tests/essentials/Modularity/Planning/Tests/FoundationSelectionCatalogTests.cs`.

## Phase 3 — US1 stable starting selection

**Independent test**: Exact19 members/reasons, old pins and unchanged definitions, no readiness inference.

- [X] T005 [US1] Publish reviewed `worker-http@1` in `src/essentials/Modularity/Planning/Catalogs/foundation-selection-catalog-v3.json` with computed canonical digests and unchanged existing definitions.
- [X] T006 [US1] Retain exact v1/v2/v3 pin loading and current fallback in `src/essentials/Modularity/Planning/Catalog/FoundationSelectionCatalog.cs` and register the new embedded resource in `src/essentials/Modularity/Planning/Elsa.Modularity.Planning.csproj`.
- [X] T007 [US1] Update current-catalog assumptions and test Worker selection/explanations/unverified findings in `tests/essentials/Modularity/Planning/Tests/FoundationSelectionCatalogTests.cs` and `tests/essentials/Cli/Tests/CompositionInitCliTests.cs`; retain Embedded assertions by ID.

## Phase 4 — US2 generated candidate to useful worker

**Independent test**: Actual built command/PTY artifacts agree with readback/activated features/hashes; real authenticated durable workflow and distinct-process restart.

- [X] T008 [US2] Link existing `DotnetElsa.cs`/`PseudoTerminalCli.cs` and production CLI build reference into `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj` without copying process code or adding test-project references.
- [X] T009 [US2] Build shared fixture candidate preparation through real init/plan/interactive accept/generate with source-local OIDC/IAM/locking/runtime objects and empty portable settings in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerProfileCandidate.cs`; assert exact pins/selections/source preservation/readback/hashes and unverified findings.
- [X] T010 [US2] Replace static feature/settings configuration with candidate base/selected-overlay/appsettings byte consumption in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Fixtures/WorkerOidcHost/Program.cs`; extend private startup/receipt and independently hash the exact bytes loaded, preserving root tenant/instrumentation.
- [X] T011 [US2] Extend async fixture startup/restart inputs and retain deterministic cleanup in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcHostFixture.cs`; no second feature list or unbounded output.
- [X] T012 [US2] Run primary exact19 candidate through all existing token/policy/tenant/store/execute/event/resume/revocation/distinct-process actor objectives in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcHostTests.cs`, asserting accepted/readback/actual IDs and consumed hashes before/after restart.

## Phase 5 — US3 granular edits bind in the worker

**Independent test**: Accepted18 actual activation and paired old/new audience HTTP outcomes with legitimate real-store capabilities permission, no workflow effects.

- [X] T013 [US3] Derive a same-pin explicit ControlFlow removal and alternate source Audience, accept/generate fresh secondary candidate, then assert exact18 actual selection/hashes in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcHostTests.cs`.
- [X] T014 [US3] Use actual IAM capabilities-read grant and signed old/new tokens against production `/capabilities`, asserting401/zero reads versus200/one read and zero runtime rows in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcHostTests.cs`.
- [X] T015 [US3] Execute static-selection and stale-audience bypass mutations, restore byte-identical behavior and rerun the decisive actor in `tests/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/WorkerOidcHostTests.cs`; record fail/restored/source evidence in `specs/191-worker-http-profile/implementation-evidence.md`.

## Phase 6 — Integration and publication

- [X] T016 Document exact selection, host-owned prerequisites, real CLI flow and bounded actor evidence in `docs/reference/worker-http-profile.md`; link canonical contracts rather than duplicate architecture meanings.
- [X] T017 Synchronize completed Spec190 T028 from its final public closure in `specs/190-worker-oidc-normalization/tasks.md`, `specs/190-worker-oidc-normalization/implementation-evidence.md` and the active Worker successor in `docs/program-goals/feature-composition-readiness.md`.
- [X] T018 Run final restored full Planning/CLI/Runtime EF suites serially plus affected retained checks, architecture and filter freshness; deliberately refresh/check maps/review findings/stage changed outputs explicitly; record commands/source/platform/counts/skips in `specs/191-worker-http-profile/implementation-evidence.md`.
- [X] T019 Root review the full integrated delta and proof; open one gated PR with #2326; obtain actual exact-head review, resolve/reply findings and applicable hosted checks; record evidence in `specs/191-worker-http-profile/implementation-evidence.md` and public issue/PR comments.
- [X] T020 Verify required resulting-main workflows/source-package/image identity, synchronize issue/Project/parent and release claim; mark lifecycle only from completed proof in `specs/191-worker-http-profile/implementation-evidence.md` and `docs/program-goals/feature-composition-readiness.md`.

## Dependencies and bounded parallel work

T001-T003 precede implementation. T004-T007 are catalog-owned. T008-T011 can proceed on a separate isolated actor worktree after root-reviewed contracts; integrated T012/T013 require catalogv3. T014 follows secondary activation; T015 follows passing controls. Root serializes heavy builds and all integration/mutations/gates. T017 can be synchronized while workers implement, because it records already verified delivery. No two concurrent writers share a checkout.

Parallel example: catalog worker edits Planning/catalog/CLI assumptions; actor worker edits only Runtime EF child/fixture/tests/project reference. Both branches begin from the same reviewed spec commit. Root reviews deltas and integrates in one lane before actor certification.

## Delivery Strategy

Deliver all three P1 journeys as one complete Worker profile task. Catalog-only US1 is useful but does not satisfy publication acceptance without actual generated-candidate activation and useful authenticated work. Keep later Authoring/UX/apply outcomes separate and unresolved. Exact-head local/hosted evidence is not inferred across revisions, and the goal remains active after this leaf ships.
