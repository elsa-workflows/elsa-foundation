# Tasks: User Tasks

Input: [spec](spec.md), [plan](plan.md), [research](research.md), [data model](data-model.md), [API](contracts/api.md), [qualification](quickstart.md). Program #3688; acceptance #3577. Risk-based test-after with causal controls is selected for this work; the derived constitution declares no universal greenfield cadence. This is delivery work, not a repository policy amendment.

## Setup and reviewed design (M0–M1)

- [x] T001 Establish Program #3688, claim #3577 and record owner authority/registry in `docs/program-goals/user-tasks.md` and ADR 0080.
- [x] T002 Research current native/BPMN/runtime/identity/Studio and primary comparative sources in `research.md`.
- [x] T003 Write/review user scenarios and quality checklist in `spec.md`; remove the completed checklist at implementation per the spec lifecycle rule.
- [x] T004 Review architecture/data/contracts and finite qualification matrix in this directory; resolve concrete findings before implementation.

## Durable task foundation and decision journey — US1/US2 (M2)

- [x] T005 Add task/form/action/response/history/query models and journal/lifecycle/provider contracts in `Essentials/src/Workflows/Runtime/Core/UserTasks/`; track public API and catalogs.
- [x] T006 Implement validated state transitions, conditional memory journal, safe frozen snapshots and basic form rules in `Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/UserTasks/`.
- [x] T007 Implement conditional durable journal/form versions, exact scope/identity validation, bounded authorized paging and fair recovery in existing `Runtime/Persistence/EntityFrameworkCore/`; wire aggregate provider selection, atomic execution-deletion retention fence and four-provider migrations.
- [x] T008 Add whole-project lifecycle/persistence tests for competing claims/completions/reassignments, stale writes, terminal protection, immutable history, scope/tenant/casing and restart readback in existing Runtime test projects; run causal controls.

## Durable workflow integration — US1/US2 (M3)

- [x] T009 Implement typed `CreateUserTask` under `Essentials/src/Activities/Primitives/`, literal dynamic action outcomes, frozen form/prefill and deterministic invocation staging.
- [x] T010 Implement staged-wait reconciliation, captured physical wait recipient/generation and provider-enforced exact dispatch condition, exact response pre-consumption guard, stable idempotent dispatch, canonical committed-result acknowledgement and cancellation/retirement proof in Runtime `UserTasks/` with fresh partition scopes.
- [x] T011 Prove crash/restart boundaries, forged stimuli, duplicate/conflicting responses, exact-wait replacement, no false completion on queue receipts, missing-state uncertainty, completion-before-ack then deletion, and cancellation races in runtime/activity integration tests.
- [x] T012 Qualify the published native User Task worker through existing `BpmnNativeServiceBinding`; verify action/response and terminal child behavior without new BPMN semantics.

## Authorized API and reusable forms — US3/US4 (M3)

- [x] T013 Add normalized current-identity/permission/eligibility adapter, current-target validation and task/form permission contributions under `Runtime/Api/`.
- [x] T014 Add task inbox/detail/claim/release/complete/reassign/form-version/assignment-target endpoints, capability relations, revision/request receipts and safe errors under `Runtime/Api/`.
- [x] T015 Add composed HTTP identity/tenant/role/revocation/forgery negatives, paging and stale/conflicting mutation tests in existing Runtime API suites.
- [x] T016 Supply an external form-provider/rendering witness and custom-client completion fixture, with missing/duplicate-provider refusal tests and immutable-version behavior.

## Studio authoring and inbox — US1/US3/US4 (M4)

- [x] T017 Add optional `Extensions/UserTasks/studio/Elsa.Studio.UserTasks` module and normal Studio host/solution/workspace composition; publish capability/permission-aware navigation/routes.
- [x] T018 Implement My tasks/Group queue/history, detail/form/action/reassignment UI, errors/conflicts/pending/terminal states using shared UI and tokens.
- [x] T019 Implement reusable basic form field authoring/version selection and assignment/action property editors through SDK contributions.
- [x] T020 Extend generic dynamic outcome facet consumption in Studio `workflowAdapter.ts` with authored literal collection parity/port edit tests.
- [x] T021 Add mounted/browser behavior tests for inbox/actions/forms, stale responses, pending/conflicts, current permissions, keyboard and narrow layouts; run module typecheck/test/build and affected Studio suites with causal controls.

## Integrated qualification, guidance and delivery (M5–M6)

- [x] T022 Run every `quickstart.md` row on owned composed hosts/stores with real identities; preserve user hosts/stores and report failures/unexecuted evidence honestly.
- [x] T023 Qualify provider/custom-host/package composition and complete required e2e suites; add user/integrator guides and extension-point/feature/cadence docs.
- [x] T024 Check claims/open PRs; review diff, complete whole affected suite/list counts, architecture/maps/tier/reference guards and post evidence on draft PR.
- [x] T025 Complete independent draft review axes, fix findings in new commits, mark ready and run required reviewed-head CI/review/permitted merge.
- [x] T026 Rebuild resulting main, repeat decisive integrated rows, reconcile spec status/#3577/Program/linked parity records, and close only when all exits are evidenced.

T026 closeout is applied by PR #3706 only after executed resulting-main qualification, healthy-main inspection and the closeout PR's own required checks pass; see [resulting-main qualification](resulting-main-qualification.md). The Program issue retains the live state until merge.

## Dependencies and implementation strategy

T001–T004 → T005–T008 → T009–T012 → T013–T016 → T017–T021 → T022–T026. US1 provides the initial decision journey; US2 proves durability before UI acceptance; US3/US4 add reusable forms/custom clients and management. All stories are required for #3577 closure. Independent test/code reading can run in parallel; shared source changes, migrations, host mutation and evidence reviews remain sequential. Within US1 activity authoring and isolated lifecycle tests can run independently after contracts; within US3 form validator tests and API identity negatives can run independently; within US4 UI state tests and narrow-layout checks can run independently. One implementation owner/leaf; no competing edits to the same paths.
