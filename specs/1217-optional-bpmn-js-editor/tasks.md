# Tasks: Optional bpmn-js editor

**Input**: [spec.md](spec.md), [plan.md](plan.md), canonical #3597.

## M0 — Feasibility before production freeze

- [x] T001 Settle exact claim, verify app identity, record owner scope, isolated branch and acceptance states on #3597.
- [x] T002 Inventory native/direct/nested/reusable authoring, history, bindings, interchange, validation/publication and run inspection.
- [x] T003 Record pinned upstream primary-source/license/API/dependency findings in `research.md` and review specification/plan before prototype code. Independent reviews identified profile/ID/raw-record/attribution requirements, incorporated before implementation.
- [x] T004 [US1] Build identity-preserving projection/merge prototype in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/bpmnJsBridge.ts`; define/admit the experimental profile before import and reject malformed raw records/unsupported transitions/ID collisions. Cover bound Decision expressions, full child/listener/nested inventory, unknown fields, default paths and layout with meaningful mutation controls in `src/__tests__/bpmnJsBridge.test.ts` (38 passed; configuration/outcome/marker mutants fail).
- [x] T005 [US1] Embed optional canvas in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/workflow-editor/WorkflowEditor.tsx`, sharing canonical draft, selection/configuration/history and read-only gates. Keep contract provisional. `bpmnJsAuthoring.ts` reconciles exact baseline/scope and deletion side tables (14 focused cases); `bpmnJsCanvas.test.tsx` covers import/rejection/history transport. Native browser switching/persistence passes in light/dark; real-host execution remains T006.
- [ ] T006 [US1] Demonstrate both directions on owned composed Studio/Workbench using `Studio/tests/acceptance/bpmnJsPrototype.spec.ts` and `tools/ux-acceptance/run-bpmn-js-prototype.py`: save/reload, same-process publication, two-path execution/result/inspection and XML download. Record failures/unsupported/unproved states and asset/attribution evidence.
- [ ] T007 Review go/no-go, maintenance cost and consequential owner choices. Refine design/spec/tasks from actual prototype evidence before production freeze.

## M1 — Production integration after feasible proof

- [ ] T008 Integrate optional module/loading/consumer assets, saved preference and absent/incompatible capability recovery using existing composition.
- [ ] T009 Complete Workflow and Activity Graph/nested surfaces, shared module contributions, coherent history/dirty/recovery, validation/publication/Test Drive and read-only/source-owned behavior.
- [ ] T010 [US3] Implement full-toolbox draft preservation and current-host execution admission in `Studio/src/essentials/Elsa.Studio.Workflows/Client/src/bpmn/`, coordinating #2736 and the existing execution-model owners. Replace temporary prototype edit restrictions with reviewed canonical full-content retention, explicit switch limitations and validation/publication blockers. Document internal and public XML fidelity separately; prove unsupported drafts survive save/reload/switch attempts without silent loss.
- [ ] T011 [US4] Complete theme/narrow/keyboard/focus/accessibility/attribution and actual composed-host network budgets; retain distributed copyright/permission and transitive notices in host and consumer packages. Update existing `Studio/docs/module-ui.md` and appropriate user/maintenance guidance.

## M2 — Review, merge and resulting-main qualification

- [ ] T012 Run affected suites as whole suites, meaningful red/green controls, optional package/consumer integration, architecture/maps and required current-head checks.
- [ ] T013 Capture current-commit composed-host journeys/screenshots/traces with downloadable evidence and obtain independent UX acceptance. Preserve failed/incomplete states.
- [ ] T014 Recheck live claims/open PRs, open one draft PR, post acceptance/gate evidence; run independent correctness/standards/acceptance review, fix/retest and request final review under current org rules.
- [ ] T015 Merge only with applicable gates, qualify resulting main, update spec lifecycle and canonical issue/program bookkeeping. Do not close #3597 at prototype completion.
