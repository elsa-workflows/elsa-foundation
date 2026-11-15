# Named Embedded Blocks

**State:** Active — owner-authorized end-to-end delivery, 3 November 2026.
**Steward:** Sipke. **Delivery:** Codex, one implementation leaf at a time.
**Program/request:** [#3610](https://github.com/elsa-workflows/elsa-foundation/issues/3610).
**Scheduling:** [Project 67](https://github.com/orgs/elsa-workflows/projects/67).
**Canonical specification/design:** [spec 1204](../../specs/1204-named-embedded-blocks/spec.md), [plan](../../specs/1204-named-embedded-blocks/plan.md), [tasks](../../specs/1204-named-embedded-blocks/tasks.md).

## Goal and authorization

Authors organize existing logic into named embedded blocks, safely extract supported Sequence/Flowchart/BPMN regions and promote eligible blocks to ordinary reusable activities through reviewed explicit contracts.

On 3 November the owner selected Block terminology, bounded extraction, BPMN extraction, safe promotion analysis and review → publish → replace, then instructed: “Update the plan to include that durable breakdown, then go ahead and deliver the program end to end as your stated goal.” This authorizes implementation, normal scoped PR/review/merge delivery and resulting-main qualification. Establishment is not product completion.

Sequencing: run beside active programs, displace none, preserve consolidation's priority one, coordinate shared Studio/runtime paths and reactivate no parked backlog. Reuse native composition/value boundaries; no new execution-model program, release, deployment, private activity catalog or optional bpmn-js prerequisite.

## Milestones

| Milestone | Durable owner | Exit |
|---|---|---|
| M0 — design and prototype | Epic #3738 / Feature #3742 | Establishment #3748 and real-host prototype #3749; paired behavior proved before production completion |
| M1 — equivalent authoring | Features #3743–#3745 | Presentation, safe native extraction/Ungroup and BPMN subprocess/interchange delivered on both hosts |
| M2 — reusable promotion | Feature #3746 | Reviewed contract, ordinary draft/publication, fresh exact-version replacement and Undo |
| M3 — integrated delivery | Program #3610 | Original #3610 criteria have browser/runtime evidence, reviewed merged changes and resulting-main qualification |

Native sub-issues carry the hierarchy. The [task map](../../specs/1204-named-embedded-blocks/tasks.md) records dependencies and acceptance mappings. Project status is scheduling state, not evidence. New issues use needs-triage; only dependency-satisfied authorized work becomes ready, respecting the repository cap. Parent closure requires its acceptance, not just closed children.

## Current checkpoint

Establishment #3748 / PR #3760 created the original Program, four Epics, six Features and nine leaf Tasks/Spikes with native parent relationships. Project 67 was configured with those 20 records, a Kanban WIP board hiding rollups, a full Roadmap table and the Backlog/Ready/In progress/In review/Blocked/Done status field. That is the establishment receipt, not a claim that the original wrappers remain the active queue; current ownership follows the owner triage below. Reconcile live board ownership and status before program closure.

S001 #3749 is delivered by [PR #3767](https://github.com/elsa-workflows/elsa-foundation/pull/3767), merged as main `40a2cd585`. [Resulting-main acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/3767#issuecomment-6623300307) passed all 22 native cases plus actual Studio graph/layout Undo/Redo with saved API snapshots. E0/F0/S001 are closed Done. T001 #3750 and Feature #3743 subsequently completed through [PR #3798](https://github.com/elsa-workflows/elsa-foundation/pull/3798), main `9daaa3206`, with [resulting-main acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/3798#issuecomment-6641358323). T002 #3751 completed through [PR #3837](https://github.com/elsa-workflows/elsa-foundation/pull/3837), main `e7d8a5d50530d21b3476248af1f2d5835ab7bf32`, with [resulting-main acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/3837#issuecomment-6881224741). T003 #3752 is active for the shared authoring commands. BPMN, reusable promotion and final program qualification remain outstanding. The [prototype receipts](../../specs/1204-named-embedded-blocks/evidence/prototype/README.md) establish bounded native child-result visibility, parent variables/occurrences, strict restart/resume, cancellation, supported recovery, nearest control targets and a real Studio authoring/history/run journey. They exposed supporting completion-causality and accepted-save/history defects, corrected in that merged PR. The temporary extraction UI was removed after qualification.

Production commands, equivalent Activity Definition authoring, promotion and integrated program acceptance remain unimplemented. Upstream #3795 added the bounded exact-version/portable-literal `elsa:taskBinding` interchange profile (see [its contract](../../Essentials/src/Activities/Bpmn/Interchange/README.md#exact-task-bindings)). Block-specific nested binding/DI/type/default fidelity and refusal qualification remain T004 obligations; native process execution alone does not satisfy them. See [research](../../specs/1204-named-embedded-blocks/research.md) for the precise supporting decisions and remaining limits.

Owner triage on 9 November folded redundant Epic wrappers #3739/#3740/#3741 and acceptance Feature #3747. The remaining native hierarchy is Program #3610 → Feature #3744 → T002/#3751 and T003/#3752; Program #3610 → Feature #3745, which owns T004; and Program #3610 → Feature #3746 → T005/#3754 and T006/#3755. T007 is the Program exit qualification. Duplicate T004/#3753 and T007/#3756 were folded into those surviving owners. Closed-not-planned wrappers are not delivered product scope, and both folded acceptance scopes remain outstanding. The [reparent instruction](https://github.com/elsa-workflows/elsa-foundation/issues/3739#issuecomment-6739780287) and [promotion-wrapper fold](https://github.com/elsa-workflows/elsa-foundation/issues/3740#issuecomment-6739781247) are reflected in this ownership map; delivered design/prototype and presentation retain their historical evidence. On 12 November the owner instructed a fresh landed-work audit and resumption of the remaining work. Existing evidence retains its original source and status; the interrupted Cloud Native07 run remains owner-interrupted/unknown, and old worker leases are not current validation authority.
