# Tasks: Studio Diagnostics Index

**Input**: Approved `spec.md`, `plan.md`, `research.md`, `data-model.md`, and `quickstart.md` in this directory.

**Execution note**: T001–T022 are complete. Root reviewed the implementation, runtime evidence, local gates, three independent final review axes, exact-head CI and resulting-main qualification. See the [final delivery receipt](https://github.com/elsa-workflows/elsa-foundation/issues/3073#issuecomment-6304236066) and [claim release](https://github.com/elsa-workflows/elsa-foundation/issues/3073#issuecomment-6304236613). Earlier pending checkpoints below are historical.

**Test cadence**: The repository does not declare a general test-first cadence. For this bounded feature, the approved plan requires the listed risk-based assertions and one RED checkpoint before any product edit. This is a work-unit gate, not a repository-wide TDD rule.

## Phase 1: Setup

**Purpose**: Reconcile the supplied clean-base evidence before changing product files.

- [x] T001 Verify the source revision, untouched-product test/build results, paired normal/baseline client manifests, before UI receipts, and cleanup record against `e656cc6042fd86d15d61c7278d846222054eb535` using `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-live-before-root-qa.md` and `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-live-baseline/`; preserve the distinction between before-state evidence and feature acceptance.

---

## Phase 2: Foundational — acceptance assertions before product edits

**Purpose**: Add focused behavior assertions for all four stories, then establish one explicit RED checkpoint that blocks product edits.

- [x] T002 Extend the host and Diagnostics assertions in `Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/overview.test.tsx`, retaining the existing direct-route helper checks in `Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/app-routing.test.ts` to prove the index receives the host-filtered navigation, built-in task descriptions and destinations are accurate, registered custom entries keep their labels/routes/gates with neutral fallback wording, and links agree with existing sidebar/dock/direct navigation. Include the paired baseline candidate set: omit OpenTelemetry and Structured Logs when their Studio modules are disabled, while retaining the registered Runtime evidence settings destination.
- [x] T003 Add empty and fully-filtered candidate-set assertions in `Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/overview.test.tsx`; require the no-links message only when no eligible entries remain, without exposing restricted names or describing backend health, outages, or empty data.
- [x] T004 [P] Declare the existing workspace DOM test dev dependencies in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/package.json` and `Studio/pnpm-lock.yaml`, then add OpenTelemetry page DOM assertions in `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/__tests__/module.test.ts` for the Metrics/Logs explanation before entry, conditional truthful Structured Logs handoff (or neutral `/diagnostics` handoff when eligibility cannot be established), and distinct trace loading, empty, permission, unavailable, and failure states.
- [x] T005 [P] Add Structured Logs destination DOM assertions in `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/__tests__/module.test.ts` for distinct loading, empty, permission, unavailable, and failure outcomes.
- [x] T006 [P] Extend `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/runtimeDiagnosticsSettings.test.tsx` to assert the “Runtime evidence settings” discovery label at the existing route, read-only users' view access with no save/PUT, and the authorized user's unchanged save gate and exact payload.
- [x] T007 [P] Create the bounded host-component browser fixture and acceptance assertions in `Studio/tests/browser/diagnosticsIndexFixture.tsx` and `Studio/tests/browser/diagnostics-index.spec.ts`, using existing host/navigation components to cover keyboard access, four widths, and light/dark themes; keep this fixture evidence distinct from composed-host proof.
- [x] T008 Run the single pre-implementation RED checkpoint after T002–T007 using the focused commands in `specs/993-studio-diagnostics-index/quickstart.md`; record the expected new-behavior failures while confirming existing classifier, widget isolation/loading/streaming, and settings save-payload guards remain green. No product edits begin until this checkpoint is recorded.

---

## Phase 3: User Story 1 — Find a diagnostic task (Priority: P1)

**Goal**: Show only destinations the current host has registered and already filtered for the user, with useful task descriptions and route parity.

**Independent Test**: The host tests prove accessible destinations, custom-entry fallback, and parity with existing navigation.

- [x] T009 [US1] Pass `filterAccessibleNavigation(navigation, access)`'s existing result from `Studio/src/apps/Elsa.Studio.Web/Client/src/app/App.tsx` into the Diagnostics index and render accurate descriptors for known built-ins plus existing custom entries. Preserve exact custom labels, destinations, gates, widget content/state behavior, ShellFrame navigation behavior, and direct-route behavior; do not add a second access rule or guess unrelated routes.
- [x] T010 [US1] Run the US1 host checks from `specs/993-studio-diagnostics-index/quickstart.md` (`overview.test.tsx`, `navPlacement.test.ts`, and `app-routing.test.ts`) and verify index/sidebar/dock/direct destinations agree for the current filtered navigation.

**Checkpoint**: Story 1 is testable independently; the baseline candidate still retains Runtime evidence settings when that Workflows entry is registered and accessible.

---

## Phase 4: User Story 2 — Understand when no diagnostic links are available (Priority: P1)

**Goal**: Explain the empty navigation case accurately, without presenting it as a backend or data failure.

**Independent Test**: Empty and fully-filtered candidate sets produce the no-links message; any eligible destination suppresses it.

- [x] T011 [US2] Update the empty-navigation state in `Studio/src/apps/Elsa.Studio.Web/Client/src/app/App.tsx` so it appears only when the current filtered candidate set has no diagnostic links and makes no backend-health, outage, or empty-data claim.
- [x] T012 [US2] Run the US2 empty/filtered cases in `Studio/src/apps/Elsa.Studio.Web/Client/src/__tests__/overview.test.tsx`, including the synthetic empty candidate set; do not use the baseline composition as proof of a zero-link state.

**Checkpoint**: Stories 1 and 2 work independently; restricted destinations remain undisclosed.

---

## Phase 5: User Story 3 — Choose a supported telemetry destination (Priority: P2)

**Goal**: Explain unsupported OpenTelemetry views before entry and keep destination-owned telemetry states truthful.

**Independent Test**: OpenTelemetry and Structured Logs DOM tests distinguish supported trace states from unsupported, inaccessible, unavailable, and failed destinations.

- [x] T013 [P] [US3] Update `Extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module.tsx` so Metrics and OpenTelemetry Logs are explained as unsupported before users can enter placeholder content; offer a direct Structured Logs link only when registration and access are both established, otherwise use the neutral Diagnostics handoff. Preserve supported trace behavior. In `Extensions/Diagnostics.StructuredLogs/studio/Elsa.Studio.Diagnostics.StructuredLogs/Client/src/module.tsx`, correct the misleading successful-empty row copy during connecting or failure and keep a known Recent classification visible through stream callbacks and pause/resume until the next request effect, without changing requests, classification, stream/buffer or subscription behavior.
- [x] T014 [US3] Run both telemetry DOM suites from `specs/993-studio-diagnostics-index/quickstart.md` and verify all loading, empty, permission, unavailable, and failure states remain distinct; classifier assertions alone do not satisfy this check.

**Checkpoint**: Users can identify the supported trace task and are not sent into unsupported placeholder views.

---

## Phase 6: User Story 4 — Open Runtime evidence settings and retain contributed diagnostics (Priority: P2)

**Goal**: Make the existing capture-policy settings discoverable to viewers while preserving the existing save gate and contributed widgets.

**Independent Test**: View-only and authorized-save cases preserve route, authorization, payload, policy, and widget outcomes.

- [x] T015 [P] [US4] Present the existing `/workflows/runtime-diagnostics` destination as “Runtime evidence settings” in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/module.tsx` and `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/RuntimeDiagnosticsSettingsPage.tsx`; retain its current view access, separate write authorization, policy, and save behavior.
- [x] T016 [US4] Run `runtimeDiagnosticsSettings.test.tsx` and the Diagnostics widget assertions in `overview.test.tsx`; verify view-only access issues no write request, authorized saves retain the exact existing payload, and a failing widget does not hide unrelated widgets or tool links.

**Checkpoint**: Stories 1–4 work with the existing settings and contribution contracts intact.

---

## Phase 7: Polish and cross-cutting acceptance

**Purpose**: Complete visual, composed-host, regression, review, and lifecycle gates.

- [x] T017 Run `pnpm exec playwright test tests/browser/diagnostics-index.spec.ts` from `Studio/` and verify keyboard reachability and no clipped descriptions or horizontal scrolling at 320, 768, 1024, and 1440 pixels in both light and dark themes.
- [x] T018 Execute the source-backed normal Workbench comparison on the owned port/root, before and after the product change. The completed run used the reviewed external execution procedure and produced the receipts below; the portable repeat setup, launch, and cleanup recipe is now maintained in `specs/993-studio-diagnostics-index/quickstart.md` under “Composed Workbench comparison” (that adaptation was not used for the recorded runs). Verify the client manifest, seeded disposable Development-admin access, Diagnostics navigation and route parity, destination-owned states, Runtime evidence settings writer behavior (with view-only authorization proven separately by T006/T016), zero page errors, and cleanup; retain before receipts under `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-live-baseline/` and use fresh roots under `execution/3073-live-after/` for after receipts.
- [x] T019 Execute the paired baseline Workbench comparison on its separate owned port/root, with the backend baseline and copied Studio feature configuration omitting only `OpenTelemetryStudio` and `StructuredLogsStudio`. The completed run used the reviewed external execution procedure and produced the receipts below; the portable repeat setup, launch, and cleanup recipe is now maintained in `specs/993-studio-diagnostics-index/quickstart.md` under “Composed Workbench comparison” (that adaptation was not used for the recorded runs). Verify both modules are disabled in the returned client manifest, those two links are absent while registered Runtime evidence settings remains discoverable, and cleanup completes; retain before receipts under `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-live-baseline/` and use fresh roots under `execution/3073-live-after/` for after receipts. Treat this anonymous run as registration/absence evidence only; the empty-index and authenticated permission cases require their separate tests.
- [x] T020 Run the complete affected Studio/Web, OpenTelemetry, Structured Logs, and Workflows test suites plus applicable type, lint, and build scripts; then run the architecture guard and `dotnet run --project tools/maps/Elsa.Maps.Generator -- check`. Use package scripts and the approved quickstart; report any gate that cannot run rather than treating it as passed.
- [x] T021 Perform a DRY and final-diff review across the touched host, telemetry, workflow, and test files; confirm the existing navigation/access helpers and route/save contracts remain authoritative and no new runtime dependencies, SDK/API contracts, backend capability model, or unrelated route discovery were added.
- [x] T022 Complete the required three-axis reviews and exact-head CI on the final change; merge only with green gates, verify resulting-main CI and Maps, publish the issue receipt/release state through the root-owned delivery process, and in the implementation PR set the spec status to `Implemented` and delete `specs/993-studio-diagnostics-index/checklists/requirements.md` before merge per `docs/reference/spec-lifecycle.md`.

---

## Dependencies and execution order

- T001 is the supplied before-state evidence gate. T002–T007 add the risk-based assertions and bounded fixture; T008 depends on all six and blocks every product edit.
- T002 and T003 share `overview.test.tsx`, so edit them sequentially. T004–T007 touch separate test/fixture files and may be prepared independently.
- T009 precedes T011 because both change `App.tsx`; run T010 before proceeding to US2. T013 and T015 touch separate product packages and may proceed independently after T008. Each story's focused test task follows its implementation.
- T017–T019 require all story implementations. Run each composition’s backend/Studio pair on owned ports 5695/5689; stop and clean up that pair before starting the other composition. T020–T022 depend on completed implementation and acceptance evidence; final review/CI and resulting-main verification are terminal gates.

## Incremental delivery

The first useful increment is US1, with US2 completed alongside it before the index is accepted so a filtered empty state is truthful. US3 and US4 can then be validated as separate destination increments. The feature is not complete until both normal and baseline composed-host before/after evidence, all visual and regression checks, and the final delivery gates pass.

## Notes

- The baseline composition is not presumed to be a zero-tool index: it must omit the two disabled telemetry module links and retain Runtime evidence settings when that entry is registered and accessible. Test zero eligible links with a separate empty/fully-filtered candidate set.
- Normal composed-host evidence uses the seeded disposable Development identity. The anonymous baseline proves registration/absence only; component or authenticated normal-composition evidence proves permission and read-only/write boundaries.
- Fixture output is not composed-host proof. Neither a registered route nor widget count establishes backend health or data availability.

## Root RED checkpoint

Before any product edit, the corrected focused suites execute 60 cases: 50 pass and 10 fail for intended behavior gaps (Web 23/5, OpenTelemetry 10/1, Structured Logs 13/2, Runtime settings 4/2; pass/fail). Existing widget, helper/classifier, route and save/permission guards remain covered. The initial OpenTelemetry import failure is excluded: declaring the two existing DOM test dev dependencies fixes collection, with a bounded lockfile diff and successful offline frozen install. Root removed a redundant fabricated-route test; real module registration and composed-route checks remain authoritative.

The mounted browser fixture also fails on the expected missing Console index link at 320px/light; a separately selected single-case run confirms the same behavior without max-failure interruption. The other seven viewport/theme cases await the full post-implementation run. Logs and traces are retained under the external execution folder; no fixture result is described as composed Workbench proof. Root reviewed all changed test/fixture paths and recorded the state-presentation refinement before allowing implementation.

## Root qualification receipt

Product checkpoint `2121e070f45c3a43f657dc126143f59c7b724303` includes main `e9a3d18aa25ab30e57e6c6a1afdf6dc7fb63fdc0`. Subsequent changes correct two stale registration-label expectations, remove an unused browser binding, and finalize these records; they do not change the product behavior under test.

| Gate | Root-reviewed evidence |
|---|---|
| Affected suites | Web 568/49 files; OpenTelemetry 11/1; Structured Logs 15/1; Workflows 2,155/169, all pass. |
| Risk-based assertions | Focused 60/60 pass; permissions, settings payload, widget isolation and destination states covered. |
| Frontend tools | Workspace types, lint and production build pass. The new unused fixture binding was removed and its targeted lint is clean; unrelated warnings remain reported. |
| Host/architecture/maps | Both .NET hosts build; all 578 architecture tests pass after the existing CI procedure restores evaluated Release and Debug graphs; dependency-map check passes. The earlier 559/19 missing-restore-input failure remains in the external log. |
| Browser fixture | Eight viewport/theme cases pass with actual Tab/Enter traversal and overflow assertions; root inspected all screenshots. |
| Composed hosts | Normal administrator sees all four expected links, matching sidebar/dock/direct routes. Paired anonymous baseline manifest disables both telemetry modules and retains only Console and Runtime evidence settings links. Both indexes pass scoped axe checks and have no document overflow in all eight width/theme combinations each; no page errors. This is scoped index evidence, not full WCAG or whole-shell conformance. |
| Settings | Normal private policy saves Off and restores its original DiagnosticSnapshot value with two 200 PUTs and preserved subject overrides. Component tests separately prove view-only/no-write access. |
| Cleanup | Private keyring tables verified without reading keys; browsers closed, exact owned host processes stopped, sessions exited zero, ports free, and both owned runtime roots removed. Evidence retained. |

The Workflows parallel rerun failed two unchanged authoring cases while load exceeded 70 on eight cores. The focused 195-case and complete 2,155-case serial runs pass without changing those cases, product code or timeouts; preserve the failures without claiming a causal repair. An interrupted browser-tool call ended both first normal-host handles; authoritative missing handles/processes justified restarting only the owned normal profile. The saved policy was read before any repeated write, then the recorded save/restore pair completed. Initial harness navigation/assertion corrections are documented separately and are not product failures.

Request-level normal-host proof confirms two pre-existing task failures outside this slice: #3131 owns OpenTelemetry's client/API path, verb and DTO mismatch; #3132 owns cross-origin Structured Logs stream credentials. Both are native dependencies of final qualification #3075. Do not present an unavailable message or scoped index pass as proof that either diagnostic task works. The finite final qualification also retains the observed crowded normal-shell topbar at 320px for evaluation.

Detailed logs, manifests, scoped accessibility results, keyboard routes, request summaries, screenshots and cleanup are retained in `/Users/sipke/Documents/Codex/2026-10-19/studio-ux-audit/execution/3073-live-after/` and `3073-root-local-qa.md`. Public evidence: #3073 implementation/verification receipts and #3075 comment 6298534519. Final exact-head review/CI and resulting-main qualification remain pending under T022.


## R4 follow-up: state precedence

Root reopened T013/T014 after finding that later native-stream callbacks may replace a known Recent denial/unavailable/failure message. The accepted scope is presentation precedence only, preserving requests, classification, the existing EventSource and native retry/subscription/entry/buffer behavior. The existing four DOM cases must first exercise both callbacks and establish valid behavior RED before the product correction. The earlier results remain historical; they did not cover this callback sequence. Affected checks and fresh final exact-head reviews/CI remain required under T022.


Root accepted the expanded test-only RED before any product correction: 16 cases execute, 11 pass and exactly 5 fail. Four known 401/403/404/500 messages are replaced by the existing stream error callback; the recovery case initially sees the correct permission row, then Pause incorrectly changes it to successful-empty copy. Root reviewed the complete diff and both logs (`3073-state-precedence-red.log`, `3073-state-precedence-recovery-red.log`). The EventSource constructor/product source remains unchanged at this checkpoint. Recovery after a successful filter change is still an assertion awaiting GREEN execution, not a claimed result.


The presentation-only correction is GREEN: Structured Logs 16/16 and OpenTelemetry 11/11, no skipped cases. A separate internal Recent failure is reset when its request/filter effect starts and projected over the existing connection status/detail in the hook result. Requests, the constructor, all native stream callbacks/retry/cleanup, and pause/buffer mechanics are unchanged. The recovery case proves failure text survives Pause/Resume, the previous stream closes on a Warning filter change, exactly one replacement opens, and successful empty/Live presentation returns without a stale permission message. Root reviewed the complete test/product delta and logs (`3073-state-precedence-green.log`, `3073-state-precedence-otel.log`). T013/T014 are complete again; relevant tools/build, final independent review, exact-head CI and resulting-main gates remain open under T022. Earlier full workspace/composed evidence remains historical and is not new callback-runtime proof.


## Final delivery and resulting-main qualification

All three independent R5 review axes are clean on `93ef68b8c55957a65851f340e4b6282667c421c0`. Exact-head CI passed, the self-merge audit preceded the normal merge, and PR #3133 merged as `4ef8ab8c9cca8cffc6223100d02074482301e154`. The spec is Implemented and its terminal requirements checklist was removed in that implementation PR.

Containing main `7fb745f8604b6ed67a2eae60266976c98bce949e` subsequently passed [Core CI including Maps](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39509964642) and [all three Studio jobs](https://github.com/elsa-workflows/elsa-foundation/actions/runs/39509964406). Root read the complete build/test log: 93 project summaries, 17,360 passed / 0 failed / 32 skipped; Workbench contributes 59 passed with no failures or skips. Architecture and Core-only jobs also pass. Conditional EF/container/Secrets-composition jobs were skipped, not executed passes. Earlier cancelled main runs and the failed first candidate attempt remain historical evidence; this success does not establish a cause or repair for #3110/#2293.

Root published the final receipt and released claim 6296385033. Issue #3073 is closed with all acceptance criteria checked; Project 61 is Done / Verification Passed. T022 is complete. Independent credential fix #3132, OpenTelemetry alignment #3131, and final composed qualification #3075 retain their own open delivery gates.
