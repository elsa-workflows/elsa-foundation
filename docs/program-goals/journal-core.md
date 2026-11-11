# Journal-first runtime core

- **Status:** Proposed 2026-11-11; active once the owner approves [ADR 0098](../adr/0098-the-runtime-core-keeps-a-journal-per-run-and-derives-everything-else.md).
- **Area:** The runtime's execution core: how a run's state is stored, committed, delivered work, recovered and read.
- **Steward:** Sipke. **Program:** [#4175](https://github.com/elsa-workflows/elsa-foundation/issues/4175).
- **Authorization:** owner direction of 11 November 2026 ([#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892#issuecomment-6807409653), [#2564](https://github.com/elsa-workflows/elsa-foundation/issues/2564#issuecomment-6807592241)) and
  [ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus).

The program issue is the queue and progress record. The design, its targets and the answers to earlier documents are in ADR 0098 and are not repeated here.

## Outcome

Elsa runs on a core in which a run's durable truth is its journal, and the current core, its tests and its migrations are deleted. Activity authoring contracts, workflow definitions, the HTTP API and Studio behave as before, except that run details can lag a run.

## Rules of the program

- The new core is built beside the current one, behind a host switch. `main` stays shippable at every merge, with the current core as the default until the owner switches it.
- Clean break: new tables, no migration of runs in flight.
- One slice per pull request. Slices run in parallel once S2 has landed, where they touch different files.
- A slice is merged by its author session after a clean independent review round with gate evidence posted. The design, the switch of the default and the deletion of the old core wait for the owner.
- The current core gets no new features and no further efficiency slices. Defects in it are fixed only when they block the conformance suite or the switch.

## Slices

| Slice | What it delivers | Depends on |
|---|---|---|
| S0 | Design proposal: ADR 0098, this plan, the registry row and the ADR 0080 D0 amendment | owner approval |
| S1 | Conformance suite: a harness that runs a composed host against either core, and tests for the guarantees, area by area, passing on the current core. The 16 guarantees without a test get one | S0 |
| S2 | Storage contract; journal, head, inbox and wait-index tables; the in-memory and EF implementations on four engines; two-writer and process-kill tests of the append. Not wired into a host | S0 |
| S3 | Working set and commit path: a run that starts, runs activities and completes on the new core behind the host switch. Budgets for the new core's reference runs | S2 |
| S4 | Waits: bookmarks, stimuli, timers, exact resume | S3 |
| S5 | Faults, retry, incidents, cancellation | S3 |
| S6 | Effects: the dispatcher, parent and child runs, the post-commit intent handlers on the new core | S3 |
| S7 | Recovery: restart, takeover, the sweep for runs with unread work, leases for who drives; multi-node tests | S4, S6 |
| S8 | Projections: the projector, the tables the API and Studio read, history level, the wait-for-projector hook, the Dashboard on a query contract | S3 |
| S9 | Alterations, run deletion, trimming, executable retention without a lease per run | S5 |
| S10 | BPMN's direct model execution on the changed ports; the ETL, Rules and Agent enrichers and intent handlers | S6 |
| S11 | PostgreSQL, SQL Server and MySQL qualification; the round-trip budget | S3 onward |
| S12 | Switch the default | all of the above, the conformance suite passing on both cores, owner go-ahead |
| S13 | Delete the old core, its tests and its migrations; report the after figures; close #2564 and #4043 | S12, owner go-ahead |

## Relation to other programs

- [Runtime Efficiency #2531](runtime-efficiency.md): epic #2564 stops after #4166. Its targets and its method of proof move here.
- [Unified Execution #2892](unified-execution.md): the ports of the 9 November clarification change as ADR 0098 sets out. Model-owned semantics, the protocol driver port and the separately owned ETL and Rules domains are untouched. The execution-core milestone is paused and has changed only tests and reports.
- Sequencing: this program runs beside the active programs. It displaces the remaining slices of #2564. Foundation/Studio consolidation keeps priority one.

## Completion

Close #4175 when the default has been switched, the old core is deleted, the conformance suite passes on `main`, and the deletion PR has reported the after figures against the targets of ADR 0098.
