# Persistence modes

A persistence mode decides when a workflow run's state is committed to the runtime database, and therefore how much work a crash costs. Elsa offers three modes. You set a default for the host and can choose a different mode for any workflow; the workflow's choice wins.

| Mode | Commits | After a crash |
|---|---|---|
| `Durable` | After every activity | Resumes at the last completed activity |
| `Balanced` (default) | On suspend, fault, completion, end of burst, and every N activities | Activities since the last commit run again (at-least-once) |
| `Ephemeral` | On suspend, fault, cancellation, an incident or an alteration, on dispatching a child workflow, before an activity that is not safe to repeat, and at the end of a pass that does not complete the run, plus a completion record unless `RecordCompletion` is off | A run that completes in one pass with none of those earlier commits leaves only its completion record, or no trace with `RecordCompletion` off |

- **Durable** keeps the checkpoints an activity produces in memory while it runs and commits them together, once per activity. It costs the most database work and loses the least on a crash.
- **Balanced** keeps the checkpoints of a burst of work in memory and commits them together: when the run suspends, faults or completes, when the runtime finishes a pass over the run's queued work (the end of a burst), and after `MaxActivitiesPerCommit` activities (50 by default). It is the default for every workflow that does not choose a mode, on every host that does not configure one.
- **Ephemeral** commits when the run suspends, faults or is cancelled, when an incident is recorded or resolved, when the run is altered, when it dispatches a child workflow, before an activity that is not safe to repeat, when a pass ends without completing the run, and once when it completes. It has no other intermediate commits, however long the pass. A run that never suspends can still be committed before it completes, by an incident, an alteration, a child dispatch or the step before an activity that is not safe to repeat.

## What every mode commits

Some checkpoints are committed in every mode, because skipping them would lose work or leave the database telling a different story than the run:

- a suspension, and the bookmark the run waits on, so a resume always finds it;
- resuming from a bookmark;
- a fault and its incident;
- a cancellation;
- the dispatch of a child workflow;
- completion;
- the step before an activity that is not safe to repeat. Activities that call out to other systems (an HTTP endpoint, publishing an event, any custom activity not marked safe to repeat) are committed before they run, so a crash never repeats them silently. Marking an activity safe to repeat drops that commit, which is why publication warns when a [compensation scope](compensation.md#crashes-and-repeated-calls) step that has a compensation, or a compensation handler, is a leaf activity marked safe to repeat. Composite activities never get this warning.

There is one exception, which a host opts into. Under Ephemeral with `RecordCompletion` off, a run that completes without ever having been persisted writes no record at all. A run that already has a record, because it suspended, faulted or ran an activity that is not safe to repeat, always gets its completion committed, so no stored run is left looking suspended. A run whose completion something else is waiting for is also always recorded: a dispatched child, whose parent is told about the result, and any run that leaves work behind for after its completion.

## What "suspend" means for Ephemeral

For Ephemeral, a suspension is any stop that is not completion. A run that waits on a bookmark, a timer or a child workflow, or that pauses for any other reason, is committed when the runtime finishes its pass, so its queued work can resume it later. Only a run that goes from start to completion in one pass can skip its record.

## Choosing a mode

- **Durable** for human-in-the-loop workflows and for workflows with side effects you never want repeated after a crash, when you are willing to pay a commit per activity.
- **Balanced** for most workflows. After a crash, the activities since the last commit run again. Activities that are not safe to repeat are committed before they run in every mode, so Balanced repeats them no more often than Durable does.
- **Ephemeral** for high-volume, short-lived runs whose history you do not need. A run leaves no record only if it completes in one pass without any earlier commit: no suspension, cancellation, incident or alteration, no child dispatch and no activity that is not safe to repeat. Note that a long Ephemeral pass that meets none of its commit points keeps all its work in memory until it does: there is no intermediate commit to bound it.

## Setting the mode

On the host, list the `WorkflowsRuntimeCheckpointPersistence` feature in your shell configuration:

```json
"WorkflowsRuntimeCheckpointPersistence": {
  "Mode": "Balanced",
  "MaxActivitiesPerCommit": 50,
  "RecordCompletion": true
}
```

| Setting | Default | Meaning |
|---|---|---|
| `Mode` | `Balanced` | The mode of every workflow that does not choose one. |
| `MaxActivitiesPerCommit` | `50` | How many activities Balanced folds into one commit before an intermediate commit. Must be greater than zero, in every mode, because a workflow can choose Balanced on a host whose default is Durable. |
| `RecordCompletion` | `true` | Under Ephemeral, whether a run that completes without ever having been committed writes a completion record. Has no effect on Durable or Balanced. |

A shell that composes the runtime API gets the persistence modes without listing this feature, with these defaults. List it to change them.

For a single workflow, choose **Persistence mode** in the workflow's settings in Studio, or set `strategyOptions.persistenceMode` to `Durable`, `Balanced` or `Ephemeral` in the workflow definition. Leave it unset to use the host default.

- The workflow's choice wins over the host default, in both directions.
- The choice is part of the published workflow: changing it takes a new published version.
- A run keeps the mode it started with, even if the host default changes while it waits.
- A host that composes the runtime without the persistence modes cannot run any workflow: every run drains through them, Durable included, so the run is refused with an error that names `AddPersistenceModes()`. Shell hosts always have them.

## What you see afterwards

The workflow instance details report the run's `persistenceMode`, and the `inspectionGranularity` that follows from it: `activity-level` under Durable, where every activity's inspection record is exact, and `boundary-level` under Balanced and Ephemeral, where activities folded into one commit are recorded as of that commit.

With `RecordCompletion` off, a run that was never committed leaves no workflow, activity, inspection or value records, and its queued start is removed. As a result:

- it does not appear in the instance list, and reading its instance returns not found;
- starting a workflow again with the same execution identifier is not recognized as a duplicate after the first run completed;
- the run still leaves one released ownership row in the runtime's execution liveness table, as every run in every mode does.

---

[Edit this page on GitHub](https://github.com/elsa-workflows/elsa-foundation/edit/main/website/docs/essentials/persistence-modes.md).
