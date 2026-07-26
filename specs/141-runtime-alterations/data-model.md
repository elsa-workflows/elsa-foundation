# Data Model: Durable Runtime Alterations

## Stable alteration identity

```csharp
public sealed record WorkflowAlterationDescriptor(
    string Kind,
    int SchemaVersion,
    string DisplayName,
    string? Description);

public sealed record WorkflowAlterationEnvelope(
    string Kind,
    int SchemaVersion,
    JsonElement Payload);
```

Rules:

- Built-in kinds are exact ordinal values `CancelWorkflow`, `ModifyVariable`, `ScheduleActivity`,
  `RescheduleActivity`, and `Migrate`, schema version `1`.
- Built-in bare names are reserved. Custom kinds are dotted/namespaced.
- Schema version is a positive integer selected exactly; no latest-version inference.
- Envelopes preserve submitted order.
- Only the protected plan payload stores envelopes; plan/job read views expose descriptor identity
  and ordinal, never payload.

## AlterationPlanState

| Field | Type | Rules |
|---|---|---|
| `PlanId` | string | Server-generated deterministic durable identity |
| `TenantPartition` | string | Sealed authorization partition |
| `AuthorityScope` | structured value | Immutable execution-authority constraints |
| `SubmittedBy` | operator provenance | Safe subject/display/correlation evidence |
| `IdempotencyKeyHash` | string | Tenant-scoped lookup identity |
| `CanonicalRequestHash` | string | Conflict comparison; excludes ciphertext randomness |
| `ProtectedPayload` | protected value/reference | Never returned by API reads |
| `TargetSelectorSummary` | safe structured summary | Redacted selector facts |
| `Status` | AlterationPlanStatus | Closed transition graph below |
| `CaptureCursor` | string? | Opaque immutable-scan continuation |
| `CapturedSoFar` | long | Durable capture progress, including explicit missing targets |
| `TargetCount` | long | Zero until sealed; immutable after seal |
| `SucceededJobCount` | long | Reconciled |
| `FailedJobCount` | long | Reconciled |
| `CancelledJobCount` | long | Reconciled |
| `CreatedAt` | timestamp | Required |
| `SealedAt` | timestamp? | Required after seal |
| `StartedAt` | timestamp? | First claimed job |
| `CompletedAt` | timestamp? | Required when terminal |
| `CancellationRequestedAt` | timestamp? | Cooperative cancellation |
| `SafeFailure` | typed safe failure? | Capture/orchestration terminal failure only |
| `Revision` | long | Optimistic concurrency |

### AlterationPlanStatus

```text
CapturingTargets
  -> Queued                         capture sealed with targets
  -> Completed                      capture sealed with zero targets
  -> Cancelling -> Cancelled        cancelled before seal
  -> Failed                         non-retryable capture failure

Queued
  -> Running                        first job claimed
  -> Cancelling -> Cancelled        no job entered actor

Running
  -> Completed                      every job succeeded
  -> CompletedWithFailures          every job terminal; at least one failed
  -> Cancelling -> Cancelled        pending jobs cancelled; running jobs settled
  -> Failed                         non-retryable orchestration failure
```

`Completed`, `CompletedWithFailures`, `Failed`, and `Cancelled` are terminal. A terminal sealed plan
satisfies:

```text
TargetCount = SucceededJobCount + FailedJobCount + CancelledJobCount
```

An unsealed cancelled/failed capture reports `TargetCount = 0` and retains `CapturedSoFar` only as
safe progress evidence.

## AlterationTargetState / job

One target document is created per deduplicated explicit ID or matching scanned workflow. It becomes
claimable only after its plan is sealed.

| Field | Type | Rules |
|---|---|---|
| `JobId` | string | Deterministic from plan ID + workflow execution ID |
| `PlanId` | string | Required |
| `WorkflowExecutionId` | string | Required |
| `TenantPartition` | string | Must equal plan partition |
| `CaptureOrdinal` | long | Stable capture evidence, not public execution order |
| `CapturedConcurrency` | CapturedWorkflowConcurrency | Immutable |
| `Status` | AlterationJobStatus | State graph below |
| `Claim` | AlterationJobClaim? | Owner/token/expiry for at-least-once work |
| `AttemptCount` | int | Monotonic |
| `Outcomes` | ordered outcome list | Empty until terminal checkpoint |
| `CheckpointCommitId` | string? | Required for succeeded/failed actor jobs |
| `SafeFailure` | typed safe failure? | Target missing/dispatch/orchestration only |
| `CreatedAt` | timestamp | Required |
| `StartedAt` | timestamp? | Required after first claim |
| `CompletedAt` | timestamp? | Required when terminal |
| `Revision` | long | Optimistic concurrency |

### AlterationJobStatus

```text
Pending -> Running -> Succeeded
                   -> Failed
Pending -> Cancelled
Running -> Running                 claim expiry/redelivery
```

`Succeeded`, `Failed`, and `Cancelled` are terminal. A cancelled pending job receives one `Skipped`
outcome per envelope with code `PlanCancelled`. A running job ignores plan cancellation until its
atomic turn completes.

### CapturedWorkflowConcurrency

| Field | Purpose |
|---|---|
| Tenant/authority partition | Prevent cross-scope execution |
| Exact current five-field pinned artifact identity | Detect incompatible artifact drift |
| Workflow lifecycle and state revision/updated marker | Validate relevant non-cancel preconditions |
| Root variable-frame revision | `ModifyVariable` optimistic concurrency |
| Referenced source activity identities/status/revision | Schedule/reschedule conflict checks |
| Requested migration target exact identity | Bind compatibility proof |

Only facts relevant to the submitted handlers are captured. `CancelWorkflow` deliberately reads
current lifecycle at execution and treats a terminal target as a no-op.

## AlterationOutcome

```csharp
public sealed record AlterationOutcome(
    int Ordinal,
    string Kind,
    int SchemaVersion,
    AlterationOutcomeStatus Status,
    string Code,
    string? Message,
    DateTimeOffset RecordedAt,
    IReadOnlyDictionary<string, string> StructuralMetadata);
```

Statuses are `Succeeded`, `Failed`, and `Skipped`.

Rules:

- Exactly one outcome exists per envelope on a terminal actor-executed job.
- The first failed outcome stops evaluation; every later outcome is `Skipped`.
- A failed job checkpoint contains no staged workflow mutation.
- Message and metadata are bounded and policy-safe.
- No submitted payload, variable value, exception, stack, CLR type, or secret is stored.

## AlterationJobClaim

| Field | Type | Rules |
|---|---|---|
| `OwnerId` | string | Nonblank worker identity |
| `Token` | string | Opaque compare-and-swap fence |
| `ExpiresAt` | timestamp | TimeProvider-driven visibility lease |

Only the current live token may dispatch or terminalize a job. Expiry permits redelivery. The
workflow actor and checkpoint fence remain the mutation safety boundary.

## Target selectors

Exactly one selector is present.

### ExplicitExecutionIds

- Non-empty list.
- Normalized by trim, ordinal deduplication, and ordinal sorting for canonical request identity.
- Every requested ID produces one captured target record. A missing/inaccessible ID later becomes a
  safe `TargetNotFound` failed job; existence outside the sealed scope is never disclosed.

### WorkflowExecutionQuery

Allowed optional equality/range filters:

- `definitionId`
- `status`
- `runKind`
- `from`
- `to`
- `correlationId`
- `workflowExecutionId`
- `artifactId`

Tenant/authority filters are derived from authorization and cannot be supplied. An otherwise empty
query requires `matchAllAuthorized: true`. Unknown statuses/run kinds and `from > to` are rejected.
All predicates are ANDed. Capture scans immutable tenant-partition/execution-ID order.

## Protected plan payload

```csharp
public sealed record ProtectedWorkflowAlterationPayload(
    string KeyId,
    string Algorithm,
    string Ciphertext);
```

Protection binds plan ID, tenant partition, and canonical request hash as authenticated associated
data. Durable hosts must retain every referenced decryption key until affected plans are deleted or
their payload is erased by retention policy.

## Built-in payloads

### CancelWorkflow/1

Empty object. Must be the only envelope.

### ModifyVariable/1

| Field | Type | Rules |
|---|---|---|
| `variableKey` | string | Exact workflow declaration reference key |
| `value` | JSON value | Converted to the declaration's exact type and protection policy |

The target's captured root-frame revision is the concurrency token.

### ScheduleActivity/1

| Field | Type | Rules |
|---|---|---|
| `nodeId` | string | Exact executable node ID |
| `parentActivityExecutionId` | string | Exact live direct parent |

The server derives successor identity, scope, path, branch/iteration, provenance, artifact, and
inputs. The direct executable child relation must carry an operator-scheduling capability described
below.

## Operator activity-scheduling capability

An executable direct-child relation MAY pin:

```csharp
public sealed record OperatorActivitySchedulingCapability(
    string PolicyKey,
    int SchemaVersion,
    JsonElement Configuration);
```

Rules:

- Absence means the relation cannot be scheduled by an operator.
- Policy identity is stable, exact, and participates in executable behavioral identity.
- Third-party policy keys are dotted/namespaced.
- Publishing obtains the capability from the parent activity module; generic Runtime never infers it
  solely from a child slot.
- The matching scoped runtime policy validates the parent lifecycle/scope, derives path/branch/
  iteration/provenance, prevents conflicting live children, stages any parent private-state change,
  and defines how child completion is consumed.
- The policy receives no caller-selected execution identity, scope, provenance, or input values.

### RescheduleActivity/1

| Field | Type | Rules |
|---|---|---|
| `sourceActivityExecutionId` | string | Scheduled/Waiting/Suspended/Faulted source |

The source transitions to `Superseded`; the replacement retains pinned inputs and records lineage.

### Migrate/1

| Field | Type | Rules |
|---|---|---|
| `targetArtifact` | WorkflowExecutableIdentity | All five immutable identity fields required |

Must occur once at most and first.

## ActivityExecutionState additions

`ActivityExecutionStatus` gains `Superseded`.

`ActivityExecutionState` gains optional:

- `SupersededByActivityExecutionId`
- `SupersededAt`

Invariants:

- Both are present only for `Superseded`.
- The successor ID differs from the source ID.
- Completed, Cancelled, Recovered, and Superseded are terminal historical states.
- Existing attempt lineage remains; supersession lineage identifies the distinct logical execution
  relationship.

## MigrationCompatibilityReport

Safe in-memory validation result:

```csharp
public sealed record MigrationCompatibilityReport(
    bool IsCompatible,
    WorkflowExecutableIdentity Source,
    WorkflowExecutableIdentity Target,
    IReadOnlyCollection<MigrationCompatibilityFinding> Findings);
```

Stable finding categories cover workflow lifecycle/quiescence, definition identity, node identity,
consumer/schema contract, bookmark target, activity/template identity, scope topology, workflow
variable declaration/type/policy, runtime/storage requirements, retained dependencies, pending
scheduler/outbox work, live claims, inspection/provenance, and artifact reference retention.
Findings contain identifiers and codes only, never workflow values.

## Atomic checkpoint addition

`RuntimeCheckpointStateChangeSet` gains:

```csharp
IReadOnlyCollection<RuntimeStateChange<AlterationTargetState>> AlterationJobs
```

Validation enforces matching `JobId`, one terminal change for the current workflow execution, and
deterministic ordering. The commit fingerprint includes it. Groundwork and InMemory writers apply it
inside the same transaction/critical section as workflow state and the checkpoint commit marker.
Coalescing treats `RuntimeAlterationJob` as a mandatory flush boundary.
