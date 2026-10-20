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

- [x] T002 Add a rendered-page test in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts` that records EventSource URL and init options, covers same-origin and separate-origin backend URLs with existing filter query semantics, and fails against the current one-argument constructor because `withCredentials` is not true; run that focused test-only RED and stop for root review before editing `src/module.tsx`.

**Checkpoint**: Root reviews the failing test and confirms the red result before T003.

---

## Phase 3: User Story 1 — Open authorized live logs (Priority: P1) — MVP

**Goal**: The existing normal signed-in operator opens the actual product stream across the configured separate origins and reaches Live, including with an empty inventory.

**Independent test criteria**: The rendered constructor regression passes with the current browser-managed session option and unchanged URL/filter semantics; the final normal-host product proof returns 200 `text/event-stream`, reaches Live, and keeps Recent/Sources at 200.

- [x] T003 Change only the existing EventSource construction in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module.tsx` to pass `{ withCredentials: true }`; keep the URL, endpoint, permissions, CORS configuration, auth model, and effect lifecycle unchanged.
- [x] T004 Run the Structured Logs client Vitest suite from `Studio/` with `pnpm --filter @elsa-workflows/studio-diagnostics-structured-logs test`; confirm the test from T002 is green and the existing URL/filter tests remain green in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts`.

**Checkpoint**: The focused client regression is green; no backend or public contract change is introduced.

---

## Phase 4: User Story 2 — Keep the existing log-reading workflow (Priority: P2)

**Goal**: Filter changes, pause/buffer, error display, reconnect, and cleanup retain their established behavior while using the same authorized browser session.

**Independent test criteria**: Rendered-page behavior tests prove filter replacement closes the prior source, the replacement carries current filters and credential mode, pause/buffer and entry filtering remain correct, and unmount closes the active source. Existing backend authorization and stream tests pass; the real-host retry proof is completed in final qualification.

- [x] T005 Extend the existing EventSource stub and rendered tests in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts` to verify filter changes close the previous source and create one replacement with the selected level/source/category query and credential option, and that unmount closes the active source.
- [x] T006 Add deterministic page-event regressions in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts` for entry filtering and pause/resume buffer behavior; verify `onerror` preserves the disconnected UI state without client-created duplicate EventSource instances or changed cleanup behavior.
- [x] T007 Run the existing authorization matrix in `Essentials/tests/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj` (`StructuredLogsApiAuthorizationTests`); retain anonymous/insufficient denial and zero-SSE/no-subscription outcomes.
- [x] T008 Run the existing stream replay/filter/cleanup coverage in `Essentials/tests/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj` (`StreamEndpointTests`); preserve backend behavior without changing its source.

---

## Phase 5: Polish and Combined Qualification

**Purpose**: Qualify the finished work against the integrated PR base and the normal product host.

- [x] T009 For local combined qualification, integrate PR #3133’s final independently reviewed frozen candidate (or its delivered main result) into the feature branch and review `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module.tsx` to confirm its separate index/copy and Recent-failure presentation changes are preserved without duplicating or overwriting the stream constructor fix; confirm the Spec 993 host-guide link and anchor referenced by `specs/994-structured-logs-stream-credentials/quickstart.md` resolve at the integrated base.
- [x] T010 Following the now-resolved Spec 993 composed-host procedure linked from `specs/994-structured-logs-stream-credentials/quickstart.md`, use a fresh disposable normal-host configuration and standard operator cookie session on separate Studio/Workbench origins; verify Recent and Sources return 200, the actual product stream returns 200 `text/event-stream`, and the page reaches Live even with an empty inventory. Do not add a telemetry-ingestion fixture or provider for this proof.
- [x] T011 In the same isolated normal-host browser context, trigger one bounded transient interruption and verify the actual EventSource retry authenticates and returns to Live without duplicate active subscriptions; inspect only safe request summaries and confirm no credential is in the URL or saved evidence, following the resolved `specs/993-studio-diagnostics-index/quickstart.md#composed-workbench-comparison`.
- [ ] T012 On the final combined candidate, run the client package test/build and Studio workspace typecheck, lint, and production build from `Studio/`; build both hosts; run the shared guide's CI dependency-graph restore before the Architecture suite, followed by `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`. Complete independent review and exact-head CI, merge only on green gates, then verify resulting-main Core/Studio/Maps and record the issue/claim-release receipt. In the implementation PR, set `specs/994-structured-logs-stream-credentials/spec.md` to Implemented and delete its `checklists/requirements.md` per `docs/reference/spec-lifecycle.md`; keep this task open until delivery and main qualification complete.

---

## Dependencies and Execution Order

- T001 is complete and supplies the unchanged-client baseline.
- T002 must produce a failing test and receive root review before T003.
- T003 precedes T004; T004 precedes the User Story 2 test additions and preservation checks (T005–T008).
- T009 depends on integration of the independently reviewed PR #3133 candidate and on T003–T008; it must happen before combined host qualification. Local stacking may precede the parent merge, but this issue’s PR/delivery must wait for the parent’s verified main result. Re-integrate any parent changes and repeat affected proof before acceptance.
- T010 precedes T011 because retry is verified in the same isolated browser context; T012 is the final qualification gate.
- User Story 2 follows User Story 1: both touch the same client test file and preserve the newly credentialed source lifecycle.

**Parallel opportunities**: None planned. The client tasks share `module.test.ts` and the approved order contains a mandatory RED/root-review checkpoint. Keep the final host and repository gates serial.

## Implementation Strategy

**MVP**: User Story 1 is the P1 product outcome: one existing constructor option, a rendered regression, and product-host proof that the authorized separate-origin page reaches Live. Story 2 and all preservation and final gates remain required before accepting the issue.

**Incremental delivery**: Complete the reviewed RED checkpoint, apply the one-call-site correction and focused green test, add client and backend preservation evidence, integrate PR #3133 base-first, then complete actual-host behavior and exact-head/resulting-main qualification. Do not add an ingestion fixture, provider, transport, endpoint, auth architecture, or bearer-token behavior.


## Root RED checkpoint

At the test-only checkpoint, all twelve cases execute: the original ten pass and the two new same/separate-origin rendered regressions fail only because the actual EventSource init options are undefined rather than credentialed. The exact existing Information filter URL and single instance assertions pass. Root reviewed the full test diff, shared failure-safe DOM/global teardown, and complete `3132-credential-red.log`. `src/module.tsx` remains unchanged. Root accepts this as valid behavior RED before T003; it is not browser cookie or server authorization proof.


## Root focused GREEN checkpoint

Root reviewed the one-call-site product diff and all new page/panel tests. The credential option makes both RED cases pass: 12/12. Two further rendered preservation cases bring the module suite to 14/14 and verify exact level/source/category query replacement, closing previous/active sources, entry rendering, pause buffering/resume, and disconnected/open callback behavior on the same source. Shared root/global cleanup remains failure-safe. Logs: `3132-credential-green.log` and `3132-preservation-green.log` in the external execution folder. Simulated callbacks do not prove browser cookie attachment or native reconnect; T010/T011 remain required. Backend safeguards, integrated base, full gates and delivery remain open.


## Root backend preservation checkpoint

On product checkpoint `533fab720`, the targeted Release test-project build passes with one existing CS8602 warning and no errors. The unchanged authorization matrix passes 16/16 with no skips; the unchanged stream replay/filter/cleanup suite passes 9/9 with no skips. All commands used the normal build-slot wrapper, and the stream suite completed in 829 ms despite machine contention; no failure or exclusion is attributed to load. Root reviewed complete command logs in the external execution folder: `3132-backend-target-build.log`, `3132-backend-authorization.log`, and `3132-backend-stream.log`. T007/T008 are complete. These safeguards do not establish browser cookie attachment or native retry; T009–T012 remain open.


## Local stacking refinement

Root may integrate the final independently reviewed PR #3133 source candidate for local T009–T011 qualification while the parent’s hosted CI runs. This keeps one implementation lane and does not waive delivery order: #3133 must merge and pass resulting-main qualification first. Before opening this issue’s PR, integrate main containing that delivered parent, check the candidate-to-main source delta, and repeat any affected proof. All final reviews and CI must target the final head. If the parent changes, re-integrate it and requalify the changed behavior; an earlier combined-host run is not acceptance of a later source revision. No merge over a failed/pending gate or hold is authorized.


## Root local parent integration checkpoint

Root integrated the independently reviewed frozen parent candidate `93ef68b8c55957a65851f340e4b6282667c421c0` under the approved local-stacking refinement. The single test-file conflict is resolved with all 14 credential/preservation cases and all six parent state cases retained. One shared failure-safe React-root/global-descriptor cleanup and existing control helpers replace duplicate page setup; no assertion was weakened. Root read the complete resolved file. Relative to the reviewed parent, the entire product module differs only by `{ withCredentials: true }` at its existing constructor. The parent index/copy/Recent-failure projection is preserved, and the Spec993 host-guide path/anchor resolves.

The frozen offline install, workspace typecheck and merged module suite (20/20, no skips) pass. The first targeted lint command ran from Studio and ignored the extension paths; it is not a lint pass. Running ESLint from repository root checks both files successfully with zero errors and one existing unused-useMemo warning. Both logs are retained. Diff/conflict checks are clean. T009 is complete for local qualification only; parent delivery/verified main integration, actual normal-host authentication/native retry, full candidate gates and own delivery remain open. No PR/push or parent merge is claimed.


## Root actual normal-host checkpoint

At combined candidate `884e483577be43acfc899ad1ba0763ae9037478d`, root built and launched both actual hosts from the Spec993 canonical procedure with a newly created private runtime, databases, key ring and empty package feeds; the canonical `elsa-3073` purpose/prefix was retained for its safety guards. Only normal composition was launched. A fresh browser signed in through the standard Development identity UI; no copied user session, product instrumentation or manual stream probe was used. Sources and Recent return 200 JSON; the actual product stream returns 200 `text/event-stream` and reaches Live. The host contains startup logs. A real category filter returns zero matching rows while remaining Live, providing filtered-empty evidence; a globally empty store is not claimed.

In the same page/context with unchanged filters and session, root stopped only its owned Workbench and restarted the same private host/data/keyring. The UI shows Disconnected/reconnecting; actual native retries receive connection-refused while down, followed by 200 SSE and Live without reload or another sign-in. Safe request summaries retain known paths/query keys, status/content type and timing only. The reference-object observer shows peak 1 active browser stream request; it does not directly count native EventSource objects or server subscribers. Those lifecycle mechanisms remain covered by the 20 rendered cases and unchanged 25 backend safeguards.

Root and an independent nonauthor read the complete safe receipts and screenshots under the external execution folder `3132-live/`. Preparation-script provenance is explicit; the script matches the canonical embedded Python and rejects a nonempty root. The initial visible-selector wait and final optional navigation timed out and are not counted as passes. Disconnected state was subsequently verified by class and screenshot. Final navigation happened after recovery evidence was saved; real navigation/unmount cleanup is not claimed by this run. Root verified the exact owned browser identity before stopping it, removed its isolated profile, confirmed both owned hosts exited and ports 5689/5695 were free, and deleted only the manifest-validated runtime root. Private logs/configuration/data/keyring were not retained.

T010/T011 are complete for the stated actual product-session, filtered-empty and same-page retry proof; no global-zero inventory, object/subscriber counter, final CI or delivery claim is implied. The full integrated workspace lint also passes with 0 errors / 48 existing warnings, including CSS and module-layout checks. Earlier workspace typecheck/full production build/both host builds pass.

The remaining local repository checks subsequently pass on record head `cf8c144808c3bb67a19cc60d731c569888badd8e`, whose product source is unchanged from `884e483577be43acfc899ad1ba0763ae9037478d`: the official CI dependency-graph restore, Architecture 578/578 with zero skips, and map freshness. Root reviewed the full logs and independently confirmed Essentials source is unchanged from the backend preservation checkpoint `533fab720`. See the public [combined local qualification receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3132#issuecomment-6301919401). T012 remains open for parent verified-main integration, final independent review and exact-head CI, merge, resulting-main qualification and claim release. Local passes do not complete those delivery gates.
