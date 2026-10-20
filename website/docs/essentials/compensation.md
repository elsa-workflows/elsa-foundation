# Undo completed steps with a compensation scope

A compensation scope runs a list of named steps in order. Each step is a forward activity and, optionally, a compensation: the activity that undoes it. When a step fails, the scope undoes the steps that already completed, newest first, and finishes with the outcome `Compensated`. Use it when a workflow makes changes in other systems that cannot share a transaction: reserve inventory, reserve a delivery slot, then confirm the order; if the confirmation is rejected, cancel the delivery and release the inventory.

The scope is part of the `ActivitiesCompensation` feature, which the Workbench enables by default.

## Author a scope in Studio

Add **Compensation Scope** to a workflow and open its **Steps**. The designer shows one row per step, with the forward activity on the left and its compensation on the right:

| # | Step | Compensation |
|---|---|---|
| 1 | Reserve inventory | Release inventory |
| 2 | Reserve delivery | Cancel delivery |
| 3 | Confirm order | none |

- **Add step** asks for the forward activity; **Add compensation** asks for the activity that undoes that step.
- Each step needs a unique name. The name identifies the step in the run view and in the scope's journal, so renaming a step in a new version does not change runs already in flight.
- The arrow keys move between rows; Tab moves between the cells of a row. Deleting a step is undone with the editor's undo.
- A step without a compensation is never undone. Leave it empty for steps with nothing to undo, such as a final confirmation.

The scope's `faultAfterCompensation` input decides how it ends after undoing its steps: off (the default) finishes with the outcome `Compensated` and the workflow continues; on re-raises the step's failure after the compensations ran.

## What runs, and when

- Steps run in order. When a step completes, the scope records it in its journal and schedules the next step in one commit.
- When a step faults, the scope absorbs the fault and runs the compensations of the completed steps, newest first. The failed step's own compensation does not run: the step never completed.
- If every step completes, the scope finishes with the outcome `Done` and no compensation runs.
- If a compensation fails, the scope runs it again straight away, up to the scope's **Handler max attempts** (3 by default, counting every run). If every attempt fails, the scope waits for an operator: the compensation's incident stays open with "wait for intervention", the workflow keeps running, and the step is never reported as compensated. See [Resume a waiting recovery](#resume-a-waiting-recovery).
- If a step cannot tell whether its effect happened (a lost response), it reports an uncertain outcome. The scope treats the step as possibly done and runs its compensation, which must work whether or not the effect exists. A step that has no compensation waits for an operator instead; it is never treated as if nothing happened. See [Uncertain outcomes](#uncertain-outcomes).

## Crashes and repeated calls

Recovery survives a restart in every [persistence mode](persistence-modes.md). After a crash the scope carries on where its last commit left it and never runs a compensation that already completed again.

A step or compensation that was running when the process stopped runs again after the restart. Its effect may already have happened, so key every external effect on the activity's **operation key** (`OperationKey` on the activity context). It is the same on every run of that step or compensation: after a crash, after an automatic retry and after an operator retry. A compensation also gets `TargetOperationKey`, the operation key of the step it undoes. Activities that call out to other systems are committed before they run (see [persistence modes](persistence-modes.md)), so a repeat is never silent.

### What a participant must do

A system that a compensation scope calls is safe when it:

1. applies a step's effect at most once per operation key, and returns the original result when the same key comes again;
2. undoes the effect for a compensation's target key when it exists;
3. when it does not exist yet, remembers the target key and refuses a later request for it, so an effect that arrives late cannot slip in after its undo;
4. treats a repeated undo for the same compensation key as done;
5. reports an uncertain outcome only when it really cannot tell what happened.

Elsa does not provide exactly-once delivery or a reconciliation framework; these rules are what make repeats harmless.

## Pass data from a step to its compensation

A compensation usually needs something the step produced: the reservation id to release. Capture the step's output into a workflow variable and bind the compensation's input to that variable. The compensation reads the value the step wrote, and a republished workflow does not change the steps or compensations of a run already in flight: a run always uses the version it started on.

## Uncertain outcomes

When a call times out or its response is lost, the activity cannot tell whether the effect happened. Report that by faulting with the category `outcome-unknown`:

```csharp
return ActivityTransition.Fault<ReservationResult>(new ActivityFault("delivery.timeout", "No response from the delivery service.", category: "outcome-unknown"));
```

The scope records the step as **Uncertain** and starts recovery with that step included. Its compensation gets the step's operation key as `TargetOperationKey` and undoes the effect if it is there (rule 3 above covers the case where it is not there yet).

## Resume a waiting recovery

A scope that is waiting shows **Needs action**, and the compensation's row shows its attempts ("Attempt 3 of 3") and a link to the blocking incident. Once the cause is fixed, an operator with runtime-manage permission retries that compensation by submitting a `RetryFaultedActivity/1` alteration for the failed run of it:

```json
{ "kind": "RetryFaultedActivity", "schemaVersion": 1, "payload": { "sourceActivityExecutionId": "<the failed compensation run>", "reason": "Delivery service restored" } }
```

The retry resolves the incident and runs the compensation again with the same inputs and operation key. If it succeeds, recovery carries on with the remaining steps; if it fails, the scope waits again. Sending the same request twice (same `Idempotency-Key`) has no further effect. A Studio button for this follows in a later release.

## Inspect a run

Open the run and the scope's **Steps**. Each row shows the forward state (`Completed`, `Uncertain`, `Faulted`, `Not run`) and the recovery state (`Can be undone` while the scope still runs, `Kept` once it has ended without undoing the step, `Waiting to undo` while recovery runs, `Compensated` with its order, `Undo failed`, `Needs action` with its attempts, `Not applicable`). A banner names the step whose failure started recovery. Select a state to open that occurrence; a compensation's activity details link to the occurrence it undid, and the timeline tags compensation runs.

## Current limits

This is the first release of compensation scopes. These behaviours are planned and not yet available:

- a delay between automatic retries of a failed compensation, and a Studio button to retry it;
- undoing a scope that already completed, a scope nested in another scope, and loops;
- compensation on cancellation;
- a warning when a step or compensation is marked safe to repeat, so its effect may not be committed before it runs.
