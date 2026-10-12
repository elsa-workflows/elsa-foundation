# Elsa.Workflows.Runtime.Reconciliation

Startup reconciliation of the workflow artifacts a host ships: `WorkflowArtifactReconcilerStartupTask` activates them when the shell starts. It is a `[SingleNodeTask]`, so on a cluster the nodes run it one at a time (#2192). That is why it waits for the lock where the node-local reconcilers do not.

## Why the passes take turns

Two passes over one mounted set activate each artifact under the same activation id, so two nodes starting together race calls for one activation on every artifact. Each call holds a root-write lease of its own (#2274), and every slot move is one commit of `IWorkflowActivationSwitch`, whose rules keep such a race from leaving a slot wrong without a word; its documentation describes them.

Three cases remain, and each is loud but wrong: one node reports an outcome that the other node's contradicts until the next pass.

1. **A resume race.** Two calls resuming an activation whose earlier attempt failed race to restore its source reference. The one whose compare-and-swap loses reports the artifact rejected, and its dependents with it, although the other call activated it.
2. **A revert after a report.** A winner whose trigger observer fails reverts its activation, as that failure requires, after the loser has already reported it active.
3. **A discard before a switch.** A loser whose mint or preparation fails discards the shared activation before the winner's switch. The winner's switch then finds nothing to switch on and fails, which rejects the artifact and its dependents, although the slot is left as it was.

The Runtime [extension-point catalog](../../Elsa.Workflows.Runtime/EXTENSION_POINTS.md) points here from `IWorkflowActivationCoordinator`. The passes keep taking turns until those three cases are dealt with.

## Starting a node that waits

`[SingleNodeTask]` waits, then runs; it never skips because another node ran. That has consequences for startup:

- **N nodes starting together wait in turn.** Each holds the lock for the length of its own pass, so the last node starts after N passes, not one.
- **A hung but live peer holds the lock.** A database lock is released when its connection closes, so a crashed node frees it, but a node that is alive and stuck inside its pass does not.
- **The wait ends in a failed shell start.** The wait is bounded by the locking feature's `LockAcquisitionTimeoutMinutes` (10 by default). When it runs out the task fails with a `TimeoutException` naming the lock, and the shell's start fails with it. The bound is shared with every other lock of that provider; single-node startup tasks have no bound of their own.
- **`Foundation.Host` does not retry eager activation.** A shell whose start failed stays failed until it is reactivated; an operator or an orchestrator restart does that, the host does not.

Raise `LockAcquisitionTimeoutMinutes` when one pass legitimately takes longer than the timeout.

## What the lock covers

The lock is only as wide as its provider:

- `DatabaseDistributedLocking` serialises every node that reaches the same database.
- `FileSystemDistributedLocking` serialises the processes that share its folder. A host that joined a cluster through a durable membership provider refuses the unconfigured default folder at shell start; a configured folder is trusted to be shared.
- **Two nodes on one database with in-process (non-durable) membership get no refusal.** Nothing tells the file-system lock that the nodes exist, so it accepts its default node-local folder, and the two nodes are serialised only if they share a lock that spans them: compose `DatabaseDistributedLocking`, or point both at one shared `LocksFolderPath`.

Catalogs: [`Elsa.Tasks/EXTENSION_POINTS.md`](../../../../Tasks/Elsa.Tasks/EXTENSION_POINTS.md), [`Elsa.Locking.FileSystem/EXTENSION_POINTS.md`](../../../../Locking/FileSystem/EXTENSION_POINTS.md).
