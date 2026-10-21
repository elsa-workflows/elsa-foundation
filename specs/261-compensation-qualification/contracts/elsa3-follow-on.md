# Contract: the Elsa 3 follow-on assessment

PR 3 writes `docs/reports/native-compensation-elsa3-follow-on.md` from this outline and lists it in `docs/reports/README.md`. It is an assessment, not a design. Implementing compensation in Elsa 3 is a separate effort, outside this program.

## Sources

- [elsa-core#4015](https://github.com/elsa-workflows/elsa-core/issues/4015): the Elsa 3 compensation request that #3048 names.
- [elsa-core#8884](https://github.com/elsa-workflows/elsa-core/issues/8884): the Elsa 3 compensation work the [program charter](../../../docs/program-goals/native-compensation.md#relationship-to-other-programs) records as paused until the M0 packet was reviewed.
- Specs 250, 251, 258, 260 and 261 of this repository.

The assessment says nothing about either issue's content beyond its title and its charter row. A reader who owns them maps their asks onto the scenario list.

## Outline

1. **What Elsa 4 settled.** The semantic contract in one page, linking spec 250's semantics and the later deltas: the scope and journal, newest-first recovery, `Compensated` versus a re-raised fault, uncertain outcomes, bounded retries with backoff, the intervention wait, operator retry, resolve and abandon, nesting and hand-over, confirmation, explicit Compensate, cancellation never compensates.
2. **What is engine-neutral and what is not.** Engine-neutral: the scenarios below, the participant contract (spec 251 FR-220), the outcome vocabulary, the operator decisions. Elsa 4-specific: structural private state, the runtime seams R-1 to R-7, checkpoints and the outbox, the Studio paired step list and the journal endpoint. Elsa 3 would need its own mechanism for each of the second group; the assessment names what each mechanism must guarantee, not how.
3. **Reusable semantic scenarios.** The table below, each with its Elsa 4 proof, so an Elsa 3 implementation can reuse the scenario and its expected outcome as an acceptance test.
4. **Gaps an Elsa 3 implementation would face.** Stated as questions for that effort: where the journal lives durably; how a handler binds to its forward occurrence's result; where a stable operation key comes from; how an intervention wait is expressed; how cancellation avoids running handlers; how a nested scope's work reaches its parent.
5. **Recommendation.** Reuse the scenarios and the participant contract as the acceptance suite; do not port the Elsa 4 runtime seams; decide the Elsa 3 mechanism in that effort. Update the program charter's Elsa 3 row to point at this assessment.

## Reusable semantic scenarios

Each is phrased without Elsa 4 types. "Participant" means an external system that follows the participant contract.

| # | Scenario | Expected outcome | Elsa 4 proof |
|---|---|---|---|
| E1 | Three steps; the third fails | The first two are undone, second then first; the scope ends `Compensated`; the failed step's own undo does not run | Spec 250 US2, P1–P4 |
| E2 | A crash after an undo's effect, before it is recorded | The undo runs again with the same key; the participant treats it as a repeat; one undo | Spec 250 P2 |
| E3 | A scope set to re-raise the failure after undoing | Undo runs, then the scope fails with the step's failure | Spec 250 FR-008 |
| E4 | A step's response is lost (effect applied) | The step is uncertain; its undo runs against the step's key; nothing left | Spec 251 J1 |
| E5 | As E4, but the effect arrives after the undo | The late request is refused; nothing left | Spec 251 J2 |
| E6 | An uncertain step with no undo | Recovery does not treat it as having had no effect; an operator decides | Spec 251 J6 |
| E7 | An undo fails twice, then succeeds | Retried with the same key; one undo; recovery continues | Spec 251 J3 |
| E8 | An undo fails every time | The scope waits for an operator; nothing is reported undone; the run keeps running | Spec 251 J4 |
| E9 | An operator retries a waiting undo, twice with one key | One more run; recovery finishes | Spec 251 J5, spec 260 O1 |
| E10 | An operator resolves a waiting undo as effect absent, or as effect kept | Recovery continues; the entry is never undone afterwards | Spec 260 O2h, O3h |
| E11 | An operator resolves an uncertain step as absent, or as kept | Absent: earlier steps undone. Kept: the scope runs the next step | Spec 260 O2, O3 |
| E12 | An operator abandons a waiting recovery | No further undo; the scope fails with an "abandoned" outcome-unknown fault; an enclosing scope treats it as uncertain | Spec 260 O4, O4n |
| E13 | A completed inner scope, a later outer step fails | The inner scope's work is undone by the outer scope, with the inner undos and results | Spec 258 N1 |
| E14 | A scope inside a loop of three; a later step fails | Three undos, newest iteration first, each with its own data | Spec 258 L1 |
| E15 | Work confirmed, then a later step fails | Confirmed work is kept; the rest is undone | Spec 258 C1, C2 |
| E16 | An explicit "compensate now" inside a step | The running step is stopped and treated as uncertain; earlier steps undone | Spec 258 E1, E3 |
| E17 | The run is cancelled during forward work, recovery or a wait | No undo starts | Spec 258 X1 |
| E18 | Republishing during recovery | The run finishes with the undos it started with | Spec 250 FR-012 |
| E19 | Backoff between undo attempts, with a restart during the wait | Each attempt once, not before its due time | Spec 261 BK1–BK6 |
| E20 | Stale or duplicate operator actions | Refused or replayed; nothing applied twice | Spec 260 O5, O5r |
