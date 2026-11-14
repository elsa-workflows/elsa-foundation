# Tasks: Workflow and activity testing

**Input:** [spec](spec.md), [plan](plan.md), [research](research.md), [model](data-model.md), [contract](contracts/testing.md), [UX acceptance](ux-acceptance.md). #3588/#3892 are the canonical feature records; #3901 remains a backlog coordination proposal. Neither feature closes at a partial milestone.

**Cadence:** Risk-based implementation followed immediately by meaningful behavior tests. Preserve valid tests and use causal red/green controls for changed behavior. Run touched complete projects/listed counts and honor build slots. Review each artifact before the next phase. `[P]` means independent files after prerequisites, never concurrent edits to shared registrations/models.

The early owned probes and internal pure runtime witnesses are not public test execution. Public admission/composed host activation requires the shared effect-closure, fixture, authority, finite scope and cleanup foundations below. Story-specific UI may be implemented against those reviewed ports, but its composed acceptance waits for normal host activation and every required profile.

## Phase 1 — Scope and reviewed design (M0)

- [x] T001 Read live issue claims/PRs, org/repo rules and initiative parents; preserve feature ownership in #3588/#3892 and isolate the resumed specification slice. Program #3901 is a backlog proposal, not an active program.
- [x] T002 Record owner product choices and independently review requirements in `spec.md` and `checklists/requirements.md`; resolve fixture ownership, native witness minimum and unsaved candidate findings.
- [x] T003 Research normal execution, source revisions, Agents conventions and Studio/Elsa3 references in `research.md` and `ux-acceptance.md`.
- [x] T004 Reconcile/review suite latest-at-admission choice, manual step conversion, full context ownership and timer clocks across `plan.md`, `data-model.md` and `contracts/testing.md`.
- [x] T005 Review this dependency-ordered `tasks.md` against every FR/SC and complete the requirements readiness checklist.

## Phase 2 — Blocking witnesses and shared backend foundations (M0–M3)

- [x] T006 Execute an owned temporary normal-runtime selected-node/nested-capture probe; record exact source/commands/outcomes in `research.md` without advertising unsupported production behavior.
- [ ] T007 Implement/prove trusted test entry and full-artifact terminal boundary in `Workflows/Runtime/Core/Models/` and `Elsa.Workflows.Runtime/Services/WorkHandlers/`; preserve published starts and zero-outcome completion.
- [ ] T008 Implement/prove runtime-owned lexical read/write/close/capture frames in `Elsa.Workflows.Runtime/Services/Values/`, with no fabricated predecessor/ancestor activity states and shared direct/expression protection-aware fixture resolution.
- [ ] T009 Add selected leaf/composite/nested/reusable, missing/null/fixture-shadow, capture writeback and cancellation behavior controls in `Essentials/tests/Workflows/Runtime/Tests/` before advertising any profile.
- [ ] T010 Prove owned controlled timer eligibility versus wall claims, timer-before-bookmark race and ordinary-pump isolation in `Runtime/Scheduling/` and its focused test project before library integration.
- [ ] T011 Add the optional production domain purpose/pin to `.specify/memory/constitution.md` and `docs/reference/elsa-domain-tree.md`; preserve `Elsa.Testing` test grouping and ratified dependency gates.
- [ ] T012 Create actual Testing Core/feature/API/provider projects in `Essentials/src/Workflows/Testing/`, plus public API files, solution entries and correct test tiers; do not create empty provider scaffolds.
- [ ] T013 Implement immutable shared case/suite/candidate/target/setup/step/assertion/run/report models and provider-neutral CQS/contribution ports in `Testing/Core/` per `contracts/testing.md`.
- [ ] T014 Implement typed source-revision authoring and exact revision capture/pins in `Testing/Elsa.Workflows.Testing/` and `Testing/Persistence/EntityFrameworkCore/`; preserve trusted scope and CAS.
- [ ] T015 Implement fenced/idempotent admission, append-only observations/final reports and durable cleanup responsibility in the Testing feature/provider; never use editable source documents as mutable results.
- [ ] T016 Add publishing-owned current-editor/retained/exact-version candidate preparation port in `Workflows/Publishing/Core/` and compiler adapter in its owning feature; capture unsaved content without business draft mutation.
- [ ] T017 Implement node/path/model/contract compatibility and deliberate rebind/new revision in Testing feature; allow compatible authored configuration changes while rejecting removed/moved/ambiguous targets.
- [ ] T018 Implement target capability/context preflight through the runtime-owned entry/frame seams; qualify direct, variable/expression/prior-output and reusable-boundary setup in `Testing/Elsa.Workflows.Testing/`.
- [ ] T019 Implement named target/setup/step/assertion/effect/time contribution aggregation with exact profile versions and missing/duplicate diagnostics in Testing Core/feature and module owners.
- [ ] T020 Implement preflight complete owned-work effect closure and runtime undeclared-effect refusal; unknown custom/script/parent-dependent profiles remain unavailable rather than silently live.
- [ ] T021 Implement server-owned ordered step execution and exact Test Drive trigger/wait-driver adapters in Testing feature; retain idempotent resolved wait/generation delivery receipts.
- [ ] T022 Implement versioned output/outcome/fault/wait/path/interaction assertion providers with missing/withheld distinctions and expected/actual differences in Testing feature.
- [ ] T023 Implement bounded state-driven settlement and complete observation intervals in Testing feature; forbid non-invocation success with unknown async responsibility.
- [ ] T024 Integrate finite real deadlines, close-admission fence, root/child/detached/ingress/timer reconciliation and durable cleanup in Testing feature and existing runtime scope owners.
- [ ] T025 Implement fixture preparation/teardown ownership receipts and fresh mutable state per attempt, including unknown creation and partially failed preparation reconciliation.
- [ ] T026 Implement cancellation-intent versus settled verdict, interrupted/pending/incomplete recovery and read/step reauthorization in Testing API/feature; no blind redispatch after disconnect.
- [ ] T027 Implement bounded authorized library CRUD/history, logical suite membership and once-at-admission/export latest-member capture in Testing API/feature; no mutable latest resolution after admission.
- [ ] T028 Implement versioned deterministic portable bundle validation/digests/mapping, atomic import analysis/apply and collision receipts in Testing feature/provider.
- [ ] T029 Test immutable capture, stale saves, scope isolation, pins and interrupted durable receipts in `Essentials/tests/Workflows/Testing/{Tests,IntegrationTests}/`.
- [ ] T030 Implement attempt-local durable business clock, controlled timer owner and normal resume delivery in Runtime/Scheduling and Activities/Scheduling, retaining wall-clock leases/expiry.
- [ ] T031 Integrate qualified Event and User Task actions through existing owner ports; record test-scoped authority/ingress in those module contributors without resuming unrelated programs.
- [ ] T032 Add context-aware HTTP substitute/integration transport boundary and safe interaction journal in `Essentials/src/Activities/Http/`; validate durable attempt identity across fresh activations/resumes.
- [ ] T033 Resolve explicit authorized test-system connection mappings/secret references through existing owner contracts; redact portable files/evidence in Testing/HTTP contributors.

## Phase 3 — US1: Selected activity manual test to saved case (M2/M4)

- [ ] T034 [US1] Add authorized candidate/preflight/ad-hoc run/observation/rebind API operations in `Testing/Api/` using immutable operation fingerprints and typed safe diagnostics.
- [ ] T035 [US1] Add common Target/Setup/Results workbench and explicit selectedActivity Tests entry in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/testing/` and `workflow-editor/InspectorPanel.tsx`; no scope-owner fallback or edit-only permission inheritance.
- [ ] T036 [US1] Preserve unapplied code buffer and require Apply/Reset before capture in `WorkflowCodeView.tsx`/`WorkflowEditor.tsx`; test autosave-disabled unsaved value and persisted-draft readback.
- [ ] T037 [US1] Convert manual observations into author-confirmed supported ordered steps/stable wait selectors plus chosen assertions in Testing API and Studio workbench; never replay old runtime wait IDs.
- [ ] T038 [US1] Verify activity target kinds, zero outside invocations, stale/rebind, missing context and manual wait→save→reload replay through normal REST/runtime and `Studio/tests/acceptance/` feature journeys.

## Phase 4 — US2: Full-workflow multi-step business checks (M3/M4)

- [ ] T039 [US2] Add Steps/Assertions UI and clear lifecycle/verdict/clock state to `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/testing/`; retain explicit supported/unavailable reasons.
- [ ] T040 [US2] Prove multi-step/time, expected-fault/wait teardown, wrong-output/outcome/forbidden-call regression and early-empty-interaction false-pass controls in Testing integration tests and composed journeys.

## Phase 5 — US3: Library, portability and headless CI (M4)

- [ ] T041 [US3] Implement authoritative JSON and documented JUnit report projection, skip policy and exit aggregation in Testing feature; preserve no-verdict/empty/all-skipped/incomplete distinctions.
- [ ] T042 [P] [US3] Add `test run` command using shared APIs in `Essentials/src/Cli/Elsa.Cli/`; explicit backend/candidate/environment/auth references, Ctrl+C reconciliation and report paths.
- [ ] T043 [P] [US3] Add Tests collection Cases/Suites/Results, save/reopen/conflict/history/import/export flows and module navigation using shared Studio controls in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/testing/` and `module.tsx`.
- [ ] T044 [US3] Test source CAS, membership concurrency, export exactness, import collisions/tampering/missing modules, permission scope and mixed report/exit semantics in Testing and CLI complete projects. Also replay retained candidate/resolved member manifests after current edits, then expire a retained dependency and prove Unavailable without latest fallback.
- [ ] T045 [US3] Pack/install CLI as an external consumer and execute same exported candidate/revisions against an owned host; compare manifests/verdicts with Studio and verify durable history after authoring-host restart.

## Phase 6 — US4: Qualified effects, models and real integration (M3/M5)

- [ ] T046 [US4] Publish model-owned BPMN/native/direct/RunBpmn support matrix through its contributor; qualify any advertised scope/time/cleanup support or return exact unavailable reasons without extraction.
- [ ] T047 [US4] Add Studio dependency/fixture/time editors and inspectable mode provenance using module contributions in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/testing/`.
- [ ] T048 [US4] Prove substitute/real owned integration, no fallback on missing/unknown boundaries and a custom extension's Studio/headless parity/absence controls in module/Testing integration tests and composed journeys.

## Phase 7 — US5: Stop and inspect isolated runs (M3/M5)

- [ ] T049 [US5] Add Stop/reconnect/expiry/cleanup states and protected-state clearing on tenant/backend/session loss to shared Studio workbench/history.
- [ ] T050 [US5] Test concurrent same-name waits, ordinary ingress exclusion, duplicate delivery, timer races, cancellation/expiry/restart, fixture contamination/partial cleanup and revoked authority in complete affected integration suites.
- [ ] T051 [US5] Compose normal Workbench catalog/project references and shell Testing feature/durable-provider activation in `Essentials/apps/Elsa.Workbench/Elsa.Workbench.csproj` and `shells.json`; prove startup/schema registration, source/result persistence and optional-off design-only/runtime-only host shapes before composed journeys.

## Phase 8 — Complete acceptance, review and closeout (M5–M6)

- [ ] T052 Document installed support profiles, extension APIs/contributors/handlers/tasks, permissions, portable schema, CLI/report semantics and retention in owning READMEs/catalogs and user documentation; update root `EXTENSION_POINTS.md`.
- [ ] T053 Extend exact composed journey/capture inventory in `Studio/tests/acceptance/` and `tools/ux-acceptance/run.py`; execute every `ux-acceptance.md` journey/state against current committed owned hosts with no unintended skip/retry.
- [ ] T054 Retain downloadable current-head screenshots/traces/accessibility/JSON/JUnit/package controls and obtain independent visual inspection with the required UX receipt on the implementation PR.
- [ ] T055 Run affected whole-project suites/listed counts, architecture guard, maps/tier checks, locked restore for reference changes, relevant REST e2e and causal regression controls; retain real failures/not-run states.
- [ ] T056 Recheck claims/open PRs, commit/push/open scoped draft PR in the same session, map both feature acceptance records to evidence, and post gate evidence/program transitions on existing records.
- [ ] T057 Complete independent correctness/standards/acceptance review loop, fixes/current-head retests, ready review and green required exact-head checks under current org rules; preserve human holds/review cap.
- [ ] T058 Merge through permitted protocol and qualify resulting main with rebuilt composed Studio/headless/integration/control journeys; record exact source/CI/evidence on #3588/#3892.
- [ ] T059 Only after every agreed criterion passes, set spec `Implemented`, complete program registry/bucket and canonical issue closeout in the delivery change; never mark either feature delivered from planning/manual demo/partial milestone/unmerged PR.

## Dependencies and independent acceptance

Numeric order is the backend critical path. T001–005 close artifact readiness before production. T006–010 prove the execution/context/timer boundary before library growth. Backend foundation tasks own reusable contracts, source/result stores, target preparation, closed effect policy, serial steps/assertions/settlement, lifecycle/fixtures, library resolution/import, controlled time and module adapters. These are prerequisites for safe public execution, not optional later polish.

After the executed T006 primitive proof, the independent domain declaration and portable Core model portion of T011–013 may proceed alongside T007–010; they do not implement a library, advertise runtime support or activate public execution. Store/orchestrator integration still waits for the execution/context/timer witnesses. This keeps the stable cross-client data contract moving without assuming unproved runtime behavior.

Normal host composition (T051) depends on the common API, durable provider, source/result stores and every advertised profile; it precedes actual composed acceptance (T038, T040, T045, T048, T050, T053). Those story tests may be written earlier but are checked complete only after that activation and their complete dependencies execute. Earlier focused backend tests can use owned hosts with explicitly qualified pure profiles. No ordinary host advertises an unfinished public runner.

US1 proves selected native leaf/composite/nested/reusable manual setup plus saved ordered actions/assertions; final replay depends on shared steps/assertions/library/action foundations. US2 proves full-workflow events/actions/controlled time and expected-fault/completed-but-failed controls. US3 proves durable save/reload/import/export and packaged same-revision parity; T042/T043 may run in parallel after report/API contracts stabilize. US4 proves closed controlled effects, model availability, real owned integration and module absence. US5 proves two-scope/ordinary-ingress isolation, cancellation/expiry/interruption and fixture cleanup. Every story uses the independent acceptance criteria in spec.md and ux-acceptance.md.

## Incremental strategy

First execute the original full-artifact selected-leaf witness with explicit scope/prior-output setup, normal capture/completion and zero unrelated invocations. Expand to composite/nested/reusable and scoped Delay. Then implement durable library and safety foundations, followed by manual-to-saved automatic steps, full-workflow business checks, Studio/CLI parity and integrated lifetime controls. One owner controls shared Runtime/Core and persistence registrations; parallel agents own disjoint files. Probe failures revise the reviewed plan rather than being relabeled acceptance. The combined request remains open until every final gate passes.
