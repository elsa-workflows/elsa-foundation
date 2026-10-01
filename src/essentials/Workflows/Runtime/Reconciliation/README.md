# Elsa.Workflows.Runtime.Reconciliation

Startup reconciliation of the workflow artifacts a host ships: `WorkflowArtifactReconcilerStartupTask` activates them when the shell starts. It is a `[SingleNodeTask]`, so on a cluster the nodes run it one at a time (#2192). That is why it waits for the lock where the node-local reconcilers do not.

## Why the passes take turns

Two passes over one mounted set activate each artifact under the same activation id, so two nodes starting together would race calls for one activation on every artifact. Most of that race converges (#2274):

- the call that loses the slot's compare-and-swap completes the winner's activation instead of compensating it (#2251);
- two completions of one slot both succeed (#2265);
- each call holds a root-write lease of its own, so the winner's release no longer fails the loser (#2274).

Two windows remain, and both fail silently:

- **A call cancelled while its slot transition is in flight**, as on a node stopping during its pass, cannot tell the winner's transition from its own. It hands the slot back, and the activation the other node logged as live stops serving.
- **A call whose mint or preparation fails or is cancelled before the other call's transition lands** compensates the shared activation. When that lands after the other call has switched the projections on, the slot names an activation that serves nothing.

Nothing reports either one, and only a later pass repairs it. A third case is loud but wrong: two calls resuming an activation whose earlier attempt failed race on restoring its reference, and the loser reports the artifact rejected, and its dependents with it, although the other call activated it.

The Runtime [extension-point catalog](../EXTENSION_POINTS.md) describes the first two under `IWorkflowActivationCoordinator`. Switching the slot and the projections in one transaction (#2230) closes them; the passes can stop taking turns once that and the third case are dealt with.

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
