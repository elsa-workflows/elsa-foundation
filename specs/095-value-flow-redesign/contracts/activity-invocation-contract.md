# Activity Invocation Contract

This contract defines the canonical boundary between a published activity node and one logical
runtime invocation. The signatures are normative at the semantic level. Namespace placement and
minor spelling may change only if the resulting API preserves every invariant and conformance case
below.

## 1. Activity author contract

An ordinary CLR activity is a transient service-activated object. Workflow data is supplied only
through plain annotated properties.

```csharp
public sealed class ChargeCard(IPaymentGateway gateway) : Activity<ChargeCardResult>
{
    [ActivityInput(Key = "customer-id")]
    public required string CustomerId { get; init; }

    [ActivityInput(Key = "amount")]
    public required decimal Amount { get; init; }

    protected override async ValueTask<ActivityTransition<ChargeCardResult>> ExecuteAsync(
        ActivityExecutionContext context)
    {
        var receipt = await gateway.ChargeAsync(CustomerId, Amount, context.CancellationToken);
        return ActivityTransition.Complete(
            new ChargeCardResult(receipt.Id, receipt.Approved),
            ChargeCardOutcomes.Completed);
    }
}

public sealed record ChargeCardResult(
    [property: Output(Key = "receipt-id")] string ReceiptId,
    [property: Output(Key = "approved")] bool Approved);
```

The author-facing protocols are:

```csharp
public interface IActivity
{
    ValueTask<ActivityTransition> ExecuteAsync(ActivityExecutionContext context);
}

public abstract class Activity<TResult> : IActivity
{
    protected abstract ValueTask<ActivityTransition<TResult>> ExecuteAsync(
        ActivityExecutionContext context);
}

public interface IStatefulActivity<TResult, TState, TTrigger>
{
    ValueTask<ActivityTransition<TResult, TState>> ExecuteAsync(
        ActivityExecutionContext context);

    ValueTask<ActivityTransition<TResult, TState>> ResumeAsync(
        ActivityResumeContext<TState, TTrigger> context);
}
```

The generic bases erase their typed transition only at the engine boundary. Constructor parameters
are services. `[ActivityInput]` properties are workflow data. Activity code MUST NOT receive an
`IServiceProvider`, memory register, mutable output context, variable writer, or a generic value bag.

## 2. Closed transition algebra

An invocation attempt returns exactly one transition:

```text
Complete(result, outcome)
Suspend(state, triggerRegistrations)
Fault(normalizedFault)
Cancel(reason)
```

- `Complete` carries one complete typed result and one authored successful outcome.
- `Suspend` carries one immutable private state document and one or more typed trigger registrations.
- `Fault` carries normalized persistable fault information, never an arbitrary serialized exception.
- `Cancel` is distinct from both failure and successful authored outcomes.
- A transition MUST NOT carry a variable mutation, independently writable output slots, scheduler
  callbacks, service instances, or CLR delegates.
- A named output is a read-only projection of the result document. Projection failure invalidates
  the completion; it MUST NOT produce a partial result.

A stateful activity is reactivated as a fresh CLR object. `ResumeAsync` receives the last committed
`TState` and exactly one validated `TTrigger`. CLR fields and injected services from an earlier
attempt are never reused.

## 3. Pinned activity contract

Every executable activity node pins the following immutable contract:

```json
{
  "activityType": "Payments.ChargeCard",
  "contractVersion": "2.0.0",
  "schemaFingerprint": "sha256:...",
  "inputs": [
    {
      "key": "customer-id",
      "type": { "alias": "String", "collection": "None" },
      "required": true,
      "hasDefault": false
    },
    {
      "key": "amount",
      "type": { "alias": "Decimal", "collection": "None" },
      "required": true,
      "hasDefault": false
    }
  ],
  "result": {
    "type": { "alias": "Payments.ChargeCardResult", "collection": "None" },
    "projections": [
      { "key": "receipt-id", "path": "receiptId", "type": { "alias": "String" } },
      { "key": "approved", "path": "approved", "type": { "alias": "Boolean" } }
    ]
  },
  "outcomes": ["completed", "declined"],
  "activation": { "descriptorType": "clr", "capability": "Payments.ChargeCard" }
}
```

Stable keys, not CLR member names, identify inputs and projections. Serialized type identity uses the
alias registry and schema descriptors. Assembly-qualified CLR names are forbidden. The schema
fingerprint covers input keys/types/requiredness/defaults/policies, the result schema and projection
paths, input nullability, outcome keys, activation descriptor, and contract version.

Publication MUST fail if the pinned contract, constructor, generated/manual hydrator, or required
activation capability is missing or incompatible. Missing activation capability is a deployment or
publication fault, not a supported activity outcome.

## 4. Invocation records

The runtime owns these role-specific records:

```csharp
public sealed record ActivityInputSnapshot(
    string InvocationId,
    string ExecutableNodeId,
    string ContractFingerprint,
    IReadOnlyDictionary<string, PersistedValue> Inputs,
    DateTimeOffset MaterializedAt);

public sealed record ActivityAttempt(
    string AttemptId,
    string InvocationId,
    int AttemptNumber,
    ActivityAttemptKind Kind,
    DateTimeOffset StartedAt);

public sealed record ActivityCompletion(
    string InvocationId,
    string AttemptId,
    PersistedValue Result,
    string OutcomeKey,
    DateTimeOffset CompletedAt);
```

The persisted JSON shape is versioned and role-named:

```json
{
  "schemaVersion": 1,
  "invocationId": "inv-42",
  "executableNodeId": "charge-card",
  "contractFingerprint": "sha256:...",
  "inputSnapshot": {
    "customer-id": { "type": { "alias": "String" }, "value": "c-7" },
    "amount": { "type": { "alias": "Decimal" }, "value": 19.95 }
  },
  "attempts": [
    { "attemptId": "attempt-1", "number": 1, "kind": "Initial" }
  ],
  "privateState": null,
  "completion": null
}
```

`PersistedValue` is a storage envelope beneath the owning role. It is not addressable workflow
memory and exposes no public `Get` or `Set` contract.

## 5. Ordering and atomicity

The following ordering is mandatory for durable execution:

1. Resolve every binding against one causal runtime view.
2. Type-check and persistability-check every value.
3. Commit `InvocationId`, the complete `ActivityInputSnapshot`, and the first `ActivityAttempt` while
   moving the activity execution from Scheduled to Running.
4. Publish the invocation intent only after that checkpoint commits.
5. Activate a fresh CLR object and hydrate every input exactly once from the committed snapshot.
6. Execute user code.
7. Commit the selected transition through the existing checkpoint path.
8. Schedule downstream work only after successful completion or suspension state commits.

Input materialization is all-or-nothing. No constructor, property hydrator, activity method, or
injected user service may run before step 3. A crash after step 3 reuses the snapshot and creates a
new attempt. A crash after completion commit reuses the completion and MUST NOT rerun the activity
solely to schedule downstream work.

Retries and resumptions preserve `InvocationId`, `ExecutableNodeId`, contract fingerprint, and input
snapshot. Each creates a new monotonically numbered `AttemptId` and fresh activation. A completed
invocation cannot gain another successful completion.

## 6. Activation and hydration

The runtime activation seam is:

```csharp
public interface IActivityActivator
{
    ValueTask<ActivityActivationLease> ActivateAsync(
        ActivityActivationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ActivityActivationRequest(
    ActivityContract Contract,
    ActivityInputSnapshot Inputs,
    ActivityAttempt Attempt,
    ActivityPrivateState? PrivateState = null,
    ActivityTriggerDelivery? Trigger = null);

public sealed class ActivityActivationLease : IAsyncDisposable
{
    public IActivity Activity { get; }
}
```

`ActivityActivationLease` owns the CLR object and whichever DI scope is selected by the benchmark decision.
Disposal occurs for complete, suspend, fault, cancel, activation failure, hydration failure, and host
cancellation. Engine intrinsics do not call `IActivityActivator` and create no activity DI scope.

Generated, manual, and reflection-based hydrators MUST produce identical observable behavior:

- assign by stable input key;
- distinguish omitted, explicit `null`, and pinned default;
- preserve CLR nullable-reference intent independently of requiredness in the pinned input contract;
- reject a missing required input;
- reject `null` for a non-nullable input;
- reject unknown or duplicate input keys;
- perform no service lookup and no expression evaluation;
- assign each property at most once.

## 7. Required diagnostics

Failures MUST be typed and include workflow execution id, executable node id, invocation id when
allocated, stable member key when applicable, and the pinned contract fingerprint.

| Code | Required condition |
| --- | --- |
| `VF-ACT-001` | Contract or schema fingerprint mismatch |
| `VF-ACT-002` | Missing activation capability, constructor, or hydrator |
| `VF-ACT-003` | Required input absent after binding normalization |
| `VF-ACT-004` | Input type/nullability violation |
| `VF-ACT-005` | Input, state, or result is nonpersistable under durable policy |
| `VF-ACT-006` | Result projection or authored outcome is not declared by the contract |
| `VF-ACT-007` | Attempt requested after committed completion |
| `VF-ACT-008` | Trigger type or identity does not match suspended state |
| `VF-ACT-009` | Persisted invocation document version is incompatible and cannot be upcast |

Diagnostics MUST NOT include unredacted sensitive values. Activity faults use the normal fault
transition/incident path; contract and durability failures occur before user code and are not
converted to authored outcomes.

## 8. Conformance obligations

An implementation satisfies this contract only if automated tests prove:

1. generated, manual, and reflection-discovered contracts hydrate the same fixture identically;
2. stable-key CLR renames remain compatible and incompatible schemas fail before activation;
3. a source variable changed after snapshot commit does not change retry/resume input;
4. failure at each checkpoint boundary never exposes a partial snapshot or partial completion;
5. committed completion survives restart without another activity call;
6. complete, suspend, fault, and cancel serialize and recover distinctly;
7. duplicate/wrong triggers are rejected before `ResumeAsync`;
8. activation and injected disposable services are disposed on every terminal/error path;
9. intrinsic-only fixtures record zero activations and zero activity scopes; and
10. serialized records contain no memory interface, argument wrapper, delegate, service, or
    assembly-qualified type name.

## 9. Forbidden compatibility behavior

Canonical activity, executable, and runtime packages MUST NOT expose or depend on `Argument`,
`InputArgument`, `OutputArgument`, `IMemoryBlock`, `IMemoryBlockReference`, `IMemoryRegister`,
`IActivity.SyntheticProperties` as a value channel, a mutable output context, or a service locator.
There is no forwarding shim from these types to the invocation records.
