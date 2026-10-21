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
- If a compensation fails, the scope runs it again, up to the scope's **Handler max attempts** (3 by default, counting every run). By default it runs again straight away. If every attempt fails, the scope waits for an operator: the compensation's incident stays open with "wait for intervention", the workflow keeps running, and the step is never reported as compensated. See [Act on a waiting recovery](#act-on-a-waiting-recovery).
- If a step cannot tell whether its effect happened (a lost response), it reports an uncertain outcome. The scope treats the step as possibly done and runs its compensation, which must work whether or not the effect exists. A step that has no compensation waits for an operator instead; it is never treated as if nothing happened. See [Uncertain outcomes](#uncertain-outcomes).

### Wait between attempts

A compensation that fails because a service is briefly unavailable often succeeds a little later. Three scope inputs make the scope wait before each automatic retry:

| Input | Default | Meaning |
|---|---|---|
| **Handler retry delay** (`handlerRetryDelay`) | `00:00:00` | The wait before the second attempt. Zero retries at once. At most 24 hours. |
| **Handler retry backoff multiplier** (`handlerRetryBackoffMultiplier`) | `2` | How much each further wait grows. At least 1; 1 keeps the wait fixed. |
| **Handler retry max delay** (`handlerRetryMaxDelay`) | `00:05:00` | The longest any one wait may be. At least the delay and at most 24 hours. |

After the n-th failed attempt the scope waits `min(max delay, delay × multiplier^(n − 1))`. With a delay of 30 seconds, a multiplier of 2, a max delay of 90 seconds and four attempts, the waits before attempts 2, 3 and 4 are 30, 60 and 90 seconds.

- The wait is a minimum. A due retry runs at the next sweep of the runtime's outbox, which runs every 10 seconds by default, so a 30-second wait can take up to 40 seconds.
- Every attempt sends the participant the same operation keys, and the wait survives a restart: the retry is committed with the failed attempt and runs once, after its due time. A retry that fell due while the process was down runs at the first sweep after it starts.
- Nothing else waits behind it. The rest of the workflow, an enclosing scope that receives handed-over work, and cancellation carry on while the retry waits; cancelling the run means the compensation never starts.
- While it waits there is no incident, so Studio offers no action and an operator cannot skip the wait. An operator's retry after every attempt failed always runs at once.
- Publication refuses a literal value outside these ranges. A value from an expression is brought into range when the scope uses it, and the attempt records that it was adjusted.
- The inputs are fixed when the workflow is published, like every scope input, so runs already in flight keep the timing they started with. The defaults keep the earlier behaviour, retrying at once.

## Crashes and repeated calls

Recovery survives a restart in every [persistence mode](persistence-modes.md). After a crash the scope carries on where its last commit left it and never runs a compensation that already completed again.

A step or compensation that was running when the process stopped runs again after the restart. Its effect may already have happened, so key every external effect on the activity's **operation key** (`OperationKey` on the activity context). It is the same on every run of that step or compensation: after a crash, after an automatic retry and after an operator retry. A compensation also gets `TargetOperationKey`, the operation key of the step it undoes. Activities that call out to other systems are committed before they run (see [persistence modes](persistence-modes.md)), so a repeat is never silent.

An activity marked safe to repeat (`ReplaySafe`) is not committed before it runs: after a crash, recovery may not know that its effect happened. That is fine for an activity with no effect outside the workflow, such as **Write Line**, but not for a step whose effect a compensation must undo, or for a compensation that makes a change of its own. So publication warns when a step that has a compensation, or a compensation, is a replay-safe activity. The warning does not block publishing: Studio lists it under **Warnings** in **Review & publish** and keeps **Publish** enabled. Composite activities such as a Sequence or a nested scope never warn, because their marking describes the container, not the work inside it. Unless the activity truly has no effect of its own, mark it as an external effect (the default for an activity without the marking).

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

## Act on a waiting recovery

A scope that is waiting shows **Needs action**: a compensation failed on every attempt, or a step's outcome is uncertain and it has no compensation. The compensation's row shows its attempts ("Attempt 3 of 3") and a link to the blocking incident. An operator acts on that incident in one of three ways, from Studio or over the API.

### In Studio

Open the run and the scope's **Steps**. When you may manage the run, the waiting row offers **Retry** and **Resolve…** (next to the step's state when an uncertain step without a compensation waits, next to the compensation's state otherwise), and the scope's header offers **Abandon recovery…**. The incident's details (**Issues** › **Open incident details**) offer the same actions: **Retry** for any incident that waits for an operator, and one button per decision the wait offers. A reader who may not manage the run sees the same view without the buttons.

Each action opens one confirmation dialog:

- **Target**: the scope, the step and its compensation, the attempt the retry would be (for example "Attempt 4 (limit 3 reached)"), the incident and the run.
- **Effect**: what happens, with only the lines that apply to that row. A retry runs the compensation again with the same operation key and says what recovery does next; a resolve says how the scope carries on; an abandon lists the steps that stay not undone and who decides next (the enclosing scope, or the run's incident strategy).
- **Outcome** (resolve only): **Effect absent or undone** or **Effect present, keep it**.
- **Reason**: optional for a retry, required for a resolve or an abandon; at most 500 characters, on one line. It is visible to everyone who can view the run, so put no secret in it.

**Confirm** stays disabled until the form is valid, and Escape cancels. After you confirm, the row reads **Retry submitted** (or **Resolve submitted**, **Abandon submitted**), then **Applying…** until the run shows the change, and the status line above the run says the result: "Retry applied.", "Resolve applied." or "Recovery abandoned.". If the action no longer applies, it says why and that nothing was changed; for example "Someone else already acted on this." when another operator acted first. Reloading the page while Studio waits reopens the same run and step and carries on waiting; it never submits the action again.

The scope's **Operator actions** list shows every retry and decision on it, newest first, with who acted, when and why, and a link to the incident; it stays after the scope completes.

### Over the API

Each action is an alteration plan submitted to `POST runtime/workflows/alteration-plans` with an `Idempotency-Key` header, naming the run in `"target": { "executionIds": ["<run>"] }`. Sending the same request twice with the same key returns the existing plan (`Replayed`) and applies nothing twice. Studio sends exactly these requests.

**Retry** runs the failed compensation again, once the cause is fixed:

```json
{ "kind": "RetryFaultedActivity", "schemaVersion": 1, "payload": { "sourceActivityExecutionId": "<the failed compensation run>", "reason": "Delivery service restored" } }
```

The retry resolves the incident and runs the compensation again with the same inputs and operation key. If it succeeds, recovery carries on with the remaining steps; if it fails, the scope waits again.

**Resolve** declares what happened to the effect without running anything, with a required reason (1 to 500 characters; it is visible to everyone who can view the run, so put no secret in it):

```json
{ "kind": "DecideIntervention", "schemaVersion": 1, "payload": { "incidentId": "<the waiting incident>", "decision": "compensation.resolve.effect-absent", "reason": "Refunded manually, ticket FIN-221" } }
```

| Decision | Meaning | On a failed compensation | On an uncertain step without a compensation |
|---|---|---|---|
| `compensation.resolve.effect-absent` | The effect does not exist: it never happened, or it was undone outside Elsa. | The step is settled; recovery continues with the next step. | The step failed with no effect; the steps before it are undone. |
| `compensation.resolve.effect-accepted` | The effect exists and the business keeps it. | The step is settled and kept; recovery continues. | The step's effect counts as done; the scope runs the next step. |

Either way the step reads **Resolved manually** and is never undone, confirmed or handed over afterwards. A later step that reads the result of a resolved step faults as for any missing value.

**Abandon** stops the recovery, with a required reason:

```json
{ "kind": "DecideIntervention", "schemaVersion": 1, "payload": { "incidentId": "<the waiting incident>", "decision": "compensation.abandon", "reason": "Order written off by finance" } }
```

No further compensation runs, every step keeps its state, and the scope faults with `compensation.scope.abandoned` (category `outcome-unknown`), so the run never completes as if recovery had finished. Inside another scope, the outer scope treats the abandoned scope as an uncertain step: it runs that step's compensation, or waits for an operator itself.

**Permissions and which runs.** Submitting needs the `workflows/runtime:manage` permission; without it the request returns 403 and creates no plan. An operator can act on any run of their tenant by id, whatever started it (an HTTP endpoint, a timer, a Studio test run or the REST API); another tenant's run is not found. Each named run is checked as run deletion checks it, so a host that restricts operators run by run can refuse one: that run's job fails `TargetNotFound`, as a missing run's does, and nothing is applied to it. The run details (`GET runtime/workflows/instances/{id}`) say whether the caller may act on that run in `permissions.canManage`.

**Refusals.** A plan's job fails with a code and changes nothing when the action no longer applies: `InterventionNotPending` (someone already retried, resolved or abandoned it), `NoBlockingIncident` (a retry after that), `DecisionNotOffered`, `RequesterNotRunning` or `SourceParentNotRunning` (the scope was cancelled), `WorkflowTerminal` (the run ended) or `InvalidDecideInterventionPayload` (a missing or invalid reason).

**History and journal.** Every action is recorded on the incident it resolved: the action (`OperatorRetry` or `OperatorDecision`), the decision, who submitted it, the reason and the time. While the scope is live, `GET runtime/workflows/instances/{run}/activity-executions/{scope}/state-view` returns its journal (view `compensation.journal`): each step with its status, attempts and incidents, what it waits on and which decisions it offers, its resolution, and the steps still to undo. A completed scope keeps no journal; the run's activities and incidents still show what happened.

## Nest scopes and run them in loops

A scope inside another scope's step hands its work to the outer scope when it finishes with `Done`. Its completed steps, and any it received from scopes inside it, become entries of the outer scope, which undoes them with their own compensations, operation keys and step results if a later step of the outer scope fails. Nothing is handed over across a reusable activity: a scope in a reusable activity's body keeps its entries.

- **Loops.** A scope in a For Each, While or For body hands over once per iteration. The outer scope undoes the newest iteration first.
- **Parallel branches.** Each branch's scope hands over when it finishes. A hand-over that arrives while the outer scope is already undoing is undone in that same recovery.
- **No enclosing scope.** A scope with nothing around it keeps its entries when it finishes; nothing is sent.
- **Keep the work.** Turn on the inner scope's `confirmOnComplete` input to confirm its entries instead of handing them over (see below).
- **Variables.** A compensation that can be handed over can use the step's result and workflow variables. Publishing refuses one that uses a variable of a Sequence or Flowchart between its scope and the outer scope, because that container has finished by the time the outer scope runs it.

A retry, after a crash or by an operator, never adds an entry twice.

## Confirm work that must not be undone

Add **Confirm** as a step, or inside a step, when the work so far must survive a later failure: a payment captured, a ticket issued. It confirms every entry the nearest scope could still undo, and later recovery skips them. Give its `scope` input the node id of an enclosing scope to confirm that scope's entries too. Confirm only acts while the scope runs its steps; inside a compensation, or once recovery started, it has no effect.

## Start recovery with Compensate

Add **Compensate** inside a step to undo the scope's work on purpose, for example when a fraud check fails. The scope stops the running step, records the Compensate as the cause, undoes the completed steps newest first, and finishes with `Compensated` (whatever `faultAfterCompensation` says). The `reason` input is recorded with the cause.

- The stopped step had started, so it may have had an effect: it is recorded as uncertain and its compensation runs first. If it has no compensation, the scope undoes the rest and then faults with `compensation.scope.uncertain-unhandled`, so an operator decides what to do with it.
- Give `scope` the node id of an outer scope to undo more: each scope in between undoes its own work, then the named scope recovers.
- Inside a compensation, or once recovery started, Compensate has no effect.

In Studio, the `scope` input of Confirm and Compensate is a list of the scopes that enclose the activity, nearest first; leave it on **Nearest enclosing scope** to target the nearest one. Publishing refuses a Confirm or Compensate without an enclosing scope, one whose `scope` names a scope that does not enclose it, and one placed in a compensation, and the editor shows the same message. A Compensate is also refused when its cascade would pass through a compensation of a scope on the way to its target. For a Confirm or Compensate inside an enclosing scope, a target given as an expression is checked when it runs: a Confirm whose target matches no enclosing scope faults with `runtime.ancestor-notification.target-not-found`; a Compensate whose target matches no enclosing scope makes every enclosing scope undo its work in turn, and the outermost one then faults.

## Cancellation

Cancelling a run never starts recovery. A scope that is cancelled while it runs its steps, undoes them or waits for an operator runs no further compensation, and its journal stays readable in the run view. A compensation that was already running when the cancellation arrived may finish.

## Which steps report failures

| Step activity | A failure inside it |
|---|---|
| A single activity, or a scope | reaches the scope directly |
| Sequence, For Each, While, For | reaches the scope through the composite, which is stopped |
| Flowchart | the Flowchart handles it; when the Flowchart faults, the scope sees that |
| Sequence inside a Flowchart | stops at the Flowchart: the run faults and nothing is undone (known limit) |

## Inspect a run

Open the run and the scope's **Steps**. Each row shows the forward state (`Completed`, `Uncertain`, `Faulted`, `Not run`) and the recovery state (`Can be undone` while the scope still runs, `Kept` once it has ended without undoing the step, `Waiting to undo` while recovery runs, `Undoing`, `Retry scheduled` with its attempt and the time after which it runs while an automatic retry waits (then `Retry due` once that time has passed), `Retrying` when a compensation runs again after a failed attempt, `Compensated` with its order, `Undo failed`, `Needs action` with its attempts, `Resolved: effect absent` or `Resolved: effect kept` after an operator resolved it, `Not undone (recovery abandoned)`, `Not applicable`). A banner names the step whose failure started recovery, or the Compensate that asked for it; the failure's message shows only to a reader who may see captured values. While recovery runs, a line lists the steps still to undo, in order. Select a state to open that occurrence; a compensation's activity details link to the occurrence it undid, and the timeline tags compensation runs. The address keeps the selected occurrence (`?selected=`), so a reload or a shared link opens it again.

While a scope is live, the view comes from its journal and says **From the scope's journal**. A completed scope keeps no journal, so its view is derived from the run's activities and incidents and says so; the states are the same. An abandoned scope shows **Abandoned**.

Nesting, confirmation and cancellation add these states:

- `Confirmed`, with a link to the Confirm that confirmed the step.
- `Compensable by {scope}` on a completed inner scope: its steps were handed to that enclosing scope, which undoes them if it recovers.
- **Handed over by inner scopes**, a list under the outer scope's own steps, with each entry's recovery state and a link to the inner scope occurrence (and loop iteration) it came from.
- `Cancelled — nothing undone` on a cancelled scope that undid no step.

A scope that ran more than once, in a loop or because its parent ran again, has an **Occurrence** list to choose which run to show.

## Current limits

These behaviours are planned and not yet available:

- a compensation that can be handed over and reads a container's variable from a script expression is not checked at publication; it fails when it runs and waits for an operator;
- a failure inside a Sequence inside a Flowchart step does not reach the scope.
