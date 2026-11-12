# Journal-first runtime core

- **Status:** Proposed. The program becomes active when the pull request that carries this plan, its registry row and the ADR 0080 D0 amendment merges, which happens on the owner's acceptance of [ADR 0098](../adr/0098-the-runtime-core-keeps-a-journal-per-run-and-derives-everything-else.md). No code is written before that.
- **Area:** The runtime's execution core: how a run's state is stored, committed, delivered work, recovered and read.
- **Steward:** Sipke. **Program:** [#4175](https://github.com/elsa-workflows/elsa-foundation/issues/4175).
- **Authorization:** the owner's goal of 11 November 2026, recorded word for word on [#4175](https://github.com/elsa-workflows/elsa-foundation/issues/4175#issuecomment-6817195557), his decisions of that day on [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6807409653) and [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564#issuecomment-6807592241), and
  [ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus).

The program issue is the queue and progress record. The design, its targets and its answers to earlier documents are in ADR 0098 and are not repeated here.

## Outcome

Elsa runs on a core in which a run's durable truth is its journal, and the current core, its tests and its migrations are deleted. Activity authoring contracts, workflow definitions, the HTTP API and Studio behave as before, except that run details can lag a run.

## Rules of the program

The owner's, from [the goal and his answers to its open choices](https://github.com/elsa-workflows/elsa-foundation/issues/4175#issuecomment-6817195557):

- The new core is built beside the current one, behind a host switch and the same host-facing contracts. `main` stays shippable at every merge, with the current core as the default until the owner switches it.
- Clean break: new tables, no migration of runs in flight.
- One slice per pull request. Slices run in parallel once S2 has landed, where they touch different files.
- A slice is merged by its author session after a clean independent review round with gate evidence posted. That is an owner exception to the rule that an author never merges its own pull request, for this program's slices. The design, the switch of the default and the deletion of the old core wait for the owner.
- A finished pull request may be merged with administrator rights when CI is stuck with no failed job; the pull request then records what CI had and had not reported.
- Epic #2564 gets no further slices on the current core.

Proposed by ADR 0098, for the owner's approval with it: the ports of the 9 November clarification keep their contracts; the per-entity store interfaces stay public, answered by the new core, until S16; and ownership per partition comes after the old core is deleted.

## Slices

| Slice | What it delivers | Depends on |
|---|---|---|
| S0 | Design proposal: ADR 0098, this plan, the registry row and the ADR 0080 D0 amendment | owner acceptance |
| S1 | Conformance suite: a harness that runs a composed host against either core through the ports, the HTTP API and the store interfaces; tests for the guarantees, area by area, passing on the current core. Guarantees for which no test was found get one. A test-only fault seam for the crash windows: a decorator over the checkpoint commit store for before and after a commit, and an internal hook each core implements for inside its transaction. The round-trip counts of the two 1,000-step reference runs on the current core | S0 |
| S2 | Storage contract; the seven tables; the in-memory and EF implementations on four engines, with the SQL dialects; two-writer and process-kill tests of the append, the lease statements, the inbox, the effects table and a wait row's revision. Not wired into a host | S0 |
| S3 | Working set and the checkpoint commit store on the journal: a run that starts, runs activities and completes on the new core behind the host switch, with the lease taken per pass, the persistence mode resolved per workflow, and the store interfaces answered from the working set, the head and read-only copies. Budgets for the new core's reference runs | S2 |
| S4 | Projections: the projector, the tables the API and Studio read, history level, the wait-for-projector hook, the Runtime API's reads and the Dashboard on a query contract | S3 |
| S5 | Waits: bookmarks, stimuli, volatile waits, timers and the timer pumps, recurring triggers' starts, exact resume, conditions on another run's wait | S3, S4 |
| S6 | Faults, retry with delayed work and its due time, compensation, incidents, cancellation, pause and unpause, holds, poison records | S3, S4 |
| S7 | Effects: delivery by the committing node, the dispatcher with per-effect retry; parent and child runs with DispatchWorkflow's enrichers and dispatch records; the intent handlers | S3, S4 |
| S8 | Background activity work, user tasks, test-run scopes | S5, S7 |
| S9 | Recovery: restart, takeover, the sweep for runs with work and no live lease, admission shedding, the distributed command transport; multi-node and process-kill tests | S5, S7 |
| S10 | Alterations, run deletion with tombstones, snapshots that drop terminal activities to record rows, trimming, executable pins and the executable store's answer to a refused delete | S4, S5, S6, S7 |
| S11 | BPMN's direct model execution and its six enqueues as inbox commands; the ETL, Rules and Agent enrichers and intent handlers verified on the new core | S5, S7, S9 |
| S12 | PostgreSQL, SQL Server and MySQL qualification; the round-trip budget | S3 onward |
| S13 | Switch the default | all of the above, the conformance suite passing on both cores, owner go-ahead |
| S14 | Delete the old core, its tests and its migrations; report the after figures; close #2564 and #4043 | S13, owner go-ahead |
| S15 | Ownership per partition and commits shared across runs, which [the owner's goal](https://github.com/elsa-workflows/elsa-foundation/issues/4175#issuecomment-6817195557) lists in the target shape | S14, owner decision |
| S16 | The per-entity store interfaces off the public surface, which completes the owner's decision of 11 November, and a cleaner commit shape | S14, owner go-ahead on the commit shape |

## Relation to other programs

- [Runtime Efficiency #2531](runtime-efficiency.md): epic #2564 stops after #4166. Its targets and its method of proof are taken over here. Its remaining slices are not built.
- [Unified Execution #2892](unified-execution.md): the ports of the 9 November clarification are unchanged by ADR 0098. Model-owned semantics and the separately owned ETL and Rules domains are untouched. The pull requests of its execution-core milestone that the survey of 11 November read (#4039 to #4129) had changed only tests and reports.
- Sequencing: this program runs beside the active programs. It displaces the remaining slices of #2564. Foundation/Studio consolidation keeps priority one.

## Completion

Close #4175 when the default has been switched, the old core is deleted, the conformance suite passes on `main`, and the deletion pull request has reported the after figures against the targets of ADR 0098. S15, which the owner's goal names, and S16 then continue under their own issues; closing #4175 before S15 is part of what the owner approves with ADR 0098.
