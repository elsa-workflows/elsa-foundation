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
| M1 — equivalent authoring | Epic #3739 / Features #3743–#3745 | Presentation, safe native extraction/Ungroup and BPMN subprocess/interchange delivered on both hosts |
| M2 — reusable promotion | Epic #3740 / Feature #3746 | Reviewed contract, ordinary draft/publication, fresh exact-version replacement and Undo |
| M3 — integrated delivery | Epic #3741 / Feature #3747 | Original #3610 criteria have browser/runtime evidence, reviewed merged changes and resulting-main qualification |

Native sub-issues carry the hierarchy. The [task map](../../specs/1204-named-embedded-blocks/tasks.md) records dependencies and acceptance mappings. Project status is scheduling state, not evidence. New issues use needs-triage; only dependency-satisfied authorized work becomes ready, respecting the repository cap. Parent closure requires its acceptance, not just closed children.

## Current checkpoint

Program, four Epics, six Features and nine leaf Tasks/Spikes exist with native parent relationships. Project 67 contains all 20 records, a Kanban WIP board hiding rollups, a full Roadmap table and the Backlog/Ready/In progress/In review/Blocked/Done status field. Establishment #3748 merged in PR #3760.

S001 #3749 is delivered by [PR #3767](https://github.com/elsa-workflows/elsa-foundation/pull/3767), merged as main `40a2cd585`. [Resulting-main acceptance](https://github.com/elsa-workflows/elsa-foundation/pull/3767#issuecomment-6623300307) passed all 22 native cases plus actual Studio graph/layout Undo/Redo with saved API snapshots. E0/F0/S001 are closed Done; T001 #3750 and its E1/F1 parents are in progress on Project 67. The [prototype receipts](../../specs/1204-named-embedded-blocks/evidence/prototype/README.md) establish bounded native child-result visibility, parent variables/occurrences, strict restart/resume, cancellation, supported recovery, nearest control targets and a real Studio authoring/history/run journey. They exposed supporting completion-causality and accepted-save/history defects, corrected in that merged PR. The temporary extraction UI was removed after qualification.

Production commands, equivalent Activity Definition authoring, promotion and integrated program acceptance remain unimplemented. Upstream #3795 added the bounded exact-version/portable-literal `elsa:taskBinding` interchange profile (see [its contract](../../Essentials/src/Activities/Bpmn/Interchange/README.md#exact-task-bindings)). Block-specific nested binding/DI/type/default fidelity and refusal qualification remain T004 obligations; native process execution alone does not satisfy them. See [research](../../specs/1204-named-embedded-blocks/research.md) for the precise supporting decisions and remaining limits.
