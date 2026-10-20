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
- If a compensation itself fails, the failure stays an open incident and the scope does not finish; the workflow's incident strategy decides what happens next. The step is never reported as compensated.

## Crashes and repeated calls

Recovery survives a restart in every [persistence mode](persistence-modes.md). After a crash the scope carries on where its last commit left it and never runs a compensation that already completed again.

A compensation that was running when the process stopped runs again after the restart. Its effect may already have happened, so write participants to be idempotent: the activity's `InvocationId` is the same on the repeated call, and a participant that keys its effect on it applies the effect once. Activities that call out to other systems are committed before they run (see [persistence modes](persistence-modes.md)), so the repeat is never silent.

## Pass data from a step to its compensation

A compensation usually needs something the step produced: the reservation id to release. Capture the step's output into a workflow variable and bind the compensation's input to that variable. The compensation reads the value the step wrote, and a republished workflow does not change the steps or compensations of a run already in flight: a run always uses the version it started on.

## Inspect a run

Open the run and the scope's **Steps**. Each row shows the forward state (`Completed`, `Faulted`, `Not run`) and the recovery state (`Can be undone` while the scope still runs, `Kept` once it has ended without undoing the step, `Waiting to undo` while recovery runs, `Compensated` with its order, `Undo failed`, `Not applicable`). A banner names the step whose failure started recovery. Select a state to open that occurrence; a compensation's activity details link to the occurrence it undid, and the timeline tags compensation runs.

## Current limits

This is the first release of compensation scopes. These behaviours are planned and not yet available:

- a failed compensation waits for an operator decision instead of staying an incident, with handler retry policies;
- undoing a scope that already completed, a scope nested in another scope, and loops;
- compensation on cancellation;
- a warning when a step or compensation is marked safe to repeat, so its effect may not be committed before it runs.
