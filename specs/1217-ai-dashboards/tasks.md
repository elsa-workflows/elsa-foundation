# Tasks: AI-authored personal and shared dashboards

**Status:** Active dependency-ordered delivery plan after owner resume. Native and Weaver implementation is in progress; Copilot/options configuration is selected, usable live access remains unqualified. Completed code tasks are not feature acceptance.

**Checkpoint:** Frozen9f62f2782 passed Architecture618/618, maps/tier checks and package006 with20archives/19native cases. Query/Workflow/Acme passed42/42,39/39,31/31; Resource62/62 and Authoring57/57 passed after R3; Dashboard182/182 and rendered browser10/10 pass. Native005 and preliminary diagnostic failures are retained; final committed composed/independent UX, usable live Copilot, current CI/merge/resulting-main gates remain open. See the [evidence index](evidence/README.md).

**Cadence:** ADR 0080 risk-based behavior tests apply. The constitutions do not mandate greenfield TDD; use test-after or test-first where useful, and retain red/green bite-proof for behavior claims before completion. Do not add mirror tests or unrelated runtime runs for this docs checkpoint.

## Phase 1 — Setup and design gates

- [x] T001 Confirm owner decisions, applicable native contract review and current provider/access status in specs/1217-ai-dashboards/spec.md and checklists/requirements.md.
- [x] T002 Refresh claim/open PR/base health and source ownership on #3604; record the exact baseline in specs/1217-ai-dashboards/research.md.
- [x] T003 Finalize distinct project/package identities and dependency graph in specs/1217-ai-dashboards/plan.md, preserving the existing Elsa.Studio.Dashboard RCL.
- [x] T004 Finalize the query/resource/Weaver contracts and pre-code observable states in specs/1217-ai-dashboards/contracts/ and ux-acceptance.md.

## Phase 2 — Shared native foundation

- [x] T005 Create public typed dataset/query/result/resource contracts and domain failures in Essentials/src/Studio/Dashboard/Core/ with tracked public API.
- [x] T006 Implement named declaration/validation/execution/drill fan-in and duplicate/missing-owner checks in Essentials/src/Studio/Dashboard/Management/.
- [x] T007 Implement bounded structural/visualization/parameter validation and operational cancellation/concurrency in Essentials/src/Studio/Dashboard/Management/.
- [x] T008 Create provider-neutral command/query definition persistence contracts in Essentials/src/Studio/Dashboard/Core/ and EF mappings in Persistence/EntityFrameworkCore/.
- [x] T009 Implement trusted tenant/canonical actor/identity audience mapping and coarse/resource capability evaluation in Essentials/src/Studio/Dashboard/Management/.
- [x] T010 Wire native API capabilities/error contracts in Essentials/src/Studio/Dashboard/Api/; never accept a saved author principal.
- [x] T011 Add risk-based query/auth/composition tests in Essentials/tests/Studio/ and classify new projects in tools/ci/test-tiers.json; registration tests only for logic-bearing wiring.
- [x] T012 Update extension-point catalogs, root EXTENSION_POINTS.md, package manifests and solution entries for the new contracts/features.

## Phase 3 — US1: Ask, preview and save a novel question

**Independent test:** use the corresponding story scenarios in [spec.md](spec.md) and [UX record](ux-acceptance.md); a fixture/build alone cannot satisfy them.

- [x] T013 [US1] Extract the minimal Runtime-owned reusable inspection contract in Essentials/src/Workflows/Runtime/Inspection/Core/ and adapt Api/ without copying authorization logic or changing fail-closed semantics.
- [x] T014 [US1] Implement Workflow dataset declaration and bounded authorized started-cohort reads/current-folder enrichment in Essentials/src/Workflows/Dashboard/Queries/.
- [x] T015 [US1] Implement module-authoritative metrics/comparison-result predicates and exact no-denominator/duration/run-kind/time meaning in Essentials/src/Workflows/Dashboard/Queries/.
- [x] T016 [US1] Implement durable saved-question create/edit/readback and native validation/preview/save transport in Essentials/src/Studio/Dashboard/Management/ and Api/.
- [x] T017 [US1] Add the native question editor and all supported field/filter/group/metric/sort/time/parameter/presentation controls in Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/.
- [x] T018 [US1] Implement Dashboard context/discovery/preview tools and retained-disclosure admission in Extensions/Agent/src/Dashboard/ through existing generic contracts.
- [x] T019 [US1] Implement typed preview/proposal/Use control with safe refinement continuity and accepted parameter defaults in Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/native/.
- [ ] T020 [US1] Use owner-selected existing Copilot backend options/configuration under Extensions/Agent/; enforce the SDK server tool/context boundary and qualify configured live access without exposing credentials.
- [ ] T021 [US1] Verify novel query numerical results and aggregate/comparison membership independently in Essentials/tests/Studio/ and Studio/tests/acceptance/.
- [ ] T022 [US1] Qualify actual selected-provider clarification/refinement/preview/native Save and record safe exact-source evidence under specs/1217-ai-dashboards/evidence/.

## Phase 4 — US2: Reuse, arrange and edit without AI

**Independent test:** use the corresponding story scenarios in [spec.md](spec.md) and [UX record](ux-acceptance.md); a fixture/build alone cannot satisfy them.

- [x] T023 [US2] Implement named Dashboard resources/tile identity/current question revisions/validated overrides in Essentials/src/Studio/Dashboard/Management/.
- [x] T024 [US2] Implement EF durable definitions/revisions/keyed creates/CAS/retention in Essentials/src/Studio/Dashboard/Persistence/EntityFrameworkCore/.
- [x] T025 [US2] Add Dashboards Mine/Shared collection, Questions collection and layout controls alongside unchanged Home in Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/.
- [x] T026 [US2] Implement private copy, reuse impact/Save copy, history/restore and incompatible override recovery in Essentials/src/Studio/Dashboard/Api/ and Dashboard/Client/src/.
- [x] T027 [US2] Implement cancellable native refresh/reopen/revision-aware outdated states and provider-unavailable native operation in Dashboard/Client/src/.
- [ ] T028 [US2] Verify native edits of every query facet, duplicate tiles, concurrent saves, restart persistence and zero provider-call increment in Essentials/tests/Studio/ and Studio/tests/acceptance/.
- [ ] T029 [US2] Run existing whole Home/Dashboard frontend suites and composed saved-layout/default/widget regression in Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/__tests__/ and tests/acceptance/.

## Phase 5 — US3: Share definitions with independent viewer access

**Independent test:** use the corresponding story scenarios in [spec.md](spec.md) and [UX record](ux-acceptance.md); a fixture/build alone cannot satisfy them.

- [x] T030 [US3] Implement selected same-tenant user/role grants and owner/editor/viewer/copy rights in Essentials/src/Studio/Dashboard/Management/ with current store ACL checks.
- [x] T031 [US3] Implement protected projections, tile-ID/revision preserve-reference updates and metadata-only preservation of withheld definitions in Essentials/src/Studio/Dashboard/Api/.
- [x] T032 [US3] Add audience review, read-only/editor actions, question dependency sharing and revocation recovery to Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/.
- [x] T033 [US3] Verify cross-tenant/nonexistence behavior, ACL revoke/downgrade concurrency, history/copy and hidden-reference preservation in Essentials/tests/Studio/.
- [ ] T034 [US3] Qualify distinct users/tenants/field-record-aggregate scopes and declared IAM permission propagation on the actual host in Studio/tests/acceptance/; record forbidden-data controls.

## Phase 6 — US4: Interpret and investigate results

**Independent test:** use the corresponding story scenarios in [spec.md](spec.md) and [UX record](ux-acceptance.md); a fixture/build alone cannot satisfy them.

- [x] T035 [US4] Implement accessible chart/table/list/metric rendering and authoritative interpretation/coverage/refresh provenance in Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/.
- [x] T036 [US4] Implement current-access native drill and supported bounded export through source-owned contracts in Essentials/src/Studio/Dashboard/Api/ and Workflows/Dashboard/Queries/.
- [x] T037 [US4] Implement distinct zero/empty/no-denominator/unknown-incomplete/stale/failed/cancelled/source-incompatible states in native API and Dashboard/Client/src/.
- [ ] T038 [US4] Verify source/module/schema removal, permission/scope changes, superseded reads, cost bounds and historical evidence refusal in Essentials/tests/Studio/ and Studio/tests/acceptance/.
- [ ] T039 [US4] Inspect keyboard/focus, empty/populated/read-only/error, all supported renderers and relevant theme/narrow states under specs/1217-ai-dashboards/ux-acceptance.md.

## Phase 7 — US5: Independent business-dimension extension

**Independent test:** use the corresponding story scenarios in [spec.md](spec.md) and [UX record](ux-acceptance.md); a fixture/build alone cannot satisfy them.

- [x] T040 [US5] Create separately packaged Acme.Processing record/store/dataset/metric fixtures under Extensions/Examples/src/Acme.Processing/ with explicit customer category and duration.
- [x] T041 [US5] Implement its declaration/validation/execution/drill/disclosure contributions through the same public contracts in Extensions/Examples/src/Acme.Processing/.
- [x] T042 [US5] Build/install the independent package into an owned host and prove processing-time grouping with independent math, tenant/field/aggregate controls and no generic dataset branch in tools/studio/Dashboard.Conformance.Probe/.
- [ ] T043 [US5] Qualify the actual-provider business question and native save/reopen/refresh/drill journey in Studio/tests/acceptance/ with safe numerical evidence.

## Phase 8 — Documentation, review, merge and resulting main

- [x] T044 Write user/integrator docs for datasets/questions/metrics/sharing/access/coverage/limits/recovery and update owning README/EXTENSION_POINTS.md catalogs.
- [ ] T045 Run whole affected suites plus required architecture/map checks and locked restore after graph changes; record counts and bite-proof evidence on the PR.
- [ ] T046 Run committed actual-host journeys and publish current-head downloadable screenshots/traces via the existing UX evidence path; obtain independent UX acceptance.
- [ ] T047 Complete independent correctness/standards/spec reviews against the exact PR base/head; fix concrete findings in new commits and repeat under the current review rules.
- [ ] T048 Ensure all acceptance mappings and required checks pass, set spec lifecycle status in the finishing PR, and merge only through the applicable authorized path.
- [ ] T049 Verify resulting-main CI and feature journeys, reconcile #3604 and specs/1217-ai-dashboards/evidence/; leave any failed/skipped/unavailable/incomplete obligation explicit.

## Dependencies and parallel opportunities

D0 applies to every implementation task. Native foundation precedes the native portions of US1–US5. US2 uses question persistence; US3 adds sharing to the same resources; US4 attaches source-owned investigation/states; US5 proves the public contract independently. Full provider qualification in US1/US5 waits for provider/access, while applicable reviewed native work can proceed independently. D5 cannot finish before every story and required evidence passes.

Use one implementation integration lane. Once contracts are fixed, independent package fixtures, native frontend controls and backend tests may be delegated in disjoint paths with explicit integration points. For US1, source math and preview UI can be reviewed separately; US2 layout and persistence tests; US3 ACL API and share controls; US4 renderer/accessibility and source-drill tests; US5 independent package and oracle. No `[P]` tasks are marked initially because project/contract dependencies are not yet finalized.

The first testable increment is a novel native question plus saved artifact, followed by native reuse/sharing before conversation qualification. It is an internal increment only: #3604 remains open until the full provider, product, authorization, review/merge and resulting-main outcomes pass.
