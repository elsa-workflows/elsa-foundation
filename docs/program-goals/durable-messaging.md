# Durable Messaging

**Status:** Active; scoped owner authorization, 5 November 2026; discovery/design first.
**Area:** Runtime-owned business-message acceptance, per-definition delivery, native/genuine-model integration and Studio operations.
**Steward:** Sipke. Canonical feature and delivery anchor: [#3583](https://github.com/elsa-workflows/elsa-foundation/issues/3583).

## Purpose and authority

Deliver #3583 end to end under the [5 November owner instruction](https://github.com/elsa-workflows/elsa-foundation/issues/3583#issuecomment-6671359620), including necessary brainstorming/design. Preserve the selected business correlation, per-definition consumption, default buffering, starts/waits, active message-started admission, independent deduplication, API/activities and operator experience.

Use the existing feature record instead of duplicating its charter. This scope reactivates no unrelated program and preserves existing owners and priority.

## Sequencing and objectives

Run beside existing work with consolidation priority one and one implementation leaf active. Coordinate shared Runtime contracts with Unified Execution, StateMachine and Runtime Efficiency. The discussed standalone ingress family is a shared-owner proposal, not an implemented dependency/adopted ABI.

| Milestone | Outcome | State |
|---|---|---|
| M0 — Inventory/contracts | Code/provider/model gaps, owner decisions, reviewed spec and shared ingress/selection boundary | In progress; reviewed [spec 1216](../../specs/1216-durable-messaging/spec.md), T003 shared mapping open; T004 rules, Core contracts, authored BPMN provenance, material-interest mechanics and dormant preparation tested; physical acceptance not started |
| M1 — Acceptance | Durable API receipt, buffering, independent ID deduplication and inspection | Not started; depends on M0 |
| M2 — Delivery/admission | Late waits/starts, per-definition recovery, same-key exclusion and terminal release | Not started |
| M3 — Authoring/models | Authored send/receive/start and genuine native/BPMN integration | Not started |
| M4 — Operations/examples | Composed Studio diagnosis/recovery and runnable configuration/integrator docs | Not started |
| M5 — Qualification/closeout | Adverse-path/provider/multi-node proof, independent UX, review/merge and resulting-main evidence | Not started |

Milestones sequence work, not release scope: all selected capabilities remain first-release acceptance. Create leaf issues only when an actionable split needs them, following current triage/cap rules. Initial design needs no new child issue; checkpoints stay on #3583/its PR.

## Boundaries and links

- [#3583](https://github.com/elsa-workflows/elsa-foundation/issues/3583): scope, decisions, claim and state transitions.
- [Spec 1216](../../specs/1216-durable-messaging/spec.md): requirements, findings, design proposals and acceptance matrix.
- [BPMN Engine roadmap](bpmn-engine.md): existing general correlation/buffering direction. Reuse Runtime persistence, scheduler, execution/recovery and publication authority.
- [#2892 shared ingress advisory](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6670940060) and [#3840](https://github.com/elsa-workflows/elsa-foundation/pull/3840), owned under [#3820](https://github.com/elsa-workflows/elsa-foundation/issues/3820): coordinate; discussion is not adoption.
- Standalone triggers #3582 and background jobs #3586 remain separate products, not whole-product prerequisites.
- No competing broker/scheduler/store/authority, release, deployment, provider purchase, secrets or workflow-permission change.

## Completion

All agreed #3583 criteria need current evidence, including claimed provider/multi-node modes, downloadable independently inspected Studio journeys and resulting-main validation. Reconcile issue/program/spec/configuration/examples before closeout. A design packet, first milestone, unmerged PR or happy-path demonstration does not complete this goal.
