# Data Model: Runtime Operational Recovery And Post-Commit Outbox

## OperationalState

Typed operational continuation/coordination state for a workflow execution. It groups execution lease, heartbeat, drain/quiescence, interrupted execution marker, and pending post-commit intent references.

## RuntimeExecutionLease / RuntimeHeartbeat

Provider-neutral lease and heartbeat records used by recovery scanners to identify ownership loss or stale execution agents.

## RuntimeDrainState

Control-plane drain/quiescence marker that can stop accepting new work at a safe boundary without corrupting active execution state.

## InterruptedExecutionState

Marker for interrupted execution with the last checkpoint reference and affected activity execution IDs.

## RuntimePostCommitOutboxItem

Durable delivery state for a post-commit intent. It tracks pending, delivering, delivered, retryable failure, final failure, and cancellation states.

## RuntimeRecoveryCandidate

Provider-facing recovery scanner result. It can request requeue from the last checkpoint without implying a workflow/activity domain retry.

## RuntimeRecoveryScanRequest

Provider-facing recovery scanner input. It carries separate lease and heartbeat timeouts so providers can detect lost leases and stale heartbeats without hard-coded thresholds.

## RuntimePostCommitOutboxDeliveryResult

Provider-facing delivery outcome for a post-commit outbox item. Failure messages are only valid for failed outcomes.

## RuntimeDomainRetryDecision

Explicit workflow/activity retry decision boundary, separate from operational recovery.
