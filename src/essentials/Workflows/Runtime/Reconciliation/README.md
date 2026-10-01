# Elsa.Workflows.Runtime.Reconciliation

Startup reconciliation of the workflow artifacts a host ships: `WorkflowArtifactReconcilerStartupTask` activates them when the shell starts. It is a `[SingleNodeTask]`, so on a cluster the nodes run it one at a time (#2192). Concurrent activations of one artifact are not shown to converge, which is why it waits for the lock where the node-local reconcilers do not.

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
