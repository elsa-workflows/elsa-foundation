# Tasks: Business decisions

**Input**: [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [contracts](contracts/) and [qualification](quickstart.md).
**State**: Product implementation and local composed qualification completed; final review, CI and resulting-main gates remain. Checked tasks identify the bounded evidence in `evidence/implementation-checkpoint.md`, not an issue-level acceptance claim. Domain admission was approved by Sipke on 5 November 2026; delivery continues autonomously under that decision.
**Cadence**: Risk-based behavior tests under ADR 0080 D1 and framework §2.23. No mandatory test-first cadence is inferred. Required causal red/green controls prove meaningful new safeguards. One issue/branch/draft PR; no new tracking issues or program.

## Phase 1 — Admission and setup

- [x] T001 Record the owner's explicit Decisions-domain decision on #3602 and in `specs/1195-business-decisions/plan.md`; if approved, add a numbered `docs/adr/` decision, admit the narrow purpose in `.specify/memory/constitution.md` with minor bump/sync-impact report and `docs/reference/elsa-domain-tree.md`, preserving ADR 0088 and framework v6.1.0.
- [x] T002 Review final specification, plan and this task list independently for correctness, standards and #3602 conformance; record outcomes in `specs/1195-business-decisions/evidence/design-review.md` before product implementation.
- [x] T003 Create only the initial Core/evaluator/test projects under `Extensions/Decisions/src/` and `Extensions/Decisions/tests/Evaluation/Tests/`, with public-API tracking, computed package versions and the appropriate entries in `Elsa.Server.slnx` / `tools/ci/test-tiers.json`. Add later projects with their slices.

## Phase 2 — Foundation and US2: pure evaluation

**Independent outcome**: An ordinary caller compiles/evaluates the supported approval table with exact typed results and actual rule explanation; no store/host is required.

- [x] T004 [US2] Add immutable artifact/value/profile/diagnostic/result/trace contracts and explicitly replaceable compiler/evaluator ports in `Extensions/Decisions/src/Core/`; keep parsing, inference policy and host dependencies out of Core.
- [x] T005 [US2] Implement secure bounded XML admission and canonical executable hashing in `Extensions/Decisions/src/Elsa.Decisions/`, preserving source/executable identities and refusing unsupported elements/attributes/languages/extensions before compilation.
- [x] T006 [US2] Implement the closed literal/unary-test parser, reserved-name validation, FEEL escapes, exact coefficient/scale numeric comparison and three-valued matching in `Extensions/Decisions/src/Elsa.Decisions/` per `contracts/dmn-profile.md`; no CLR/functions/scripts or binary-float conversion.
- [x] T007 [US2] Implement five hit policies, typed scalar/record/list result shape, actual matched/selected/cell trace, no-match/null distinction, operation limits and cancellation in `Extensions/Decisions/src/Elsa.Decisions/`.
- [x] T008 [US2] Add scoped feature registration and duplicate/unsupported contract diagnostics in `Extensions/Decisions/src/Elsa.Decisions/`; document registrations and extension ports in its `README.md` / `EXTENSION_POINTS.md` and root `EXTENSION_POINTS.md`.
- [x] T009 [US2] Add complete risk-based parser/evaluator/registration suites in `Extensions/Decisions/tests/Evaluation/Tests/`, including all hit policies, nullable truth tables, strings/escapes/reserved names, exact 34-digit values, limits, cancellation, unsupported dormant cells and genuine explanations. Record causal guard/matching/selection red-green controls in `specs/1195-business-decisions/evidence/`.
- [x] T010 [US2] Pack actual computed-version Core/evaluator packages and run an isolated PackageReference consumer; retain dependency envelope and direct approval-matrix evidence under `specs/1195-business-decisions/evidence/`. Source references alone do not complete this task.

## Phase 3 — US1: reusable assets and publication

**Independent outcome**: Protected HTTP clients create/save/validate/publish/read immutable decisions with explicit conflict and tenant behavior.

- [x] T011 [US1] Add asset/draft/version models and CQS read/write contracts in `Extensions/Decisions/src/Persistence/Core/`, implementing `data-model.md` invariants and operation identity without a pending-execution ledger.
- [x] T012 [US1] Implement Decisions-owned EF records/mappings/queries and authoritative CAS/atomic publication/idempotency in `Extensions/Decisions/src/Persistence/EntityFrameworkCore/`; use shared persistence policy, exact EF closure admissions and consumer-owned context support.
- [x] T013 [US1] Implement validate/publish orchestration in `Extensions/Decisions/src/Design/`, freezing expected draft content and refusing raced revisions or unsupported semantics; queries remain separate from persistence mutations.
- [x] T014 [US1] Add authenticated capability-discovered authoring/version/clone-to-draft/import/export APIs and permission contributions in `Extensions/Decisions/src/Api/`, using trusted tenant/principal and safe problem translation. Implement exact-published-version `decisions.evaluate` with Evaluate authorization, protected-output checks and structured outcomes; add HTTP/direct parity and denial tests.
- [x] T015 [US1] Add real-provider concurrent save/publication/replay and tenant isolation tests under `Extensions/Decisions/tests/`; prove fresh installs and preserve existing provider migration histories. Record each provider actually exercised; qualify only that support. Actual behavior: SQLite; four-engine migration SQL generation is recorded separately.

## Phase 4 — US3: saved cases and completed receipts

**Independent outcome**: Saved cases detect a deliberate threshold regression against an exact candidate, with safe history and retry behavior.

- [x] T016 [US3] Add revision-checked case and completed-receipt CQS models/store operations in `Extensions/Decisions/src/Persistence/Core/` and `Extensions/Decisions/src/Persistence/EntityFrameworkCore/`, with request fingerprints and optional raw-history policy.
- [x] T017 [US3] Implement bounded synchronous exact-candidate/case snapshot evaluation and typed comparison in `Extensions/Decisions/src/Design/`. Look up completed receipts before mutable content; a precommit interruption has no durable test job; changed retry revisions conflict.
- [x] T018 [US3] Expose protected cases/test/history/delete-run capabilities in `Extensions/Decisions/src/Api/`, with exact decimal-text tagged values and summary-only replay when raw history was not retained.
- [x] T019 [US3] Add whole affected case/comparison/replay/retention tests under `Extensions/Decisions/tests/`, including concurrent equivalent/different operation keys, response loss after receipt, interruption before receipt, case mutation/deletion, deliberate regression and >2^53/34-digit values.

## Phase 5 — US4: DMN interchange

**Independent outcome**: Supported tables preserve meaning/configuration through both external-tool round-trip directions, and unsupported content remains explicit.

- [x] T020 [US4] Implement original-XML retention and targeted supported mutations in `Extensions/Decisions/src/Design/`, preserving covered IDs/namespaces/diagrams/extensions and refusing edits that cannot preserve content safely; classify view/edit/execute/publish separately.
- [x] T021 [US4] Add a pinned external-editor and reference-evaluator fixture under `specs/1195-business-decisions/evidence/`, with dependency/license notices and a finite supported/unsupported differential matrix. Record normative expectations and actual tool disagreements rather than treating one engine as the standard.
- [x] T022 [US4] Run both round-trip directions with actual editing/reimport and evaluator checks; prove numeric/string/null/policy/result behavior and covered opaque/diagram preservation. Retain bounded raw evidence and exact tool versions in `specs/1195-business-decisions/evidence/`; no full-DMN claim.

## Phase 6 — US1/US3/US4: Studio authoring

**Independent outcome**: The normal composed Studio performs create/edit/validate/case-test/publish/reload/import/export, using one accessible editor.

- [x] T023 [US1] Add optional `Extensions/Decisions/studio/Elsa.Studio.Decisions/` SDK module with feature area/resource routes, backend capability requirements, authenticated HTTP and deferred editor loading; use public Studio UI primitives/tokens.
- [x] T024 [US1] Implement named-asset list and accessible profile-limited table editor in `Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/`, including column/rule operations, labeled native controls, dirty-navigation guard, immutable-version display, explicit clone-to-draft and conflict resolution.
- [x] T025 [US3] Implement exact-candidate input/case editing, tagged decimal transport, test comparison and safe history in `Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/`; label results for a changed draft accurately.
- [x] T026 [US4] Implement import compatibility report, safe export and located validation/explanation views in `Extensions/Decisions/studio/Elsa.Studio.Decisions/Client/`; viewability never claims execution readiness.
- [x] T027 [US1] Add meaningful mounted-component/browser tests for identity changes, stale requests, conflicts, keyboard cells/errors, 320px scrolling, light/dark and permission/unavailability in the Studio module's client tests; no assertions on source text.
- [x] T028 [US1] Rebuild owned actual Workbench/Studio and exercise complete author/test/regress/repair/publish/reload/clone-to-draft/export journeys with real identity/storage. Inspect screenshots and retain exact product/harness identities under `specs/1195-business-decisions/evidence/`.

## Phase 7 — US5: normal native/BPMN reuse

**Independent outcome**: The same frozen version/inputs yield equivalent direct/native/BPMN results and explanations; restart/retry cannot select edited decisions.

- [x] T029 [US5] Implement `IExecutableCompilationSource` contribution in `Extensions/Decisions/src/Workflows/Design/`, selecting specific/latest-at-workflow-publication once and embedding immutable artifact/profile/semantics/protection metadata; reject invalid/unavailable/unauthorized publication.
- [x] T030 [US5] Implement ordinary `EvaluateDecision` activity in `Extensions/Decisions/src/Workflows/Runtime/` using frozen material and Core evaluator. Reuse the existing execution authority/checkpoint/input/retry lifecycle; no Design-store reference or new execution coordinator.
- [x] T031 [US5] Contribute normal decision-version selection/property authoring and result metadata through the SDK/owning activity contracts in the Decisions integration modules. Bind the activity under ordinary native/BPMN tasks; preserve existing gateway/interchange semantics and explicitly refuse unsupported vendor decisionRef binding.
- [x] T032 [US5] Add whole publication/runtime/native/BPMN suites and actual owned-host restart witnesses under `Extensions/Decisions/tests/` and `specs/1195-business-decisions/evidence/`: newer decision published while waiting, original content after restart, missing/corrupt material refusal, and runtime-only recovery with Design unavailable.

## Phase 8 — US6: authority and sensitive values

**Dependency rule**: Server-side tenant/permission protection is built with each earlier slice. This phase qualifies the complete cross-surface matrix; it is not permission to defer enforcement.

- [x] T033 [US6] Implement Decisions-owned operation/source/sensitive grants and asset policy enforcement through `Extensions/Decisions/src/Design/` / `Extensions/Decisions/src/Api/`, with no raw values in routine diagnostics, traces or default history.
- [x] T034 [US6] Bind current workflow execution subjects through supported host policy and `ICurrentSubjectPermissionAuthorizer` in `Extensions/Decisions/src/Workflows/Runtime/`; use trusted authority and current grants, never audit metadata/browser principal/allow-all fallback.
- [x] T035 [US6] Carry sensitivity through existing result/projection/ValueEnvelope policies and Decisions-specific resource-denial contributions in the integration modules; protect embedded source/executable reads and outputs/evidence. Refuse combinations whose protection cannot be preserved.
- [x] T036 [US6] Execute full two-tenant/API/native/BPMN permission and revocation matrix, including workflow-only grants, protected outputs/source, service/unsupported identities, test/export/error/history leakage and value propagation. Add meaningful tests under `Extensions/Decisions/tests/` and actual-host evidence under `specs/1195-business-decisions/evidence/`.

## Phase 9 — Integrated delivery

- [x] T037 Complete module/API/hosting/profile documentation and all domain/cross-domain catalogs in `Extensions/Decisions/` and root `EXTENSION_POINTS.md`; reconcile `specs/1195-business-decisions/quickstart.md` with runnable commands and actual acceptance receipts. Do not duplicate glossary meanings or commit generated maps.
- [x] T038 Run whole affected suites with discovered/executed counts, locked solution restore after reference changes, architecture guards, generated maps and tier checks. Record actual commands/counts/causal controls and all evidence limits on the single draft PR and under `specs/1195-business-decisions/evidence/`.
- [ ] T039 Independently review the exact final diff for correctness, standards and acceptance; resolve findings in normal commits, rerun affected checks, post PR evidence, mark ready and obtain green exact-head required CI. Follow current org review/merge authorization and record the result on #3602.
- [ ] T040 Set `specs/1195-business-decisions/spec.md` to Implemented in the completing PR; after authorized merge verify resulting-main applicable CI/Studio/security and all acceptance rows before closing #3602/releasing the exact claim. Do not label research or draft status as product completion.

## Dependencies and execution strategy

Admission/review T001–T002 precede product setup. Pure evaluator T003–T010 is the first useful slice. Asset lifecycle T011–T015 precedes cases T016–T019 and normal Studio T023–T028; interchange T020–T022 supplies the editor's preservation/validation paths. Native/BPMN T029–T032 consumes the proven evaluator/publication lifecycle. Security T033–T035 is implemented alongside its consuming slice, then T036 verifies the complete matrix. T037–T040 close the whole issue.

No issue-level acceptance is dropped at an MVP checkpoint. The pure evaluator is a demonstrable intermediate, while end-to-end #3602 remains open until Studio, interchange, callers, security and qualification all pass.

## Parallel opportunities

Independent conformance fixtures and parser tests may be prepared on separate files after the profile is frozen; module UI tests and API provider fixtures may proceed after their respective contracts stabilize. Keep shared authority/publication paths serial. Reviews run independently per lens. The owning session retains integration responsibility and follows repository WIP/claim rules; this list does not dispatch other GitHub workers or create new issue lanes.
