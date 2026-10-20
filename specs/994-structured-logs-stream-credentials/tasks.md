# Tasks: Authenticated Structured Logs Streaming

**Input**: Approved design documents in `specs/994-structured-logs-stream-credentials/`.

**Execution gate**: Root must review and commit this task list before any pending task starts. T001 is the only task already completed. No issue closure or readiness promotion is implied by this plan.

**Test cadence**: The app has no repository-wide TDD mandate. The approved plan explicitly requires one test-only RED regression and root review before the product edit. Remaining tests follow the story order below; native EventSource retry and credential behavior require the real-host proof, not a stub-only claim.

## Phase 1: Setup

**Purpose**: Establish the existing client baseline and preserve its receipt.

- [x] T001 Confirm root's offline Studio workspace install and untouched Structured Logs client suite baseline (10/10 tests) using receipts at `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3132-baseline-install.log` and `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3132-baseline-module.log`.

---

## Phase 2: Foundational RED Checkpoint

**Purpose**: Prove the client regression before any product change; this checkpoint blocks implementation until root reviews the failing test.

- [ ] T002 Add a rendered-page test in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts` that records EventSource URL and init options, covers same-origin and separate-origin backend URLs with existing filter query semantics, and fails against the current one-argument constructor because `withCredentials` is not true; run that focused test-only RED and stop for root review before editing `src/module.tsx`.

**Checkpoint**: Root reviews the failing test and confirms the red result before T003.

---

## Phase 3: User Story 1 — Open authorized live logs (Priority: P1) — MVP

**Goal**: The existing normal signed-in operator opens the actual product stream across the configured separate origins and reaches Live, including with an empty inventory.

**Independent test criteria**: The rendered constructor regression passes with the current browser-managed session option and unchanged URL/filter semantics; the final normal-host product proof returns 200 `text/event-stream`, reaches Live, and keeps Recent/Sources at 200.

- [ ] T003 Change only the existing EventSource construction in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module.tsx` to pass `{ withCredentials: true }`; keep the URL, endpoint, permissions, CORS configuration, auth model, and effect lifecycle unchanged.
- [ ] T004 Run the Structured Logs client Vitest suite from `Studio/` with `pnpm --filter @elsa-workflows/studio-diagnostics-structured-logs test`; confirm the test from T002 is green and the existing URL/filter tests remain green in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts`.

**Checkpoint**: The focused client regression is green; no backend or public contract change is introduced.

---

## Phase 4: User Story 2 — Keep the existing log-reading workflow (Priority: P2)

**Goal**: Filter changes, pause/buffer, error display, reconnect, and cleanup retain their established behavior while using the same authorized browser session.

**Independent test criteria**: Rendered-page behavior tests prove filter replacement closes the prior source, the replacement carries current filters and credential mode, pause/buffer and entry filtering remain correct, and unmount closes the active source. Existing backend authorization and stream tests pass; the real-host retry proof is completed in final qualification.

- [ ] T005 Extend the existing EventSource stub and rendered tests in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts` to verify filter changes close the previous source and create one replacement with the selected level/source/category query and credential option, and that unmount closes the active source.
- [ ] T006 Add deterministic page-event regressions in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts` for entry filtering and pause/resume buffer behavior; verify `onerror` preserves the disconnected UI state without client-created duplicate EventSource instances or changed cleanup behavior.
- [ ] T007 Run the existing authorization matrix in `Essentials/tests/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj` (`StructuredLogsApiAuthorizationTests`); retain anonymous/insufficient denial and zero-SSE/no-subscription outcomes.
- [ ] T008 Run the existing stream replay/filter/cleanup coverage in `Essentials/tests/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj` (`StreamEndpointTests`); preserve backend behavior without changing its source.

---

## Phase 5: Polish and Combined Qualification

**Purpose**: Qualify the finished work against the integrated PR base and the normal product host.

- [ ] T009 After PR #3133 is integrated, update the feature branch onto that base and review `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module.tsx` to confirm its separate index/copy change is preserved without duplicating or overwriting the stream constructor fix; confirm the Spec 993 host-guide link and anchor referenced by `specs/994-structured-logs-stream-credentials/quickstart.md` resolve at the integrated base.
- [ ] T010 Following the now-resolved Spec 993 composed-host procedure linked from `specs/994-structured-logs-stream-credentials/quickstart.md`, use a fresh disposable normal-host configuration and standard operator cookie session on separate Studio/Workbench origins; verify Recent and Sources return 200, the actual product stream returns 200 `text/event-stream`, and the page reaches Live even with an empty inventory. Do not add a telemetry-ingestion fixture or provider for this proof.
- [ ] T011 In the same isolated normal-host browser context, trigger one bounded transient interruption and verify the actual EventSource retry authenticates and returns to Live without duplicate active subscriptions; inspect only safe request summaries and confirm no credential is in the URL or saved evidence, following the resolved `specs/993-studio-diagnostics-index/quickstart.md#composed-workbench-comparison`.
- [ ] T012 On the final combined candidate, run the client package test/build and Studio workspace typecheck, lint, and production build from `Studio/`; build both hosts; run the shared guide's CI dependency-graph restore before the Architecture suite, followed by `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`. Complete independent review and exact-head CI, merge only on green gates, then verify resulting-main Core/Studio/Maps and record the issue/claim-release receipt. In the implementation PR, set `specs/994-structured-logs-stream-credentials/spec.md` to Implemented and delete its `checklists/requirements.md` per `docs/reference/spec-lifecycle.md`; keep this task open until delivery and main qualification complete.

---

## Dependencies and Execution Order

- T001 is complete and supplies the unchanged-client baseline.
- T002 must produce a failing test and receive root review before T003.
- T003 precedes T004; T004 precedes the User Story 2 test additions and preservation checks (T005–T008).
- T009 depends on PR #3133 integration and on T003–T008; it must happen before the combined host qualification.
- T010 precedes T011 because retry is verified in the same isolated browser context; T012 is the final qualification gate.
- User Story 2 follows User Story 1: both touch the same client test file and preserve the newly credentialed source lifecycle.

**Parallel opportunities**: None planned. The client tasks share `module.test.ts` and the approved order contains a mandatory RED/root-review checkpoint. Keep the final host and repository gates serial.

## Implementation Strategy

**MVP**: User Story 1 is the P1 product outcome: one existing constructor option, a rendered regression, and product-host proof that the authorized separate-origin page reaches Live. Story 2 and all preservation and final gates remain required before accepting the issue.

**Incremental delivery**: Complete the reviewed RED checkpoint, apply the one-call-site correction and focused green test, add client and backend preservation evidence, integrate PR #3133 base-first, then complete actual-host behavior and exact-head/resulting-main qualification. Do not add an ingestion fixture, provider, transport, endpoint, auth architecture, or bearer-token behavior.
