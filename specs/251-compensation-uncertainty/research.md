# Research: uncertain effects and resumable compensation (M2)

Citations are to `main` plus the M1 stack as of 20 October 2026.

## R1 — Where the operation key comes from

- **Decision:** add `OperationKey` and `TargetOperationKey` to the activity execution, start and resume contexts. The runtime resolves them when it builds the context:
  1. the provenance metadata `runtime.operationKey` / `runtime.targetOperationKey`, which a structural parent assigns when it schedules the child;
  2. otherwise `Attempt.FirstAttemptActivityExecutionId`;
  3. otherwise the invocation id.
- **Rationale:**
  - The invocation id is already stable across crash re-attempts. `ActivityAttemptActivationClaimer` re-attempts with the same invocation id.
  - The invocation id is not stable across a reschedule, which mints a new execution (`RescheduleActivityAlterationHandler`), or across an activity-boundary retry.
  - Scheduling metadata is copied by both of those paths: the reschedule handler's copy of the source's scheduling provenance metadata, and the provenance copy in `WorkflowRetryActivityBoundarySchedulerWorkHandler`. So a key the parent assigns survives every retry path.
  - First-attempt lineage gives activities without an assigned key a sensible default.
- **Alternatives considered:**
  - A compensation-only key carried in `compensation.*` metadata. Rejected by OD6, because no other activity could use it.
  - Deriving the key from the node id plus the iteration. Rejected, because two occurrences of the same node in one scope run would share it.

## R2 — How the scope learns that an outcome is unknown

- **Decision:** the reserved fault category `outcome-unknown` (already named in spec 250 `contracts/semantics.md`) on `ActivityFault`. `ActivityChildFaultedContext` gains `FaultCode` and `FaultCategory`, filled from the committed child state's normalized fault.
- **Rationale:**
  - `ActivityFault.Category` is already persisted on the normalized fault (`ActivityFaultProjection`).
  - The child-fault callback is built where the child's committed state is in scope, but today it drops the fault (`WorkflowParentActivityCompletionSchedulerWorkHandler`).
  - No new result type, and no new channel between the activity and its parent.
- **Alternatives considered:**
  - A distinct "uncertain" completion outcome. Rejected: it would complete the activity, so incident evidence and the operator retry path would be lost.
  - A parent notification from the activity. Rejected: only structural children can send one.

## R3 — How "wait for intervention" is expressed

- **Decision:** a new seam, `RequestChildFaultIntervention(incidentId, reason, metadata)`, sits next to `RequestChildFaultAbsorption`.
  - Its validation is the same: only during a child-fault evaluation, only for that evaluation's incident, with a `Defer` continuation, and at most once.
  - It is mutually exclusive with absorption.
  - In the same checkpoint, it leaves the incident `Blocking` and sets the outcome `WaitForIntervention` with the system source `StructuralIntervention`. It does not cancel the child's subtree.
  - Absorption also starts copying its request metadata into the outcome.
- **Rationale:**
  - A structural continuation can only complete, defer, fault or cancel (`RuntimeStructuralContinuation`), so a bookmark wait is impossible.
  - Both incident-strategy observers skip incidents that already have an outcome, so a staged outcome keeps the default `Fault` strategy from faulting the run. That is OD2: the run stays running.
  - `WaitForIntervention` already exists as an action kind, with the same meaning for poisoned scheduler work.
- **Alternatives considered:**
  - A scope-level bookmark. Impossible for a structural activity.
  - Faulting the run and relying on a terminal-run retry. Rejected: it contradicts OD2, and retrying a terminal run is out of scope.

## R4 — The operator retry (#2747 core)

- **Decision:** a new alteration kind, `RetryFaultedActivity/1`, with payload `{ sourceActivityExecutionId, reason? }`. It reuses `RescheduleActivity`'s successor and supersession construction, with three differences:
  1. it requires a `Faulted` source in a running run, and at least one blocking incident on that source;
  2. it resolves those incidents in the same alteration checkpoint, with action kind `OperatorRetry`, system source `OperatorRetryAlteration`, and the submitter, job and reason in metadata;
  3. the successor keeps the source's scheduling cause, so a retried compensation handler is still recognised as one. Its metadata adds `runtime.retryOfActivityExecutionId`.

  Idempotency, authorization (`WorkflowRuntimeManage`), fencing and captured-concurrency checks come from the alteration substrate unchanged.
- **Rationale:**
  - `RescheduleActivity` already builds a superseding successor with the source's inputs, parent, scheduling id, metadata and pinned node. It refuses only because of the blocking incident.
  - #2747 proposes exactly this shape: "retry as an alteration, not as an incident mutation".
  - Alteration checkpoints cannot write incidents yet, so `WorkflowAlterationRuntimeCheckpointStateBuilder` gains `AddIncident`.
- **Alternatives considered:**
  - Extending `RescheduleActivity` with a flag. Rejected: it would change a published kind's semantics, which ADR 0049 forbids without a new version.
  - An incident-resolution endpoint. Rejected by #1015.
- **To verify in implementation:**
  - how the supersession planner reports the superseded child to its structural parent, so the scope does not treat the superseded handler as a new fault;
  - how the parent sees the successor's completion. It must arrive as an ordinary child completion that carries `compensation.*` metadata.

## R5 — Scope state version 2

- **Decision:** add phase `AwaitingIntervention`, entry statuses `Uncertain` and `Failed`, and these entry fields:
  - `forwardOutcome`;
  - `operationKey`;
  - `handlerAttempts[]`, which replaces `handlerActivityExecutionIds`;
  - `interventionIncidentId`.

  The cause gains kind `uncertain`. The persister reads version 1 through an upcaster and writes only version 2.
- **Rationale:**
  - Spec 250 `data-model.md` already reserved these statuses and the attempt list.
  - The persister currently throws on any version other than 1, so an upcaster is required for M1 runs in flight (§E2.6.1).
- **Alternatives considered:** keeping version 1 and adding optional fields. Rejected: version-1 readers would silently drop M2 statuses on downgrade, and the spec forbids downgrade anyway.

## R6 — Handler retries

- **Decision:** the scope input `handlerMaxAttempts` (default 3, minimum 1).
  - A faulted handler with attempts left is absorbed. The same handler node is scheduled again with the same scheduling id and keys, in one commit.
  - When the attempts are exhausted, the scope requests intervention.
  - Operator retries count against the same total.
- **Rationale:**
  - Immediate retries need no timer (OD7).
  - Re-scheduling from the forward occurrence keeps causal result binding (spec 250 FR-010).
- **Alternatives considered:**
  - Using the activity-boundary retry. Rejected: it only applies to boundary scopes.
  - A per-handler retry policy. Deferred: it can be added later as an override.

## R7 — Studio derivation

- **Decision:** keep deriving from run data (spec 250 FR-019).
  - **Uncertain:** the step's absorbed or waiting incident carries `compensation.forwardOutcome=unknown` in its resolution outcome metadata.
  - **NeedsAction:** a blocking incident with source `StructuralIntervention`.
  - **Attempts:** handler executions of the row's node under the scope occurrence. That includes successors from operator retries, which keep the `compensation` scheduling cause.
- **Rationale:** no new endpoint, and incident outcome metadata is not behind the sensitive-values gate (`IncidentResolutionOutcomeView.Metadata`).
- **Bug found:** `IncidentStateSummary.resolutionAction` does not exist on the wire. The API sends `resolutionOutcome {actionKind, systemSource, metadata}`, so the incident panel's "Resolution action" row is always empty. It is fixed here (FR-218).
