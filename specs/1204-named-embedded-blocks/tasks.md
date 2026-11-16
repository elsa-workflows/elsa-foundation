# Tasks: Named embedded blocks

**Program:** [#3610](https://github.com/elsa-workflows/elsa-foundation/issues/3610).
**Board:** [Project 67](https://github.com/orgs/elsa-workflows/projects/67).
**Status:** Establishment, S001 and T001–T006 merged with resulting-main acceptance. T005/#3754: PR #4649 / main `bb0b859bc133f327299ca94fa3d2c5834f4c201d`, [acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/4649#issuecomment-6920297143). T006/#3755: PR #4686 / main `f2405a42575440108c6e321ce349327cdc0b94ee`, [acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/4686#issuecomment-6923603664). T007 (program exit) is implemented by PR [#4697](https://github.com/elsa-workflows/elsa-foundation/pull/4697); it is checked, and Feature #3746 and Program #3610 close, only after that PR's resulting-main acceptance.

Native sub-issues are durable work units; these checklists are the canonical task/acceptance map. One implementation leaf is active at a time. Evidence belongs to that leaf/PR, not new checkpoint issues. A checked task requires its actual acceptance; source preparation is insufficient.

T003 selection/history follow-through [#4604](https://github.com/elsa-workflows/elsa-foundation/pull/4604) landed with [#4630](https://github.com/elsa-workflows/elsa-foundation/pull/4630) in main `c32bca382dfc9a5ed7bc33fbfd825f052fb24e83`. The [current-tree eight-case repair acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/4630#issuecomment-6905374188) covers the two bounded repair contracts. Phone toolbar overlap UX-OBS-1 passed the bounded T004 phone inspection; the broader matrix is T007's (PR #4697). Neither is a GitHub CI pass.

Owner triage on 9 November folded duplicate T004/#3753 into Feature #3745 and T007/#3756 into Program #3610. Their closure as not planned removes duplicate tracking, not the original acceptance obligations. The T004 and T007 identifiers and serial dependencies below remain in force.

## Phase 1 — Establishment and prototype

**Goal:** prove the selected native composition and exact delivery contract before production completion.
**Independent test:** actual composed-host extract/edit/reload/history/run proof with paired data/control baselines.

- [x] T000 [#3748](https://github.com/elsa-workflows/elsa-foundation/issues/3748) Persist `spec.md`, `plan.md`, research/data boundaries/journeys and this task map; record ADR 0080 activation, registry/program links, native hierarchy and verified Kanban configuration. Delivered by PR #3760 / main `12aa9e263`; reviewed files match and resulting-main documentation guards pass. No product result claimed.
- [x] S001 [#3749](https://github.com/elsa-workflows/elsa-foundation/issues/3749) Build and exercise a bounded UX/execution prototype through the existing graph-authoring area and owned Studio/Workbench composition. Prove parent variables, crossing result references, repeat/loop occurrences, wait/resume, cancellation and supported recovery. Record any completion-causality defect and revise supporting contracts before production delivery. Depends on T000. Local proof: [receipts](evidence/prototype/README.md); supporting runtime/history fixes delivered by PR #3767 / main `40a2cd585`, with [resulting-main acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/3767#issuecomment-6623300307). The temporary UI experiment was removed; production commands remain T002–T006.

## Phase 2 — Shared prerequisites and User Story 1

**Goal:** equivalent named native block authoring on both resources.
**Independent test:** contiguous Sequence and linear Flowchart extraction, nested editing, persistence and atomic Undo/Redo/Ungroup preserve expected execution and visible data.

- [x] T001 [#3750](https://github.com/elsa-workflows/elsa-foundation/issues/3750) Extend typed node-presentation contracts/persistence/source freezing in existing Activities Design/Graph ownership and Hosting SDK; include complete lifecycle, default-empty compatibility, historical/hash and editor-history regressions. Depends on S001. Delivered by PR #3798 / main `9daaa3206`, with [resulting-main acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/3798#issuecomment-6641358323). Task #3750 and Feature #3743 are complete; later tasks retain their separate acceptance receipts.
- [x] T002 [#3751](https://github.com/elsa-workflows/elsa-foundation/issues/3751) Add pure native creation/extraction/Ungroup plans in Studio Workflows graph-authoring with identity/layout/reference preservation and pre-mutation refusal diagnostics. Cover order, entry/exit/outcomes, control targets, child results and no-loss inverse cases. Depends on S001; executed after T001 under the serial WIP policy. Delivered by PR #3837 / main `e7d8a5d50530d21b3476248af1f2d5835ab7bf32`, with [resulting-main acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/3837#issuecomment-6881224741).
- [x] T003 [#3752](https://github.com/elsa-workflows/elsa-foundation/issues/3752) Integrate shared block actions and preview into Workflow and Activity Definition graph hosts; add Sequence range selection, summary/name, breadcrumbs, keyboard/focus, dirty state and atomic history; honor source/read-only/module/permission restrictions. Depends on T001 and T002. Delivered by PR #4561 / main `90beef1af3357c87011609bdbbae19e0dc35f546`, with the resulting-main receipt linked above; Feature #3744 is complete. The full cross-cutting authority/module/history matrix remains T007.

## Phase 3 — User Story 2

**Goal:** real BPMN subprocess extraction with preserved supported binding/interchange semantics.
**Independent test:** same-lane native task extraction, nested edit/save/run and separately audited XML/DI/binding round trip; unsafe selections/exports refuse without silent degradation.

- [x] T004 [Feature #3745](https://github.com/elsa-workflows/elsa-foundation/issues/3745) Implement the bounded BPMN transformation in shared Studio authoring and required native binding-retention/refusal support in existing BPMN interchange; preserve elements/flows/child nodes and add plain start/end events. Run native and interchange regressions plus composed evidence. Depends on T003. Delivered by PR #4612 / main `4fd435eb3116d6542a8aa7a76415ebe46053480f`, with the resulting-main acceptance linked above.

## Phase 4 — User Story 3

**Goal:** reviewable ordinary reusable promotion without source loss.
**Independent test:** review/create normal draft, validate/publish, resume/reanalyze/replace exact version, execute two independently bound calls and Undo replacement.

- [x] T005 [#3754](https://github.com/elsa-workflows/elsa-foundation/issues/3754) Add internal shared promotion analysis/rewrite over supported structured references/explicit parameters; propose graph/public contract/call-site mappings and diagnose opaque, external-write, timing and control-target cases. Validate candidate through existing graph authoring. Depends on T004. Delivered by PR #4649 / main `bb0b859bc133f327299ca94fa3d2c5834f4c201d`, with the resulting-main acceptance linked above.
- [x] T006 [#3755](https://github.com/elsa-workflows/elsa-foundation/issues/3755) Integrate review → draft → explicit ordinary publication → exact-version replacement with source/target freshness checks and one source Undo operation. Prove failure/cancellation preserves source and Undo retains shared reusable resources. Depends on T005. Delivered by PR #4686 / main `f2405a42575440108c6e321ce349327cdc0b94ee`, with the resulting-main acceptance linked above.

## Phase 5 — Cross-cutting qualification and delivery

- [ ] T007 [Program #3610](https://github.com/elsa-workflows/elsa-foundation/issues/3610) Complete `quickstart.md` journeys on rebuilt composed hosts, both resource editors and supported structures; add meaningful regressions/bite proofs and safe receipts. Cover themes, narrow/keyboard, unavailable modules, permissions/source authority, exact historical names/material and two-call-site isolation. Complete affected whole suites, source e2e, architecture/maps, exact-head review/CI and resulting-main proof. Update delivered documentation/glossary and spec lifecycle in the final implementation PR; close parents only with mapped evidence. Depends on T006. Implemented by PR [#4697](https://github.com/elsa-workflows/elsa-foundation/pull/4697): native lifecycle proof (wait across restart, cancel, nearest-loop Break inside a block), promotion refusal/keyboard-activation/resume journeys, user guide and glossary. Known limits without a composed-host journey are tracked in [#4700](https://github.com/elsa-workflows/elsa-foundation/issues/4700): unavailable optional modules, denied principals, source-owned documents, a historical running version, promotion stale-source/target refusals and a composed parent-variable read. Their unit or native evidence is listed there.

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

Current native ownership is Program #3610 → Feature #3744 → T002/#3751 and T003/#3752; Program #3610 → Feature #3745 carrying T004; and Program #3610 → Feature #3746 → T005/#3754 and T006/#3755. T007 is the Program exit qualification. The original Epic/Feature decomposition remains historical in the [delivery plan](plan.md#durable-delivery-breakdown); owner-folded #3739/#3740/#3741/#3747 and duplicate #3753/#3756 closures do not prove acceptance. Only M3 acceptance closes the Program. A failed prototype blocks the affected production path until the design/proof is corrected; it does not justify quietly reducing owner-approved scope.
