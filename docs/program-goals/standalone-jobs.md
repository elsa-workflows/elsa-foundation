# Standalone durable jobs

- **Status:** Active; delivery authorized 5 November; Jobs admission adopted on the owner's authority 6 November 2026.
- **Area:** Standalone .NET jobs, optional native/BPMN integration, API/Studio operations.
- **Steward:** Sipke.
- **Canonical feature/program delivery record:** [#3586](https://github.com/elsa-workflows/elsa-foundation/issues/3586).
- **Authorization:** [owner instruction and decisions](https://github.com/elsa-workflows/elsa-foundation/issues/3586#issuecomment-6671631992).

## Purpose and boundary

Deliver the complete #3586 product: registered .NET handlers, durable inputs/results and attempts,
immediate/delayed/recurring submission, named queues, recovery and operations. Standalone consumers
and workers need no workflow graph or unrelated workflow authoring/runtime packages. Optional native
and BPMN adapters invoke the same handlers and consume their results.

Queue, workflow-instance, workflow-definition and job-type pauses durably gate new attempts across
nodes and restart. Accepted work remains recoverable, unrelated shared-queue work remains eligible,
and running attempts finish. Every applicable scope must allow a start, including retries. Held work
uses no execution capacity. Completed physical results survive a parent pause; the owner selected
holding result-driven continuation under workflow-instance/definition pauses until resume.

The owner selected a durable start-permission cutoff: an attempt starts when the store commits its
permission. A grant issued before pause may enter its handler afterward; operations show that handoff
as Starting/Running. Pause never suspends a process or undoes its effects.

## Sequencing and milestones

The 5 November instruction supersedes only this feature's historical post-#2892 sequence. Run beside
existing work, preserve priority-one Foundation/Studio consolidation, and resume no unrelated paused
scope. Live owning records determine other streams' current status.

One implementation leaf and one integration/review lane. Keep milestones/acceptance on #3586; create
a child issue only for independent ownership or a material dependency, as `needs-triage`. No duplicate
feature/program rollup is needed. Claim ownership and state transitions remain on #3586.

| Milestone | Outcome | State |
|---|---|---|
| M0 | Current-source/provider research, scoped domain admission, reviewed spec/plan/tasks and pre-code UX acceptance | Design reviewed; domain adopted 6 November; runtime contracts remain subject to concrete consumer proof |
| M1 | Packaged standalone client/worker, handler/input/result identity, durable intake and recoverable attempts | Checkpoint passed at b706023ddd; [two-profile receipt](../../specs/1218-standalone-jobs/evidence/m1-packaged-standalone.json) |
| M2 | Immediate/delayed/recurring jobs, retry/cancel/timeout, all four pauses and distributed concurrency/recovery | Checkpoint passed with source boundaries in the [M2 receipt](../../specs/1218-standalone-jobs/evidence/m2-packaged-scheduling-recovery.json); final-source and application-clock witness still open |
| M3 | Real native and BPMN invocation of the same handler, retained results and model-owned continuation | Source with 54 cases per provider prepared; runtime qualification remains unrun |
| M4 | Authorized API/Studio operations and integrator/operator documentation | Source with 24 composed Jobs executions planned; API/security/UX acceptance remains unrun |
| M5 | Complete packaged, multi-worker/provider and composed UX qualification; current-head review/checks/merge and resulting-main acceptance | In progress; final-source whole suites, packages, UX, CI, merge and main acceptance remain incomplete |

M1 qualifies the eight-module standalone package boundary and five focused separate-process cases on SQLite/PostgreSQL at its recorded source. M2 additionally qualifies 27 standalone scheduling/recovery cases per provider at 49a49d36b4, with the prior-source whole-suite and mutation boundaries recorded in its receipt. These checkpoints do not establish final-source Jobs, model, operations/UX or resulting-main acceptance. The [matrix](../../specs/1218-standalone-jobs/acceptance-matrix.md) retains every outstanding criterion.

## Shared work and agreement

- [#3582](https://github.com/elsa-workflows/elsa-foundation/issues/3582) owns standalone triggers and
  subscriptions. Agree schedule/occurrence identity and reusable arithmetic without competing
  scheduling authority or assuming unmerged work exists.
- [#3583](https://github.com/elsa-workflows/elsa-foundation/issues/3583) owns durable business messaging.
  Coordinate compatible admission/idempotency primitives; do not deliver messaging here.
- [Unified Execution #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) owns its
  integrations and the historical task-runner follow-up. Consume suitable contracts; its whole
  program is not a prerequisite. Coordinate shared Runtime changes with current
  [StateMachine #3815](https://github.com/elsa-workflows/elsa-foundation/issues/3815) work.
- [ADR0096](../adr/0096-jobs-owns-durable-handler-invocations.md) records the explicit 6 November
  owner-authority exception adopting only Jobs admission; no multi-architect consensus is claimed. Ordinary adapters consume
  accepted contracts; amendments to [#3052](https://github.com/elsa-workflows/elsa-foundation/issues/3052)
  topics follow its agreement process without placing the whole job product in that initiative.

## Completion and exclusions

Close only when every #3586 criterion has actual merged/resulting-main evidence: packaged standalone
usage, genuine native/BPMN integration, failure/restart/handover, pause overlaps/races, tenant permissions
and inspected composed Studio journeys. State provider/deployment guarantees precisely. Keep passed,
failed, skipped, cancelled, unavailable and incomplete evidence distinct.

No shared/production deployment, production credentials or package release is authorized. Human User
Tasks, another graph language, unrelated programs and universal execution abstractions are outside
scope. Physical execution may repeat; external effects require integrator idempotency.
