# Writes to one variable whose order is not defined

Several nodes can write the same variable: an output capture, or a Set, Merge or Reduce node. When the definition
fixes their order (steps of a Sequence, nodes on one Flowchart path, passes of a loop) or only one of them can run
(the arms of an If, the cases of a Switch, the branches after an exclusive gateway), the last write in that order is
the final value. When the definition does not fix their order, Elsa reports it and still runs the workflow.

Specification: [spec 266](../../specs/266-capture-concurrency/spec.md). Decision: [ADR 0048](../adr/0048-container-output-captures-resolve-through-producer-visible-variable-frames.md)
§4, §5 and its unordered-capture amendment.

## What happens at run time

One workflow instance never applies two writes at the same moment: its commits are serialized. For plain writes
(captures, and Set, Merge and Reduce as they work today) the **last successfully committed write wins**. Activity
start order, completion timestamps and the order completion notifications arrive in do not choose the winner.

That rule holds within one run. Two runs of the same definition can commit unordered writes in a different order and
end with different values. Merge and Reduce overwrite like Set; there is no combine step yet.

A step that reads a variable and writes it back (a read-modify-write) can meet a value another branch changed in the
meantime. That is a separate policy, [ADR 0092](../adr/0092-variable-reads-and-writes-share-one-versioned-lifecycle.md):
by default the write applies and a lost-update warning is recorded. If an update must never be lost (a counter, a
balance), set the node's lost-update mode to `Fault` (node setting `LostUpdateMode`, or
`[ActivityLostUpdateMode(LostUpdateMode.Fault)]` on the activity type): a write on a stale base then faults instead. See
`docs/runtime-variable-lifecycle.md` (spec 1221).

## The warning

| Where | Finding |
|---|---|
| Workflow draft validation | `ValueFlow/ConcurrentWrite`, severity `Warning`, path `$workflow/variables/{key}` (or `{scope}/variables/{key}`) |
| Graph activity draft validation and compilation | `activity.graph.unordered-variable-write`, severity `Warning`, metadata `validationType = ValueFlow/ConcurrentWrite` |

There is one finding per variable. It names every node that writes the variable without a defined order and says how
to make the result deterministic:

- order the branches (put the writes on one path, or after the join);
- capture into separate variables; or
- combine the branch results with one write after the join.

Promotion, publication and compilation do not block on it. Workflow executable compilation does not return it: API
clients get it from workflow draft validation (`ValueFlow/ConcurrentWrite`) before they publish.

Two outputs of one node that capture into the same variable get a separate warning, `ValueFlow/DuplicateCaptureTarget`
(graph: `activity.graph.duplicate-capture-target`). Their order is fixed, but only the last one is kept.

## Which writes count as unordered

Each structure declares how its children relate in time; the warning has no list of structure kinds of its own.

- **Unordered**: the branches of a Parallel; Flowchart paths that leave one node (through one port or through several
  ports, since any activity may select several outcomes); BPMN parallel, inclusive and event-based gateway branches,
  implicit splits, non-interrupting boundary paths, event subprocesses and compensation handlers; the runs of a
  non-sequential BPMN multi-instance.
- **Across a join**: a branch is ordered before the continuation only when the join waits for it. A Flowchart node
  with several inbound connections waits, except under the `merge` policy. A BPMN parallel or inclusive gateway waits;
  an exclusive gateway or an implicit join does not. A `firstWins` race leaves its losing branch running, so a join
  after the race does not order it. A Parallel with a threshold below its branch count completes while the other
  branches still run, and so does a Flowchart that uses the `firstWins` policy: writes of those branches stay
  unordered with what follows.
- **Loops that fork**: when a pass of a Flowchart or BPMN cycle forks off the cycle, the branch can still run while
  the next pass runs, so it is unordered with itself and with the cycle. In a Flowchart this includes a loop whose
  decision repeats through one port and leaves through another, because the decision may select both.
  Two branches of one pass that both lead back into the loop are unordered with each other too; a waiting join
  that closes the loop still follows both. When two such branches return into the loop separately, the loop's steps
  overlap themselves. A Flowchart refuses to start when a loop leads back into a node that has another incoming
  connection, so it runs a loop only through a node whose sole incoming connection is the way back; the
  multi-entry loop shapes above occur in BPMN.
- **A structure that declares nothing** is treated as unordered, and the warning says that it does not declare how
  its children are ordered.

## Limits

The analysis sees only declared writes. It does not see a script's `setVariable`, a C# activity writing through its
execution context, or writes inside sub-workflows and graph activities. No warning does not mean no write conflict.
A BPMN process with link events declares nothing, so its writes are reported as undeclared.
A `Break` that ends a Flowchart cancels the other paths' running activities, so they commit nothing afterwards.

Seeing the actual run order is not available yet: per-variable write history and the winning write
([#3290](https://github.com/elsa-workflows/elsa-foundation/issues/3290)), a commit-ordered journal and instance
comparison ([#3291](https://github.com/elsa-workflows/elsa-foundation/issues/3291)) and a run-time trace of overwrites
([#3292](https://github.com/elsa-workflows/elsa-foundation/issues/3292)) are separate work, specified in
[spec 1229](../../specs/1229-variable-lifecycle-inspection/spec.md). Until they ship, the
warning names the declared writers but gives no run-time evidence.
