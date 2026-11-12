# Journal-first runtime core

- **Status:** Active since 2026-11-11 by the owner's goal of that day; design first. No code is written before the owner accepts [ADR 0098](../adr/0098-the-runtime-core-keeps-a-journal-per-run-and-derives-everything-else.md).
- **Area:** The runtime's execution core: how a run's state is stored, committed, delivered work, recovered and read.
- **Steward:** Sipke. **Program:** [#4175](https://github.com/elsa-workflows/elsa-foundation/issues/4175).
- **Authorization:** the owner's goal and decisions of 11 November 2026, recorded on [#4175](https://github.com/elsa-workflows/elsa-foundation/issues/4175), [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6807409653) and [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564#issuecomment-6807592241), and
  [ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus).

The program issue is the queue and progress record. The design, its targets and its answers to earlier documents are in ADR 0098 and are not repeated here.

## Outcome

Elsa runs on a core in which a run's durable truth is its journal, and the current core, its tests and its migrations are deleted. Activity authoring contracts, workflow definitions, the HTTP API and Studio behave as before, except that run details can lag a run.

## Rules of the program

The owner's, from the goal recorded on #4175:

- The new core is built beside the current one, behind a host switch and the same host-facing contracts. `main` stays shippable at every merge, with the current core as the default until the owner switches it.
- Clean break: new tables, no migration of runs in flight.
- One slice per pull request. Slices run in parallel once S2 has landed, where they touch different files.
- A slice is merged by its author session after a clean independent review round with gate evidence posted. That is an owner exception to the rule that an author never merges its own pull request, for this program's slices. The design, the switch of the default and the deletion of the old core wait for the owner.
- A finished pull request may be merged with administrator rights when CI is stuck with no failed job; the pull request then records what CI had and had not reported.
- Epic #2564 gets no further slices on the current core.

Proposed by ADR 0098, for the owner's approval with it: the ports of the 9 November clarification keep their contracts, and ownership per partition comes after the old core is deleted.

## Slices

| Slice | What it delivers | Depends on |
|---|---|---|
| S0 | Design proposal: ADR 0098, this plan, the registry row and the ADR 0080 D0 amendment | owner acceptance |
| S1 | Conformance suite: a harness that runs a composed host against either core through the ports, the HTTP API and the store interfaces; tests for the guarantees, area by area, passing on the current core. Guarantees for which no test was found get one | S0 |
| S2 | Storage contract; journal, head, inbox, wait-index and effects tables; the in-memory and EF implementations on four engines; two-writer and process-kill tests of the append, the inbox and the effects table. Not wired into a host | S0 |
| S3 | Working set and the checkpoint commit store on the journal: a run that starts, runs activities and completes on the new core behind the host switch, with the store interfaces answered from the working set. Budgets for the new core's reference runs | S2 |
| S4 | Projections: the projector, the tables the API and Studio read, history level, the wait-for-projector hook, the Runtime API's reads and the Dashboard on a query contract | S3 |
| S5 | Waits: bookmarks, stimuli, timers and the timer pumps, exact resume, conditions on another run's wait | S3 |
| S6 | Faults, retry, incidents, cancellation, holds, poison records | S3 |
| S7 | Effects: the dispatcher with per-effect retry; parent and child runs with DispatchWorkflow's enrichers and dispatch records; the intent handlers | S3 |
| S8 | Background activity work, user tasks, test-run scopes | S5, S7 |
| S9 | Recovery: restart, takeover, the sweep for runs with unconsumed work, the lease that decides who drives, the distributed command transport; multi-node and process-kill tests | S5, S7 |
| S10 | Alterations, run deletion with tombstones, trimming, executable retention by reference | S4, S5, S6, S7 |
| S11 | BPMN's direct model execution and its six enqueues as inbox commands; the ETL, Rules and Agent enrichers and intent handlers verified on the new core | S5, S7, S9 |
| S12 | PostgreSQL, SQL Server and MySQL qualification; the round-trip budget | S3 onward |
| S13 | Switch the default | all of the above, the conformance suite passing on both cores, owner go-ahead |
| S14 | Delete the old core, its tests and its migrations; report the after figures; close #2564 and #4043 | S13, owner go-ahead |
| S15 | Ownership per partition and commits shared across runs, which the owner's goal lists in the target shape | S14, owner decision |
| S16 | A cleaner commit shape and the per-entity store interfaces off the public surface | S14, owner decision |

## Relation to other programs

- [Runtime Efficiency #2531](runtime-efficiency.md): epic #2564 stops after #4166. Its targets and its method of proof are taken over here. Its remaining slices are not built.
- [Unified Execution #2892](unified-execution.md): the ports of the 9 November clarification are unchanged by ADR 0098. Model-owned semantics and the separately owned ETL and Rules domains are untouched. The pull requests of its execution-core milestone that the survey of 11 November read (#4039 to #4129) had changed only tests and reports.
- Sequencing: this program runs beside the active programs. It displaces the remaining slices of #2564. Foundation/Studio consolidation keeps priority one.

## Completion

Close #4175 when the default has been switched, the old core is deleted, the conformance suite passes on `main`, and the deletion pull request has reported the after figures against the targets of ADR 0098. S15, which the owner's goal names, and S16 then continue under their own issues; closing #4175 before S15 is part of what the owner approves with ADR 0098.
