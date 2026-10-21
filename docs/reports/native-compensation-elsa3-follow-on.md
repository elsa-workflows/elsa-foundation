# Native compensation: the Elsa 3 follow-on

**Program:** [Native compensation #3035](https://github.com/elsa-workflows/elsa-foundation/issues/3035) · **Feature:** [#3048](https://github.com/elsa-workflows/elsa-foundation/issues/3048) (M5) · **Spec:** [261](../../specs/261-compensation-qualification/spec.md) · **Outline:** [Elsa 3 follow-on contract](../../specs/261-compensation-qualification/contracts/elsa3-follow-on.md) · **Qualification record:** [native-compensation-qualification.md](native-compensation-qualification.md)

This is an assessment, not a design. It says what an Elsa 3 implementation of compensation can reuse from Elsa 4's, and what it would have to decide for itself. Implementing compensation in Elsa 3 is a separate effort, outside this program.

## Sources

- [elsa-core#4015](https://github.com/elsa-workflows/elsa-core/issues/4015), "Compensation": the Elsa 3 compensation request that #3048 names.
- [elsa-core#8884](https://github.com/elsa-workflows/elsa-core/issues/8884), "Program: Elsa 3 Native Compensation and Saga Recovery": the Elsa 3 compensation program, which elsa-core#4015 belongs to.
- Specs [250](../../specs/250-native-compensation/spec.md) (M1), [251](../../specs/251-compensation-uncertainty/spec.md) (M2), [258](../../specs/258-compensation-nesting/spec.md) (M3), [260](../../specs/260-compensation-operator-actions/spec.md) (M4) and [261](../../specs/261-compensation-qualification/spec.md) (M5) of this repository.

Beyond their titles, this assessment says nothing about either issue's content: their scope, decisions and status are theirs. A reader who owns them maps their asks onto the scenarios below.

## 1. What Elsa 4 settled

The semantic contract is [spec 250's semantics](../../specs/250-native-compensation/contracts/semantics.md), with the deltas of [spec 251](../../specs/251-compensation-uncertainty/contracts/semantics.md), [spec 258](../../specs/258-compensation-nesting/contracts/semantics.md), [spec 260](../../specs/260-compensation-operator-actions/contracts/semantics.md) and spec 261 (backoff). In one page:

- **Scope and journal.** A compensation scope runs named steps in order. Each completed step is recorded in the scope's journal with its operation key and result; a step may have a compensation that undoes it.
- **Recovery.** When a step fails, the scope undoes the completed steps, newest first, by running their compensations one at a time. The failed step's own compensation does not run. The scope then ends `Compensated`, or re-raises the step's failure when it is set to (`faultAfterCompensation`).
- **Uncertain outcomes.** A step that cannot tell whether its effect happened reports `outcome-unknown`. Recovery treats it as possibly done and runs its compensation, which must work either way; a step without a compensation waits for an operator. It is never treated as having had no effect.
- **Bounded retries with backoff.** A failed compensation runs again, up to a maximum number of attempts, with the same operation key, optionally after a wait that grows per attempt up to a cap. The wait survives a restart and holds nothing else back.
- **The intervention wait.** When every attempt fails, the scope waits for an operator; the run keeps running and nothing is reported undone.
- **Operator actions.** An operator retries the compensation, resolves it (effect absent, or effect kept) or abandons the recovery. Each action is idempotent, refused when it no longer applies, and recorded with who acted and why. Abandoning faults the scope with an uncertain outcome, so an enclosing scope treats it as an uncertain step.
- **Nesting and hand-over.** A scope that finishes inside another hands its completed work to the enclosing scope, which undoes it, with the inner compensations and results, if a later step fails. A scope in a loop hands over once per iteration; the newest iteration is undone first.
- **Confirmation.** A Confirm, or a scope set to confirm on completion, marks completed work as never to be undone.
- **Explicit Compensate.** A Compensate activity starts recovery on purpose; the step it runs in is stopped and treated as uncertain.
- **Cancellation never compensates.** Cancelling the run stops forward work and starts no compensation; one already running may finish.
- **Pinning.** A run finishes with the compensations of the version it started on, whatever is published later.

## 2. What is engine-neutral and what is not

**Engine-neutral, reusable as they are:**

- the semantic scenarios in section 3, phrased without Elsa 4 types;
- the participant contract ([spec 251](../../specs/251-compensation-uncertainty/spec.md) FR-220; the [compensation guide's version](../../website/docs/essentials/compensation.md#what-a-participant-must-do)): apply at most once per operation key, undo for the target key, refuse a late effect after its undo, treat a repeated undo as done, report an uncertain outcome only when it truly cannot tell;
- the outcome vocabulary: completed, uncertain, compensated, compensated while uncertain, resolved as effect absent or effect kept, confirmed, abandoned;
- the operator decisions: retry, resolve absent, resolve kept, abandon, each with a reason, refused when stale.

The [compensation sample](../../samples/Elsa.Samples.Compensation/README.md)'s participant is an engine-neutral reference for the contract; only its two activity classes are Elsa 4 code.

**Elsa 4-specific, not reusable:** structural private state and its upcasters; the runtime seams R-1 to R-4 ([spec 258](../../specs/258-compensation-nesting/contracts/runtime-seams.md)), R-5 ([spec 260 operator actions](../../specs/260-compensation-operator-actions/contracts/operator-actions.md)), R-6 ([the activity state view](../../specs/260-compensation-operator-actions/contracts/journal-and-permissions.md)) and R-7 ([the delayed child schedule](../../specs/261-compensation-qualification/contracts/runtime-seam.md)); checkpoints, persistence modes and the post-commit outbox; the side-effect profile that commits an external activity before it runs; the Studio paired-step list; the journal endpoint.

Elsa 3 would need its own mechanism for each of the second group. What each mechanism must guarantee:

| Elsa 4 mechanism | What an Elsa 3 mechanism must guarantee |
|---|---|
| Structural private state, committed with each step | The journal survives a crash at any point and never records a compensation as done before its effect committed (E2). |
| Commit before an external activity runs | After a crash, recovery knows a step or compensation may have run, so it runs it again with the same key instead of skipping it (E2). |
| R-1, R-2 ancestor notification | A Confirm or Compensate reaches the nearest enclosing scope, or a named one, exactly once (E15, E16). |
| R-3 descendant-fault delivery | A failure deep inside a step reaches the scope that owns the step (E1). |
| R-5 operator decisions | An operator's decision reaches the waiting scope once, after a crash too, and a stale one is refused (E10–E12, E20). |
| R-7 delayed child schedule | A retry waits durably, runs once after its due time, and holds nothing else back (E19). |
| Executable pinning | A run in recovery uses the compensations it started with (E18). |

## 3. Reusable semantic scenarios

Each scenario is an acceptance test an Elsa 3 implementation can reuse with its expected outcome. "Participant" is an external system that follows the participant contract. The Elsa 4 proof is the journey that shows it; its exact revision and result are in the [qualification record](native-compensation-qualification.md#2-qualification-matrix) (row Q1 for the backend journeys).

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
| E13 | A completed inner scope, a later outer step fails | The inner scope's work is undone by the outer scope, with the inner undos and results | Spec 258 N1; sample S2 |
| E14 | A scope inside a loop of three; a later step fails | Three undos, newest iteration first, each with its own data | Spec 258 L1 |
| E15 | Work confirmed, then a later step fails | Confirmed work is kept; the rest is undone | Spec 258 C1, C2; sample S2 |
| E16 | An explicit "compensate now" inside a step | The running step is stopped and treated as uncertain; earlier steps undone | Spec 258 E1, E3 |
| E17 | The run is cancelled during forward work, recovery or a wait | No undo starts | Spec 258 X1 |
| E18 | Republishing during recovery | The run finishes with the undos it started with | Spec 250 FR-012; spec 261 Q10 |
| E19 | Backoff between undo attempts, with a restart during the wait | Each attempt once, not before its due time | Spec 261 BK1–BK6; sample S3 |
| E20 | Stale or duplicate operator actions | Refused or replayed; nothing applied twice | Spec 260 O5, O5r |

The sample's three workflows (S1 to S3) are also usable as an end-to-end smoke for an Elsa 3 implementation: S1 is E1 with a participant, S2 is E13 with E15, and S3 is E8, E19 and E10 in one run.

## 4. Gaps an Elsa 3 implementation would face

Questions for that effort, not answers:

- **Where the journal lives durably.** Elsa 4 keeps it in the scope's private state, committed with each step. What in Elsa 3 commits a step's completion and its journal entry together?
- **How a handler binds to its forward occurrence's result.** Elsa 4 resolves a step-result reference through causal lineage, so a loop iteration's undo gets that iteration's result and a handed-over undo still gets the inner step's. What gives an Elsa 3 undo the right occurrence's output?
- **Where a stable operation key comes from.** It must be the same after a crash, on an automatic retry and on an operator retry, and an undo must know its step's key. What identity in Elsa 3 survives all three?
- **How an intervention wait is expressed.** The run must keep running, with an open incident an operator acts on, and nothing reported undone. What does Elsa 3's incident model offer, and how does an operator's decision reach the scope exactly once?
- **How cancellation avoids running handlers.** Cancelling must stop forward work and start no undo. Does Elsa 3's cancellation path run any activity's cleanup that would look like compensation?
- **How a nested scope's work reaches its parent.** A finished inner scope must hand its entries to the nearest enclosing scope, once, across restarts, and not across a reusable-activity boundary.

## 5. Recommendation

- **Reuse** the scenarios above and the participant contract as the Elsa 3 acceptance suite, with the sample participant as a reference.
- **Do not port** the Elsa 4 runtime seams, private state or Studio components; they rest on Elsa 4's checkpoint and outbox model.
- **Decide** the Elsa 3 mechanism for each guarantee in section 2 in that effort, under its own program.

The [program charter](../program-goals/native-compensation.md#relationship-to-other-programs)'s Elsa 3 row points here.
