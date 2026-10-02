# Elsa.Workflows.Runtime.Reconciliation

Startup reconciliation of the workflow artifacts a host ships: `WorkflowArtifactReconcilerStartupTask` activates them when the shell starts. It is a `[SingleNodeTask]`, so on a cluster the nodes run it one at a time (#2192). That is why it waits for the lock where the node-local reconcilers do not.

## Why the passes take turns

Two passes over one mounted set activate each artifact under the same activation id, so two nodes starting together would race calls for one activation on every artifact. That race now converges:

- each call holds a root-write lease of its own, so the winner's release no longer fails the loser (#2274);
- a slot and its serving projections switch in one commit (#2230), and a call that stops short of its own switch discards the shared activation only while it does not serve. The call that loses the slot's compare-and-swap finds the activation serving and answers that it is already active (#2251).

The two windows that failed silently are closed by that one commit (#2230):

- **A call cancelled while its switch is in flight**, as on a node stopping during its pass, used to hand the slot back because it could not tell the winner's transition from its own. It now hands nothing back: its discard is refused once the activation serves, and a switch that commits stands.
- **A call whose mint or preparation fails or is cancelled** used to compensate the shared activation after the other call had switched it on. Its discard is now refused once the activation serves; one that runs before the other call's switch deletes the shared projections, and that switch then fails loudly, having moved nothing, unless the other call prepares after it, in which case that call's switch also makes the shared reference live again.

Two cases remain, and both are loud but wrong. Two calls resuming an activation whose earlier attempt failed race on restoring its reference, and the loser reports the artifact rejected, and its dependents with it, although the other call activated it. And a winner whose trigger observer fails reverts its activation, as that failure requires, after the loser has already reported it active. In each, one node reports a failure, but the other node's report is wrong until the next pass.

The Runtime [extension-point catalog](../EXTENSION_POINTS.md) points here from `IWorkflowActivationCoordinator`. The passes keep taking turns until those two cases are dealt with.

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

Catalogs: [`Elsa.Tasks/EXTENSION_POINTS.md`](../../../Tasks/EXTENSION_POINTS.md), [`Elsa.Locking.FileSystem/EXTENSION_POINTS.md`](../../../Locking/FileSystem/EXTENSION_POINTS.md).
