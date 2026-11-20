# Tasks: Named embedded blocks

**Program:** [#3610](https://github.com/elsa-workflows/elsa-foundation/issues/3610).
**Board:** [Project 67](https://github.com/orgs/elsa-workflows/projects/67).
**Status:** establishment and S001 delivered. PR #3767 merged as main `40a2cd585`; [resulting-main acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/3767#issuecomment-6623300307) passed the 22 native cases and actual Studio graph/history checks. T001 is in progress; production extraction and promotion remain unimplemented.

Native sub-issues are durable work units; these checklists are the canonical task/acceptance map. One implementation leaf is active at a time. Evidence belongs to that leaf/PR, not new checkpoint issues. A checked task requires its actual acceptance; source preparation is insufficient.

## Phase 1 — Establishment and prototype

**Goal:** prove the selected native composition and exact delivery contract before production completion.
**Independent test:** actual composed-host extract/edit/reload/history/run proof with paired data/control baselines.

- [x] T000 [#3748](https://github.com/elsa-workflows/elsa-foundation/issues/3748) Persist `spec.md`, `plan.md`, research/data boundaries/journeys and this task map; record ADR 0080 activation, registry/program links, native hierarchy and verified Kanban configuration. Delivered by PR #3760 / main `12aa9e263`; reviewed files match and resulting-main documentation guards pass. No product result claimed.
- [x] S001 [#3749](https://github.com/elsa-workflows/elsa-foundation/issues/3749) Build and exercise a bounded UX/execution prototype through the existing graph-authoring area and owned Studio/Workbench composition. Prove parent variables, crossing result references, repeat/loop occurrences, wait/resume, cancellation and supported recovery. Record any completion-causality defect and revise supporting contracts before production delivery. Depends on T000. Local proof: [receipts](evidence/prototype/README.md); supporting runtime/history fixes delivered by PR #3767 / main `40a2cd585`, with [resulting-main acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/3767#issuecomment-6623300307). The temporary UI experiment was removed; production commands remain T002–T006.

## Phase 2 — Shared prerequisites and User Story 1

**Goal:** equivalent named native block authoring on both resources.
**Independent test:** contiguous Sequence and linear Flowchart extraction, nested editing, persistence and atomic Undo/Redo/Ungroup preserve expected execution and visible data.

- [ ] T001 [#3750](https://github.com/elsa-workflows/elsa-foundation/issues/3750) Extend typed node-presentation contracts/persistence/source freezing in existing Activities Design/Graph ownership and Hosting SDK; include complete lifecycle, default-empty compatibility, historical/hash and editor-history regressions. Depends on S001.
- [ ] T002 [#3751](https://github.com/elsa-workflows/elsa-foundation/issues/3751) Add pure native creation/extraction/Ungroup plans in Studio Workflows graph-authoring with identity/layout/reference preservation and pre-mutation refusal diagnostics. Cover order, entry/exit/outcomes, control targets, child results and no-loss inverse cases. Depends on S001; executed after T001 under the serial WIP policy.
- [ ] T003 [#3752](https://github.com/elsa-workflows/elsa-foundation/issues/3752) Integrate shared block actions and preview into Workflow and Activity Definition graph hosts; add Sequence range selection, summary/name, breadcrumbs, keyboard/focus, dirty state and atomic history; honor source/read-only/module/permission restrictions. Depends on T001 and T002.

## Phase 3 — User Story 2

**Goal:** real BPMN subprocess extraction with preserved supported binding/interchange semantics.
**Independent test:** same-lane native task extraction, nested edit/save/run and separately audited XML/DI/binding round trip; unsafe selections/exports refuse without silent degradation.

- [ ] T004 [#3753](https://github.com/elsa-workflows/elsa-foundation/issues/3753) Implement the bounded BPMN transformation in shared Studio authoring and required native binding-retention/refusal support in existing BPMN interchange; preserve elements/flows/child nodes and add plain start/end events. Run native and interchange regressions plus composed evidence. Depends on T003.

## Phase 4 — User Story 3

**Goal:** reviewable ordinary reusable promotion without source loss.
**Independent test:** review/create normal draft, validate/publish, resume/reanalyze/replace exact version, execute two independently bound calls and Undo replacement.

- [ ] T005 [#3754](https://github.com/elsa-workflows/elsa-foundation/issues/3754) Add internal shared promotion analysis/rewrite over supported structured references/explicit parameters; propose graph/public contract/call-site mappings and diagnose opaque, external-write, timing and control-target cases. Validate candidate through existing graph authoring. Depends on T004.
- [ ] T006 [#3755](https://github.com/elsa-workflows/elsa-foundation/issues/3755) Integrate review → draft → explicit ordinary publication → exact-version replacement with source/target freshness checks and one source Undo operation. Prove failure/cancellation preserves source and Undo retains shared reusable resources. Depends on T005.

## Phase 5 — Cross-cutting qualification and delivery

- [ ] T007 [#3756](https://github.com/elsa-workflows/elsa-foundation/issues/3756) Complete `quickstart.md` journeys on rebuilt composed hosts, both resource editors and supported structures; add meaningful regressions/bite proofs and safe receipts. Cover themes, narrow/keyboard, unavailable modules, permissions/source authority, exact historical names/material and two-call-site isolation. Complete affected whole suites, source e2e, architecture/maps, exact-head review/CI and resulting-main proof. Update delivered documentation/glossary and spec lifecycle in the final implementation PR; close parents only with mapped evidence. Depends on T006.

## Acceptance mapping

| Original #3610 success criterion | Spec outcomes | Owning leaves |
|---|---|---|
| Create/name/navigate/persist | SC-001 | T001–T004 |
| Preserve extracted control/data/results; reject unsafe selections | SC-002, SC-004, SC-006 | S001, T002, T004 |
| Coherent history, dirty state, selection and layout | SC-001, SC-006 | T001, T003, T004, T006 |
| Paired execution outputs/outcomes/effects; no implicit policy isolation | SC-002 | S001, T002, T004, T007 |
| Inspect waits/recovery and preserve historical versions | SC-003 | S001, T001, T007 |
| Reviewed reusable contract and correct call-site mappings | SC-005, SC-006 | T005, T006, T007 |
| Equivalent relevant editors, readonly/source authority, composed journeys | SC-001, SC-004, SC-006, SC-007 | T003, T004, T007 |

## Dependencies and execution strategy

T000 → S001 → T001 → T002 → T003 → T004 → T005 → T006 → T007 is the serial delivery order. T001/T002 are technically independent after S001 but are not run as competing implementation leaves. Independent read-only research/review lenses may run concurrently when the applicable skill requires them. A runtime supporting fix discovered by S001 needs a scoped task/PR linked to its existing acceptance, not an unrelated program.

Program #3610 → Epics #3738/#3739/#3740/#3741 → Features #3742–#3747 → leaves above. Only M3 acceptance closes the Program. A failed prototype blocks the affected production path until the design/proof is corrected; it does not justify quietly reducing owner-approved scope.
