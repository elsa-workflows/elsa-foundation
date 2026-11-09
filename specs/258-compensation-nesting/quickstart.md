# Quickstart: proving M3

All journeys use the shared participant from spec 251 (operation-key deduplication, tombstones) and run in Durable, Balanced and Ephemeral.

## Journeys

| ID | Shape | Proves |
|---|---|---|
| N1 | Outer scope: [inner scope (A, B), Charge (rejected)] | Hand-over on `Done`; recovery runs B⁻¹ then A⁻¹ with their own keys and forward results (story 1). |
| N2 | Three levels: outer [middle [inner (A)], X (rejected)] | Transitive hand-over keeps the first origin. |
| N3 | Inner scope without enclosing scope | Entries end kept; nothing sent. |
| L1 | Outer [ForEach(3) { scope (Reserve item) }, Ship (rejected)] | One entry per iteration; undo newest iteration first (story 2). |
| L2 | L1 with a crash re-attempt after the first iteration's forward effect | The crash re-attempt adds no entry. An operator retry of a handed-over handler is covered separately (FR-304 journey). |
| B1 | Outer [Parallel { branch: scope (A), branch: scope (B) }, X (rejected)] | Each branch's hand-over journaled; both undone. |
| B2 | Outer recovering while a Parallel branch's scope completes | Late hand-over compensated in the current recovery. |
| C1 | Scope [A, Confirm, B (rejected)] | A confirmed and kept, nothing undone for A (story 3). |
| C2 | Outer [inner (A, Confirm(scope: outer)), X (rejected)] | Named Confirm reaches the outer scope. |
| C3 | Inner with `confirmOnComplete` | No hand-over. |
| C4 | Confirm targeting a non-enclosing scope, and across a reusable boundary | Refused at publication. |
| E1 | Scope [A, Flowchart { check → Compensate }] | Explicit cause, running step stopped, A undone (story 4). |
| E2 | Outer [inner [A, Compensate(scope: outer)]] | Cascade: inner compensates, outer recovers. |
| E3 | Compensate stops a started step that has no handler | Scope faults `uncertain-unhandled` after recovery. |
| X1 | Cancel during forward, during recovery, while waiting | No handler runs; journal readable (story 5). |
| S1 | Steps that are Sequence, ForEach, While, For with a faulting leaf | Fault reaches the scope (story 6). |
| S2 | Flowchart steps; Sequence inside Flowchart | Unchanged; in the nested case the fault never reaches the scope, the run faults and nothing is undone (known limit). |
| S3 | Outer [P, inner (faultAfterCompensation) [Sequence { fail }], Ship] | The inner scope faults on the descendant fault; the outer scope receives it as a child fault and undoes P. |

## Restart injection points

1. After an inner scope's completion and hand-over commit, before the outer scope journals it (N1).
2. Between two handed-over handlers (N1, L1).
3. After Compensate's notification commits, before the step's cancellation lands (E1).
4. After a descendant fault is delivered, before its absorption commits (S1).

## Workbench owned process

N1 + C1 over REST: publish an order workflow with a nested reservation scope and a Confirm, run it, reject the last step, kill the process after the first handed-over handler's effect, restart, and assert one undo per unconfirmed effect.

## Negative controls (SC-305, run by hand, reverted)

1. Skip the hand-over on `Done`: N1 must fail.
2. Drop the `receivedHandOvers` check: the repeat-delivery test must fail.
3. Treat `Confirmed` as eligible: C1 must fail.
4. Compensate on cancellation: X1 must fail.
5. Disable descendant-fault delivery: S1 must fail.
6. Put routing keys in command metadata only: the operator-retry test of a handed-over handler must fail.

### Results (T317, run by hand on the full Elsa.Activities.Compensation.Tests suite of 166 tests, each reverted)

| Control | Mutation | Result |
|---|---|---|
| 1 | `ContinueForward` sends no hand-over | 32 fail: N1, N2, L1, L2, B1, X1, P1, P2, the operator-retry journey, and the two hand-over callback tests |
| 2 | `OnHandOver` drops the `receivedHandOvers` check (also with the per-origin check dropped) | 1 fails: the repeat-delivery callback test. The runtime's own work-item idempotency keeps P1 green |
| 3 | `NextEligible` treats `Confirmed` as eligible | 7 fail: C1, C2 and the confirmed-entries callback test |
| 4 | Compensate on cancellation | Not runnable: the runtime cancels a scope's subtree without calling the scope (research R7), so there is no code to remove. X1 pins that no handler runs |
| 5 | `OnDescendantFaultedAsync` returns `Defer` | 16 fail: S1 (all four composites, three modes), P4 and the descendant-fault callback test |
| 6 | Handler routing keys in command metadata only, not provenance | 22 fail, among them the operator retry of a handed-over handler in every mode |
