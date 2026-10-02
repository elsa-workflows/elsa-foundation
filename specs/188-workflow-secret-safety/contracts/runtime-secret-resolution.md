# Contract: runtime secret resolution

Shapes for FR-001 to FR-005, as shipped in slice 3 (the bridge mapping and feature below are slice 4's, and the parts
marked slice 8 arrive with masking). Decisions and
alternatives are in [research R1 to R4](../research.md).

## Runtime-owned contract (`Elsa.Workflows.Runtime.Core`)

```csharp
/// A replacement contract (framework constitution §2.6.2): at most one implementation per container. Declared by
/// [RuntimeSecretResolverReplacementContract]; the runtime registers none, and the registering feature (slice 4) refuses a second.
[RuntimeSecretResolverReplacementContract]
public interface IRuntimeSecretResolver
{
    /// Resolves one reference for one tenant. Returns a failure for every domain outcome; throws only
    /// OperationCanceledException when the caller cancels.
    ValueTask<RuntimeSecretResolution> ResolveAsync(RuntimeSecretResolutionRequest request, CancellationToken cancellationToken = default);
}

public sealed record RuntimeSecretReference(string Name, string? TypeName = null, string? Scope = null);   // slice 2

public sealed record RuntimeSecretResolutionRequest(string TenantId, RuntimeSecretReference Reference);

// A class, not a record: a record's generated ToString would print the value.
public sealed class RuntimeSecretResolution
{
    public bool Succeeded { get; }
    public string? Value { get; }          // set only when Succeeded
    public string? FailureCode { get; }    // set only when !Succeeded
    public bool IsRetryable { get; }       // meaningful only when !Succeeded

    public static RuntimeSecretResolution Success(string value);
    // Refuses a code that is not a code name (ASCII letters and digits, starting with a letter), so free text that
    // could carry store detail never reaches a fault message.
    public static RuntimeSecretResolution Failure(string failureCode, bool isRetryable);
}

/// Runtime-owned fault classification. The fault recorder reads retryability and code through it, never through a
/// concrete exception type, so wrapping (masking) cannot change the classification.
public interface IRuntimeFaultClassification
{
    bool IsRetryable { get; }
    string? FailureCode { get; }
}

public sealed class RuntimeSecretResolutionException(string referenceName, string failureCode, bool isRetryable)
    : Exception($"Secret '{referenceName}' could not be resolved ({failureCode})."), IRuntimeFaultClassification;

// The host composes no IRuntimeSecretResolver: an activation failure, not a fault.
public sealed class RuntimeSecretResolverNotFoundException(string inputKey) : Exception;
```

Rules:

- `TenantId` is the partition the execution runs under, read from `IWorkflowExecutionPartitionAccessor.Current` at
  activation; that partition is the scope the instance's own rows are stored under. The instance's recorded tenant,
  when present, must match it (next rule). No binding, request payload, setting or default selects it. A global or
  across-scope access context throws from `RequireScope()` before resolution.
- Fail closed on tenant disagreement: when the executing instance's `WorkflowExecutionState.TenantId` is set and
  differs ordinally from the partition, resolution is refused with `TenantMismatch` (permanent) before the resolver
  is called. A null `TenantId` uses the partition (research R2). `ActivityActivationRequest` gains the workflow
  execution id so the activator can read the instance; every caller sets it, and the read happens only when a
  withheld secret envelope is present.
- `RuntimeSecretResolution.Value` and any resolved value never reach a log, exception message, metric, span
  attribute or persisted state. The fault message carries the reference name and the code only.
- `FailureCode` is one of the codes below, `ConversionFailed` from conversion (a backstop, research R11),
  `TenantMismatch` from the tenant check, or `ResolverFailed` (permanent) when the resolver throws anything while the
  activation's token is live, a cancellation-typed exception included, or returns null, which breaks its contract. It is a code name, not the resolver's error text: what a
  resolver throws is dropped, never wrapped or chained, because its message may carry the value or store detail.
- Classification is the resolver implementation's obligation. The activator copies `RuntimeSecretResolution.IsRetryable`
  into the fault unchanged and nothing in the runtime checks it, so FR-003's rule (`StoreUnavailable` transient, every
  other code permanent) holds only because the Secrets bridge maps it that way (the table below; proved per code by
  T036).

## Bridge mapping (`Elsa.Secrets.Workflows`, `SecretValueRuntimeResolver`)

| `SecretResolutionFailureCode` | Runtime `FailureCode` | `IsRetryable` |
|---|---|---|
| `StoreUnavailable` | `StoreUnavailable` | true |
| `NotFound`, `Inactive`, `Expired`, `Revoked`, `Deleted`, `TypeMismatch`, `ScopeMismatch`, `Unauthorized`, `CorruptState` | same name | false |
| `None` on a failed result (defensive) | `CorruptState` | false |

`ResolvedSecret.Error` is discarded: `DefaultSecretValueResolver` copies store exception text into it for
`StoreUnavailable`, which can carry store-private detail. Every enum member must appear in the mapping test, so a new
member added later fails the test until someone classifies it.

## Activation behavior (`ActivityActivator`)

| Situation | Outcome |
|---|---|
| Snapshot has no withheld secret envelopes | Unchanged path, no resolver call, no instance read. |
| Global or across-scope context | The partition accessor throws; resolver never called. |
| Instance `TenantId` set and different from the partition | Throw `RuntimeSecretResolutionException(name, "TenantMismatch", false)`; resolver never called. |
| Instance not found in the partition | Throw `InvalidOperationException`; resolver never called. |
| Resolver composed, resolution succeeds | Convert with the envelope's plan; hydrate; nothing written back. Slice 8 (masking) adds: register the value with `IRuntimeSecretMask`. |
| Resolution fails | Throw `RuntimeSecretResolutionException`; the handler's existing fault boundary records a fault whose `IsRetryable` and code come from `IRuntimeFaultClassification`, and, from slice 8 (masking), also when the exception is masked (the masking wrapper copies both). |
| Conversion fails while the activation's token is live (a cancellation-typed exception included), or the envelope carries no plan | Throw `RuntimeSecretResolutionException(name, "ConversionFailed", false)`, without the conversion's exception (its message may describe the value). `TypeMismatch` is never used for this case; it means only that the stored secret's type differs from the reference's. A backstop: publish compiles only plans from text that cannot fail on a string (research R11), so this is reached only by an artifact that skipped publish. |
| No `IRuntimeSecretResolver` composed | Throw `RuntimeSecretResolverNotFoundException`, which `ActivityActivationFailureHandler` classifies as kind `MissingSecretResolver`, capability `SecretResolver` (key `IRuntimeSecretResolver`), recovery `CorrectDeploymentAndResume`, so composing `SecretsWorkflows` repairs it; the activity waits with an `ArtifactActivationFailed` incident and is not faulted (§E2.6.1). A `VF-ACT-010` refusal of another input in the same snapshot is reported first, because composing a resolver would not repair it. |
| Activation canceled | The resolver gets the activation's token; whatever it throws, and a failure it reports, once that token is canceled is treated as the cancellation, never as a resolution failure. |
| Resolver throws anything while the activation's token is live, cancellation-typed or not (its own store or HTTP timeout) | Throw `RuntimeSecretResolutionException(name, "ResolverFailed", false)`, without the thrown exception or its message. |
| Resolver returns null | Throw `RuntimeSecretResolutionException(name, "ResolverFailed", false)`: the resolver broke its contract. |
| Resolution fails after the activity was created, and disposing its lease fails too | The two are thrown together, keeping the resolution failure's classification, so the fault still records its code and retryability. |
| Withheld envelope of kind `PolicyRequiresEncryption` | Throw `VF-ACT-010`: the value was withheld and cannot be recovered. Never hydrate null. A backstop only: publish refuses literal and expression bindings on encryption-required inputs (`VF-ACT-011`). |

Resolution happens only in the activator's hydration branch, which runs for strategies with
`RequiresInputHydration = true` (the CLR strategy). Every CLR activation goes through it: invoke, bookmark resume,
structural parent evaluation, and the second activation on a re-materialized snapshot after a child completes. Each
one re-resolves (FR-002).

Structural parent evaluation (notify-parent, parent-completion, and child-completion re-materialization) activates a
running parent before it checks which callback the parent implements, so it resolves the parent's secret inputs on every
such evaluation, including one in which the parent runs no callback (a parent that does not implement the notification,
completion or fault callback is activated, then acked or left as is); re-materialization activates it twice. A
resolution that fails there faults that evaluation, like any other activation failure.

The boundary retry never activates anything: it clones graph boundary inputs and
schedules a fresh execution of that graph boundary, which cannot carry a `Secret` binding. Activity kinds that read inputs outside the
hydration branch (graph activities, checkpoint participants, intrinsics), inputs an activity copies into its own
persisted state or returns in its result or fault, and inputs read at publish cannot carry a `Secret` binding: publish refuses it with `VF-ACT-012`. The full path list is [research R3a](../research.md).

## Publish-time refusals owned by this contract

| Code | Refused |
|---|---|
| `VF-ACT-012` | a `Secret` binding on an intrinsic node, a non-CLR consumer (graph activity), a CLR type implementing `IRuntimeActivityCheckpointParticipant`, an input the CLR type names in `[RefusesSecretBinding]` (the activity persists its value, returns it in its result or fault, or a publish-time reader needs a literal), the input named by `[ActivityValueOutcomes]`, or a variable default (research R12, R3a IP14 to IP22). Checked before the conversion plan, so it wins over `VF-COER-001` |
| `VF-COER-001` (existing) | a `Secret` binding on an input for which `ValueConversionPlanResolver` has no plan from source `String`, representation `TextValue`: everything except a single `String` (or its nullable alias) and a single `Elsa.Any`, `Any` or `JsonNode` input. Numeric, boolean, date/time, `TimeSpan`, enum, `Guid`, `Uri`, `Object`, `JsonElement`, `JsonObject` and every collection, including a collection of `String`, are refused, for every secret type and in every host (research R11) |

## Feature

```text
[ShellFeature(name: "SecretsWorkflows", DependsOn = { "Secrets", "ActivitiesRuntime" })]
public class SecretsWorkflowsFeature : IShellFeature   // public, not sealed; ConfigureServices virtual (§2.5)
```

Registers `IRuntimeSecretResolver -> SecretValueRuntimeResolver` (scoped, because `ISecretValueResolver` is scoped).
Enabled in every Workbench and compose shells file that enables `Secrets`. The exact runtime dependency name is
verified when the slice is implemented (`ActivitiesRuntime` registers the activator).
