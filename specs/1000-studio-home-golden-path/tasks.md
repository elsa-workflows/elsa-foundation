# Tasks: Direct Home authoring paths

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [UI contract](contracts/home-navigation.md).

One issue/PR: #3063. Root owns scope/integration/review/QA; one isolated implementation writer. This is brownfield behavior work with focused regressions and required red→green evidence, not a new greenfield test-cadence choice. Reuse fixtures and run builds serially through existing slots.

## Phase 1: Setup

- [x] T001 Refresh issue ownership/prerequisites/base, allocate1000 and review the feature specification/design in specs/1000-studio-home-golden-path/; record isolated overlap and the retained Runs main-qualification merge gate on #3063/#3057/Project61.
- [ ] T002 Verify existing workspace/ignore/public-module boundaries in Studio/package.json, .gitignore, Studio/eslint.config.js and Studio/src/apps/Elsa.Studio.Web/Client/src/app/ui/shared.ts; add no dependency or unrelated ignore boilerplate.

## Phase 2: User Story 1 — Start from Home

Independent test: Home Create enters existing creation once, preserves catalog/readiness/errors, and does not replay through Cancel/history; ordinary Browse remains.

- [ ] T003 [US1] Add observable real-route one-shot/conflicting-intent/history regression coverage in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/module.test.tsx or a focused sibling sharing its fixtures; retain baseline RED where practical.
- [ ] T004 [US1] Implement acknowledged one-shot create intent in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/pages.tsx and WorkflowDefinitions.tsx, preserving unrelated location state, an already selected definition, catalog/readiness and successful exact-ID creation.
- [ ] T005 [US1] Add direct Create and Browse navigation through a focused task component integrated with Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/HomePage.tsx, preserving guarded internal navigation and native modified behavior for links.
- [ ] T006 [US1] Verify catalog unavailable/retry, known permission denial, Cancel and successful-create navigation through focused Dashboard/Workflows tests under their Client/src/__tests__/ directories; do not replace server authority or duplicate create APIs.

## Phase 3: User Story 2 — Continue the intended current draft

Independent test: zero/one/many/beyond-eight/duplicate-name candidates, failed/denied discovery, stale/late selection and authority change never silently pick/create a different draft.

- [ ] T007 [US2] Preserve actual draftId and the complete authorized list contract in Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/widgets/homeData.ts; extend widgets/__tests__/homeData.test.ts for published-with-draft versus published-only and uniqueness before any preview window.
- [ ] T008 [US2] Implement scoped discovery/chooser/access affordances alongside Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/HomePage.tsx using its existing useHomeDialogFocus and module-safe UI; cancel/invalidate late requests and preserve server wildcard/subtree/manage→read and anonymous posture without a global SDK rewrite.
- [ ] T009 [US2] Add explicit exact-definition failed/no-current-draft recovery and isolate rapid definition changes at the narrow existing boundary in Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/pages.tsx / WorkflowEditor.tsx / useWorkflowEditorData.ts only as needed; preserve pending saves and lifecycle behavior.
- [ ] T010 [US2] Cover candidate and editor identity/recovery/access races in Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/__tests__/ and Studio/src/essentials/Elsa.Studio.Workflows/Client/src/__tests__/ using actual observable selection/request behavior, including denied grants, broader allowed grants, duplicate names and a ninth candidate outside the recent window.

## Phase 4: User Story 3 — Durable everyday task hierarchy

Independent test: the group precedes configurable widgets on both laptop sizes; keyboard and narrow/zoom work; feature-off is honest; Needs attention uses accepted Runs health without altering layout.

- [ ] T011 [US3] Complete compact token-based task-group layout and feature gating in Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/HomePage.tsx, its focused task component and styles.css; link Needs attention to the Runs-owned active-health route and preserve every widget preference/content.
- [ ] T012 [US3] Extend Studio/src/essentials/Elsa.Studio.Dashboard/Client/src/__tests__/homeLanding.test.tsx or a focused sibling for reordered/all-hidden/onboarding layouts, feature-off, unchanged preference writes, loading/empty/error and accessible keyboard/focus behavior.
- [ ] T013 [US3] Extend Studio/tests/browser/home.spec.ts / homeFixture.tsx and a focused real Workflows navigation fixture if required; cover both laptop themes, narrow/zoom, chooser focus/Escape, exact selection, one-shot history and serious/critical axe checks on changed compositions.

## Phase 5: Polish and implementation handoff

- [ ] T014 Update only relevant Home entries in Studio/docs/ux/design-spec.md and Studio/docs/ux-polish-plan.md, describing actual delivered behavior and retained domain exceptions; review changed source/tests for repetition and remove only obsolete in-scope paths with evidence.
- [ ] T015 Run complete affected Dashboard/Workflows suites and typechecks, Studio lint and affected client builds from Studio/package.json; compare listed/executed counts, retain failures/skips, and record focused behavior RED→GREEN evidence outside the repo for the PR.
- [ ] T016 Root reviews the integrated diff, local evidence and delegated report; finalize specs/1000-studio-home-golden-path/spec.md terminal status/process-residue cleanup and the issue-acceptance mapping for the coherent implementation candidate. Keep exact-head review/CI/normal-host/resulting-main evidence on #3063/the PR rather than inventing passed receipts in source.

## Dependencies and execution

T001→T002 precedes code. US1 and US2 are logically separable but share Home/Workflows files, so the single writer serializes them; no parallel writers to these paths. US3 integrates both. T014→T015→T016 follows the stories. Bounded independent read-only root/reviewer checks may run beside the writer. No separate foundational infrastructure is needed.

MVP is direct Create/Browse (US1); complete this whole issue before delivery. Do not begin3067/3069/3070/3071 from this task list.

## Mandatory integration gates outside the implementation checklist

Root retains all acceptance obligations: three independent exact-head review axes and any fixes/re-review, whole affected checks, architecture restore/test + listed/executed reconciliation, generated solution-filter check, rebuilt normal-host UI/asset/cleanup proof, relevant backend workflow-flow, canonical PR checks and standing self-merge audit. Runs resulting-main qualification must pass before Home merge/acceptance. Any failed required base gate stops implementation until reconciled. Resulting-main qualification and claim release are recorded publicly after merge; the committed task file is a source checkpoint, not a claim that future CI already ran. Final3075's nine composed rows are separate and remain unrun here.
