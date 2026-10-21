# Tasks: Studio OpenTelemetry Trace Inspection

**Input**: Approved design in `specs/995-studio-otel-contracts/` (`spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`).

**Tests**: Required by the approved work-unit direction and the risk in the existing client/server contract mismatch. Add the complete shared backend-shaped regression set first, confirm meaningful test-only RED with `module.tsx` unchanged, then stop for root review before any product edit. After approval, keep the per-story groups green and run the existing TestServer authorization suites separately from the rendered 403 case.

**Execution boundary**: Root owns integration decisions, heavy shared-machine checks, normal-host trace proof, GitHub transitions, exact-head review, and main qualification. This is one serial lane; there is no #3132 dependency.

## Phase 1: Setup

**Purpose**: Establish an offline, measured untouched baseline before editing fixtures or product code.

- [x] T001 From `Studio/`, run `pnpm install --offline --frozen-lockfile` and then the untouched `pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry test` for `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/package.json`; record the observed test count and pass/fail result from this exact checkout without assuming a historical count, and stop/report if the offline install cannot complete without lockfile or dependency changes.

## Phase 2: Foundational — Shared RED Gate

**Purpose**: Create realistic behavior tests for all stories before changing production code.

- [x] T002 In `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts`, add shared backend-shaped fixtures and rendered request/response regressions grouped by `US1`, `US2`, and `US3`; cover `{items,droppedCount}` search and nested `{trace,spans,resources,logs}` detail, trace statuses `Unset`/`Ok`/`Error`, resource statuses `Unknown`/`Active`/`Stale`, TimeSpan trace duration and span start/end duration, multi-resource/per-span joins and unknown fallback, supported `traceId`/`serviceName`/`status`/`search`/`take` request fields, truthful storage/resource data without trace totals, independent failures, loading/empty/denied/unavailable/failure and selected-detail 404, no per-row detail/errorCount/zero-health inference, and sentinel-log exclusion from UI/export while preserving trace/span export; share fixture cleanup and leave `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx` unchanged.
- [x] T003 From `Studio/`, run `pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry test` against test-only changes in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts`; confirm the new assertions fail against unchanged `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx`, record only failing test names/counts and safe shape-level details, then stop for root review before any production edit.

**Blocking checkpoint**: Do not begin T004 or later product tasks until root has reviewed and approved the test-only RED result.

## Phase 3: User Story 1 — Find and inspect a workflow trace (Priority: P1)

**Goal**: Read real trace search/detail contracts and let a signed-in operator find and inspect traces without inventing counts, fields, services, or signal data.

**Independent test**: The `US1` group in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts` passes for supported filter request bodies and result bounds, actual nested trace/span/resource data, multiple services and unknown resources, `Unset`/`Ok`/`Error`, trace/span elapsed formatting, and trace/span export with no nested logs.

- [x] T004 [US1] Update trace search and selected-detail projections in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx` to use the existing authenticated client and real routes/envelopes; bind `traceId`, `serviceName`, `status`, `take`, and the **Trace name or ID** `search` field; include the bounded resource-search read needed to resolve trace-row services, preserve nested span relationships, resolve each span through its own resource, show all known services and explicit unknowns, normalize host trace/span durations, render host status without `errorCount`, avoid per-row detail reads, and exclude nested logs from the existing trace/span export. Preserve the selected ID/detail when a refreshed result page still contains it; clear it only when the selected ID is no longer present. Trim resource service names once so whitespace-only names use the explicit unknown fallback.
- [x] T005 [US1] From `Studio/`, run `pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry exec vitest run src/__tests__/module.test.ts -t US1` after T004; verify the `US1` group in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts` passes for filter/body, nested detail, resource, status, duration, no-error-count, no-N+1, retained-ID detail/export, and export preservation. The retained-ID focused run passed 2/2 (24 skipped, exit 0) in `3131-retained-trace-green.log`; final US1 focused result passed 5/5 (21 skipped, exit 0) in `3131-us1-focused.log`. Full module suite passed 26/26 (exit 0) in `3131-module-full-r2.log` before the final overview-only removal of raw resource attributes.

## Phase 4: User Story 2 — Understand the stored diagnostic data (Priority: P2)

**Goal**: Show only truthful stored resource/span counts and supplied resource metadata, with no provider-dependent trace total or false health signal.

**Independent test**: The `US2` group in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts` passes for storage and resource endpoint shapes, supplied metadata, bounded results, independent success/failure, and omission of an unqualified trace-total card.

- [x] T006 [US2] Update the existing overview reads and view in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx` to add the storage read independently and reuse the bounded resource-search mapping from T004; label returned resource/span values as stored data, show only supplied service metadata/resource status, retain known identities with unknown fallback, omit an unqualified trace total, and never turn a failed read into zero or healthy status.
- [x] T007 [US2] From `Studio/`, run `pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry exec vitest run src/__tests__/module.test.ts -t US2` after T006; verify the `US2` group in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts` passes for independent reads and truthful stored-data labels with no inferred total or health measure. Focused result after validating absent/malformed counts: 9 passed, 15 skipped, exit 0; full output recorded in the local execution log `3131-us2-focused.log`.

## Phase 5: User Story 3 — Distinguish empty, denied, and unavailable states (Priority: P2)

**Goal**: Keep each read's loading, successful-empty, denied, unavailable, and failure state distinct, and preserve unsupported-signal messaging and the Diagnostics handoff.

**Independent test**: The `US3`-grouped tests in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts` pass for loading, successful empty, rendered 401/403 denial, list/storage/resource 404 unavailable, selected-detail 404 missing trace, other failure, and independent partial-overview outcomes; Metrics/Logs remain unsupported and Diagnostics handoff works.

- [x] T008 [US3] Update per-read state handling and visible messages in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx` so successful empty, 401/403 denial, diagnostics-surface 404, other failure, and selected-trace detail 404 remain distinct; keep storage/resource outcomes independent, leave Metrics and OpenTelemetry Logs unsupported, and preserve the existing Diagnostics handoff. Selected-detail 404 is reported as a missing trace; detail 401/403 and 500 stay in the detail pane without overwriting successful trace-search state.
- [x] T009 [US3] From `Studio/`, run `pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry exec vitest run src/__tests__/module.test.ts -t US3` after T008; verify the `US3` group in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts`, including rendered 403 and selected-detail-404 cases. Focused result on the final story head: 9 passed, 15 skipped, exit 0; full output recorded in the local execution log `3131-us3-focused.log`.
- [x] T010 [US3] Root runs `dotnet test Essentials/tests/Diagnostics/OpenTelemetry/Tests/Elsa.Diagnostics.OpenTelemetry.Tests.csproj --filter 'FullyQualifiedName~OpenTelemetryAuthorizationTests|FullyQualifiedName~OpenTelemetryAuthorizationBoundaryTests'` from the repository root as a separate TestServer permission-boundary proof; preserve malformed-request and successful-read cases, and do not substitute anonymous baseline composition for denial proof or add an optional Identity/admin setup.

## Phase 6: Polish and Root-Owned Qualification

**Purpose**: Complete focused checks, prove the normal engine-bridge path, and finish the repository's integration and lifecycle gates.

- [x] T011 From `Studio/`, run the full OpenTelemetry module Vitest suite, workspace typecheck with `pnpm --recursive --workspace-concurrency=1 typecheck`, workspace lint with `pnpm lint`, module build with `pnpm --filter @elsa-workflows/studio-diagnostics-open-telemetry build`, and recursive workspace build with `pnpm --recursive --workspace-concurrency=1 build`, serially and in that order.
- [x] T012 Build both scoped hosts serially with the normal repository dotnet build-slot wrapper: `Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj` followed by `Studio/src/apps/Elsa.Studio.Web/Elsa.Studio.Web.csproj`; use the normal host-build instructions in `specs/995-studio-otel-contracts/quickstart.md` and do not launch concurrent heavy checks.
- [x] T013 Root proves the authenticated happy path against the normal supplied Workbench and Studio compositions using the private-host procedure in `specs/995-studio-otel-contracts/quickstart.md` and the canonical guide `specs/993-studio-diagnostics-index/quickstart.md`: retain the guide's `elsa-3073.*` root prefix and manifest guards with separate #3131 provenance, fresh per-run databases/keyring and a fresh browser context, sign in through the standard Development UI with its disposable seeded principal, author/publish/execute a harmless `WriteLine` via `e2e-tests/Test-WorkflowFlow.ps1` and `e2e-tests/_ElsaCommon.ps1`, then find the actual generated trace in Studio and inspect nested detail plus engine execution tags; capture only the approved safe receipt fields and deterministically clean the owned run root.
- [x] T014 Root runs the current architecture gates serially using the normal dotnet wrapper: `bash tools/architecture/restore-ci-project-graph.sh --locked-mode -p:WarningsNotAsErrors=NU1603`, `dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj -c Release --no-restore -p:WarningsNotAsErrors=NU1603`, and `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`; report the actual outcomes and preserve any setup failure as a failure.
- [x] T015 In the same implementation PR, after the required evidence is accepted, change `specs/995-studio-otel-contracts/spec.md` to `**Status**: Implemented` and remove `specs/995-studio-otel-contracts/checklists/requirements.md` (and its now-empty checklist directory) before merge, following `docs/reference/spec-lifecycle.md`; do not create a separate status PR.
- [ ] T016 Root completes independent nonauthor correctness, standards, and acceptance review of the final head and green exact-head CI, refreshes all current policy/check/comment/hold gates, then merges normally under the current repository protocol. After merge, verify the actual resulting-main Core/Studio/maps gate and record its source and outcome. Keep this task open until that qualification is complete; do not report cancelled or skipped work as passing evidence.
- [ ] T017 At each required state transition, root updates issue #3131 and synchronized board/labels, then releases its specific winning claim `https://github.com/elsa-workflows/elsa-foundation/issues/3131#issuecomment-6303781470` with the Elsa Workflows Codex identity when the work ships or is abandoned; keep the accepted verification record in `specs/995-studio-otel-contracts/quickstart.md`.

## Dependencies & Execution Order

### Phase dependencies

- Phase 1 baseline (T001) precedes every change.
- Phase 2 test-only fixtures/regressions and RED review gate (T002–T003) block all product edits; root approval is required before T004.
- User story phases then run in priority order: US1 (T004–T005), US2 (T006–T007), US3 (T008–T010). The stories are independently specified, but the existing page and test suite are shared files, so this work executes serially.
- Final qualification (T011–T014) depends on all three story phases. Lifecycle and GitHub closeout (T015–T016) depend on accepted verification and the root-owned PR path.

### Verification coverage

| Spec coverage | Tasks and proof |
|---|---|
| FR-001–FR-007, SC-001–SC-003: search, nested detail, filters/take, exact enums/durations, multiple resources and unknown fallback | T002–T005, then the real normal-host trace in T013 |
| FR-008–FR-009: truthful stored counts and supplied resource metadata; no trace total/health inference | T002, T006–T007 |
| FR-010–FR-011, SC-004–SC-005: loading/empty/denied/unavailable/failure, selected-trace 404, independent partial reads, no fabricated zeros/error count | T002–T003, T006–T010 |
| FR-012, SC-006: unsupported Metrics/Logs, Diagnostics handoff, no nested log in UI/export while preserving trace/span export | T002–T005, T008–T009 |
| FR-013: unchanged sign-in and read permission; denial boundary | T009–T010 and the authenticated normal-host proof T013 |
| Repository integration and delivery | T011–T016 |

### User story dependencies

- **US1 (P1)**: Starts after the foundational RED checkpoint is reviewed; no semantic dependency on US2, US3, or #3132.
- **US2 (P2)**: Starts after the shared RED checkpoint; reuses the existing module but does not depend on trace details being fetched per row.
- **US3 (P2)**: Starts after the shared RED checkpoint; uses the same reads and existing auth boundary, with a separate TestServer proof.

## Parallel Opportunities

No task is marked `[P]`: the product and regression work share `module.tsx` and `module.test.ts`, and the heavy suite/host checks must be serial on the shared machine. For each user story, parallel execution is therefore **none**; keep its implementation and focused verification together before moving to the next story. Root owns one serial integration lane and final base reconciliation; no task depends on #3132.

## Implementation Strategy

### Suggested MVP

US1 is the suggested MVP slice after the complete shared test-only RED gate is root-approved. It proves the primary value: the actual supported search/detail mapping and real trace inspection. The issue is not complete until US2 and US3 plus their acceptance evidence are also finished.

### Incremental delivery

1. Measure the untouched offline module baseline.
2. Add the shared backend-shaped regressions for all three stories and stop at the test-only RED review gate.
3. After root approval, implement and verify US1, then US2, then US3 without overlapping edits to the shared module.
4. Run the full focused/workspace checks, separate TestServer permission proof, and normal-host real-trace proof.
5. Root completes architecture gates, exact-head review/CI and resulting-main qualification; update the spec lifecycle and release the issue claim only through the accepted PR closeout.

## Root Task Review

Root read the complete task list against the approved specification and plan. T004 explicitly owns the resource lookup needed by its trace-row acceptance, and T006 reuses it for the overview. Backend authorization and all heavy verification stay in root's serial lane. The final review/main qualification order and specific claim identity are explicit. Approved to execute T001–T003 only, with a mandatory stop after test-only RED for root review before any product edit. No task is completed by this approval.


## Root test-only RED approval

Root read the complete revised test file, both baseline logs and full `3131-contract-red-r2.log`. The untouched module baseline passes 11/11. The revised backend-shaped suite executes 21 cases: 8 pass and 13 fail at contract-level assertions; there are no collection, harness or teardown failures. Product `module.tsx` remains byte-identical to HEAD at this checkpoint. Failed search-route/body, nested detail/export, stored resource/span and independent overview state assertions establish meaningful behavior RED. The selected-detail-404 case cannot reach its detail request until search is corrected; this result does not independently prove a defective 404 handler.

Shared fixtures now scope count cards and per-span service/duration assertions, preserve the unsupported-signal handoff, and restore every mounted root/global descriptor while surfacing cleanup errors. Root accepts T001–T003 and authorizes T004–T009 only, sequentially by story, with focused GREEN and a stop for integration review. Preserve all assertions; any necessary harness-only adaptation must be explained. Root retains backend authorization, normal-host proof, heavy gates and delivery. Final GREEN must also exercise exact resource statuses and meaningful export content, not merely fixture presence.


## Root implementation review and focused qualification

Root read the full product module, complete test changes, current backend endpoint contract and all focused/full logs. The initial story checkpoint passes24/24, but root found that clearing detail on every filter request leaves the pane permanently empty when the selected trace ID stays the same. T004/T005 were reopened. Two new limit/name-search cases established valid RED (both fail after request/selection assertions pass,24 cases selected out); the correction preserves selection/detail when the result still contains that ID and lets the existing ID effect clear it on a real selection change. Both cases pass, with shared clipboard/download capture and failure-safe teardown.

Root also strengthened existing metadata assertions: a blank service must not hide a supplied resource ID (one focused RED), and the approved all-unknown service fallback includes its supplied resource-ID count (one focused RED). Both corrections are present. Resource service names are trimmed once, resource status stays factual, raw attributes remain in the existing selected-span inspector/export rather than default overview, and no nested log is exposed. Missing or malformed stored counters remain unavailable; numeric zero is preserved.

The selected-detail404 negative control now reaches its assertion: disabling only that handler causes the missing-trace case to fail (1 failure,25 selected out). Root restored the exact product bytes/SHA and all26 cases passed. After the final identity/count and scoped stylesheet refinements, `3131-module-root-final-r4.log` passes26/26 with no skips. Earlier logs and failures remain historical, including the original RED's unreachable detail404 path. This is module-level behavior proof, not backend authorization or an actual product trace receipt.

The existing stylesheet now matches four page sections and two stored-count cards, separates name/ID lines, lets wrapping filters size the list correctly, and permits bounded detail/source scrolling and long-ID wrapping. Root reviewed the stylesheet diff; actual normal-host light/dark screenshots remain required under T013. T004–T009 are accepted locally. T010–T017 remain open; backend authorization, remaining workspace/host/gates, real engine-bridge trace/UI, lifecycle and final delivery are not claimed.

## Root normal-host qualification checkpoint

Backend authorization passes21/21 with0skips on2381e6459. Its Workbench build first failedMSB4166 under load267.83 on8CPUs; the absent crash diagnostic does not establish a cause. A normal queued single-node retry passes101warnings/0errors, then Studio passes0/0. The initial failure remains retained. Types, lint0errors46existingwarnings, module and full workspace builds passed at2381; final integrated T011 remains open for its current-candidate checkpoint.

The fresh private normal composition retained the existing engine bridge. A source-backed harmless WriteLine completed, yielding a correlated trace with14engine spans,14resource joins and12parent-child links. A fresh standard Development UI session reached More tools → Diagnostics → OpenTelemetry, filtered the generated ID, received actual search/detail200 and inspected the activity span, matching execution tag, elapsed duration, Unset status and parent. The overview shows one stored resource and15stored spans without a trace total; Metrics/Logs remain unsupported. No provider or manual trace was introduced.

Root laptop proof exposed an execution-tag label/value overlap and Status outside the trace list. Scoped CSS now wraps metadata labels/values and fits all six columns; the compact Trace heading preserves the full filter label and name/ID detail. Actual1440light and1280dark screenshots and geometry show no page/table overflow, visible Status and no tag overlap. The final built module CSS/JS served by the owned host match retained SHA256 hashes; this is2381 host binaries with the scoped rebuilt frontend changes, not a claim that the initial binaries contained later asset edits. The final affected module suite passes26/26, module build and CSS lint pass.

Incorrect heading/row/tab selectors were corrected, not passes. An early filter-step tool timeout exited its first owned browser; exact-profile cleanup and a new fresh standard session supplied accepted proof. The registry-count inference was corrected by actual More tools navigation. Both final host sessions and browser exited; ports are free and the manifest-guarded private runtime/configuration/logs/databases/keyring were removed. T013 is accepted for this scope; final main integration, current-candidate T011/T014, lifecycle/review/CI and resulting-main gates remain open.


## Final integrated local qualification

Root integrated main `dd2fdff97e82efe3e4cdfdc060e23a4d36d318a2` into candidate `943ca444c93ad3ecf95317e1dc8a49ac3718068a`. All eleven serial local gates passed: module26/26, workspace types, lint0errors46existingwarnings, module/workspace production builds, both single-node host builds, authorization21/21, official architecture restore, architecture579/579 with0skips and generated-map freshness. The earlier MSB4166 remains an unproven build-process failure; later successes do not establish its cause.

A fresh normal private Workbench and Studio run used this full candidate's rebuilt binaries and client. Standard UI sign-in and More tools → Diagnostics → OpenTelemetry led to the actual source-backed WriteLine execution's trace: Completed, search/detail200,14engine spans,14resource joins,12parent-child links and the matching execution tag in the selected activity span. Stored-data cards showed1resource/15spans with no trace-total card. Actual filtered-empty200 and later matching search/detail200 were observed. Settled1440light/1280dark screenshots show readable metadata, all list columns, no measured page/list overflow and no tag-label overlap. This supersedes the earlier mixed-build provenance limitation only for this repeat.

The first readiness probe returned503 during startup, but the separately invoked guarded helper completed the workflow; later readiness200 and exact owned checkout/content-root commands were verified. A duplicate proof attempt was refused before mutation by CreateNew. Strict link/empty-copy selectors timed out; corrected observed selectors supplied accepted proof. The early dark screenshot captured a theme transition and is not the accepted settled capture. Browser page/console errors were0. The browser and both owned hosts exited; no listeners remained, reusable port binds succeeded, the exact browser profile disappeared and the manifest-guarded private root was deleted. A first non-reusable bind failure after exit is retained.

Safe external receipts: `3131-final-gates.json`, `3131-final-workflow-trace-proof.json`, `3131-final-host-readiness.json`, `3131-final-head-ui-proof.json` and `3131-final-head-cleanup.json`. Backend permission tests remain distinct from a denied normal-browser journey. No external ingestion, performance benchmark, provider activation or deployment is claimed. T011–T015 are complete locally; final exact-head independent reviews/hosted CI, merge and resulting-main gates under T016–T017 remain open.
