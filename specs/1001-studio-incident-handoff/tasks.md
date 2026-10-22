# Tasks: Incident investigation handoff

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [UI contract](contracts/incident-handoff.md).

One issue/PR: #3067. Root owns scope, integration, review and QA. One isolated implementation writer; no other leaf implementation. Brownfield changed behavior needs focused observable regression and meaningful RED→GREEN/bite-proof, not a new greenfield cadence decision.

## Phase 1: Setup

- [x] T001 Refresh qualified base/dependency, specific claim6329077955, competing PRs, spec1001 allocation and root design review in specs/1001-studio-incident-handoff/; preserve stopped Home1000 and its claim release.
- [x] T002 Verify current workspace/public-module/ignore boundaries in Studio/package.json, Studio/src/essentials/Elsa.Studio.Workflows/Client/package.json and .gitignore; use the frozen lockfile and no new dependency.

## Phase 2: User Story 1 — Understand the selected next step

Independent test: active blocking/nonblocking/history, scheduled/due retry and partial/unavailable evidence explain the current exact target without invented actions.

- [x] T003 [US1] Add an observable existing-component failing next-step regression in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/workflowInstances.test.tsx or a focused sibling using shared fixtures; retain an assertion-level baseline RED before adding implementation imports.
- [x] T004 [US1] Derive/render focused selected-incident/current-run guidance through Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx and an adjacent guidance component/helper only if useful; keep advanced technical details and existing evidence/copy controls.
- [x] T005 [US1] Cover selected-target/run/evidence changes and active/history/RetryScheduled/due/NotLoaded branches in Client/src/__tests__/workflowInstances.test.tsx, compensationJournal.test.ts or focused siblings; reuse current compensationRuntime.ts/journal semantics rather than guessing from lifecycle or missing rows.

## Phase 3: User Story 2 — Use a permitted existing handoff

Independent test: known/unknown/failed capability and denied/read-only/design authority produce an honest handoff; canonical actions retain all guards and confirmations.

- [x] T006 [US2] Add observable availability/authority/pending/canceled tests in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/operatorActionFlow.test.tsx and focused incident tests, including delayed/out-of-order capability results and pending action for another incident.
- [x] T007 [US2] Expose only necessary read-only availability distinctions from Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/useOperatorActions.tsx and integrate honest guidance; preserve canonical action mutation/target/confirmation/cancellation/pending behavior.
- [x] T008 [US2] Add appropriate exact-definition inspection/author/operator handoff and pinned-executable copy in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowInstances.tsx / adjacent guidance component, using current authority semantics without global SDK/backend policy changes.
- [x] T009 [US2] Verify read-only/unknown Design, broad allowed grants/auth-off and explicit denial in Client/src/__tests__/workflowInstances.test.tsx or focused siblings; assert zero unintended run/recovery/design mutations and unchanged canonical action target/consequence/confirmation tests.

## Phase 4: User Story 3 — Preserve investigation context and readability

Independent test: exact activity/input and filtered/paged return work; new guidance stays readable and keyboard accessible in the existing UI.

- [x] T010 [US3] Extend actual incident/Workbench integration tests in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/WorkflowInstanceWorkbenchIntegration.test.tsx or workflowInstances.test.tsx for exact occurrence/input, missing/withheld evidence and unchanged Runs return context; do not equate selection with loaded values.
- [x] T011 [US3] Finish token-based guidance spacing/disclosure/reflow in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/styles.css and the changed incident composition only; retain focus/Escape/trigger return and avoid unrelated styling or UI frameworks.
- [x] T012 [US3] Extend Studio/tests/browser/incident-diagnostics.spec.ts and incidentDiagnosticsFixture.tsx, plus existing compensation fixtures only as needed, for both laptop themes, long names, narrow/zoom, keyboard/modal return and serious/critical axe checks.

## Phase 5: Polish and implementation handoff

- [x] T013 Update relevant delivered examples in Studio/docs/ux/design-spec.md and Studio/docs/workflow-run-incidents.md; proactively review changed source/tests for repetition and remove only obsolete in-scope paths with evidence.
- [x] T014 Run complete affected Workflows tests/typecheck, Studio lint and affected client/library builds from Studio/package.json; compare listed/executed counts and retain all failures/skips plus meaningful behavioral RED→GREEN proof outside the repository.
- [x] T015 Report exact changed files/head under Studio/src/essentials/Elsa.Studio.Workflows/Client/src/, validation commands/results, assumptions, no-mutation/evidence boundaries and unresolved findings in the supplied execution-evidence handoff; run git diff --check on the scoped change and leave no unrelated files staged in the worker checkout.
- [ ] T016 Root reviews the integrated diff/delegated evidence, resolves scope/QA concerns and finalizes specs/1001-studio-incident-handoff/spec.md as Implemented with terminal checklist cleanup and issue-acceptance mapping for the coherent candidate. Future review/CI/runtime proof remains public evidence rather than pre-checked source receipts.

## Dependencies and execution

T001→T002 before code; US1 precedes the dependent explanation/handoff integration; US2/US3 reuse the same files and are serialized by one writer. T013→T014→T015→T016 follows the stories. A bounded independent read-only plan/review can run alongside root or writer; no parallel writers to these paths, and heavy builds/testhosts run serially through current slots.

MVP is the selected-incident explanation (US1); deliver the whole issue before acceptance. Do not start3063/3069/3070/3071 from this task file.

## Mandatory integration gates outside the implementation checklist

Root retains the meaningful final revert/mutation bite-proof, complete affected checks, three independent non-author exact-head review axes and fixes/re-review, architecture restore/test with listed/executed reconciliation, generated solution-filter check, final four client/host rebuilds and source-bound normal-host blocked/read-only/no-mutation/asset/cleanup proof. Inspect screenshots; distinguish fixture-only states and loaded-value retrieval. Canonical PR gates and standing merge audit precede merge; resulting-main qualification/issue/Project acceptance and specific claim release follow it. A failed required base gate stops implementation until reconciled. If canonical action mapping or backend design/publishing/runtime behavior changes, stop and refine required rebuilt-server e2e rather than widening this UI leaf. Final3075's nine composed rows remain separate.
