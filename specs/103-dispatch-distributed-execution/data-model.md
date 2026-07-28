# Data Model: Execute DispatchWorkflow Across Distributed Nodes

## Distributed dispatch node

- `NodeId`: nonblank configured node identity.
- `Capabilities`: actor-provider and command-transport capabilities registered in the host.
- `PersistenceProfile`: in-memory, durable single-node Groundwork, or distributed Groundwork.

Two logical nodes in tests share durable Groundwork state but use distinct `NodeId` values.

## Durable command transport item

- `TransportItemId`: provider identity for forwarded workflow command work.
- `WorkflowExecutionId`: deterministic child execution identity reserved by DispatchWorkflow.
- `OwningNodeId`: current placement/lease owner.
- `LeaseVersion` or equivalent fence: provider-owned stale-write guard.
- `Status`: pending, claimed/forwarded, completed/acknowledged, or abandoned according to the existing distributed provider model.

The transport item references existing workflow start command state; it does not add DispatchWorkflow activity inputs.

## Placement lease

- `NodeId`: current claimant.
- `ExpiresAt` or provider-defined lease boundary.
- `Fence`: monotonically changing ownership evidence used by checkpoint writes.

Stale holders cannot write child execution checkpoints after ownership changes.

## Distributed readiness report

- `Mode`: `InMemoryDevelopment`, `DurableSingleNodeGroundwork`, or `DistributedGroundwork`.
- `DurableRuntimePersistence`: whether checkpoint/outbox persistence survives process restart.
- `DistributedActorProvider`: whether distributed actor provider/transport is configured.
- `Warnings`: safe operational messages; no workflow payloads, raw inputs, secrets, or stack traces.

## State transitions

```text
Parent node A commits dispatch checkpoint
  -> child-start post-commit intent deliverable
  -> workflow start dispatcher returns local accepted OR durable distributed forwarding
  -> node B claims transport item
  -> node B materializes child execution under fence
  -> duplicate/stale delivery observes existing child or rejected fence
```

Restart after any durable step resumes from the same dispatch ID, child execution ID, and transport/fence state.
