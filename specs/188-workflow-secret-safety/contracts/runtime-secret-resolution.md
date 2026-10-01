# Contract: runtime secret resolution

Proposed shapes for FR-001 to FR-005. Decisions and alternatives are in [research R1 to R4](../research.md).

## Runtime-owned contract (`Elsa.Workflows.Runtime.Core`)

```csharp
/// A replacement contract (framework constitution §2.6.2): at most one implementation per container.
public interface IRuntimeSecretResolver
{
    /// Resolves one reference for one tenant. Returns a failure for every domain outcome; throws only
    /// OperationCanceledException when the caller cancels.
    ValueTask<RuntimeSecretResolution> ResolveAsync(RuntimeSecretResolutionRequest request, CancellationToken cancellationToken = default);
}

public sealed record RuntimeSecretReference(string Name, string? TypeName = null, string? Scope = null);

public sealed record RuntimeSecretResolutionRequest(string TenantId, RuntimeSecretReference Reference);

public sealed record RuntimeSecretResolution
{
    public bool Succeeded { get; init; }
    public string? Value { get; init; }          // set only when Succeeded
    public string? FailureCode { get; init; }    // set only when !Succeeded; a stable code name, never free text
    public bool IsRetryable { get; init; }       // meaningful only when !Succeeded
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
```

Rules:

- `TenantId` is supplied by the runtime from `IWorkflowExecutionPartitionAccessor.Current` at activation. No
  binding, request payload, setting or default supplies it. A global or across-scope access context throws from
  `RequireScope()` before resolution.
- Fail closed on tenant disagreement: when the executing instance's `WorkflowExecutionState.TenantId` is set and
  differs ordinally from the partition, resolution is refused with `TenantMismatch` (permanent) before the resolver
  is called. A null `TenantId` uses the partition (research R2). `ActivityActivationRequest` gains the workflow
  execution id so the activator can read the instance; every caller sets it, and the read happens only when a
  withheld secret envelope is present.
- `RuntimeSecretResolution.Value` and any resolved value never reach a log, exception message, metric, span
  attribute or persisted state. The fault message carries the reference name and the code only.
- `FailureCode` is one of the codes below, `TypeMismatch` from conversion (a backstop, research R11), or
  `TenantMismatch` from the tenant check. It is a code name, not the resolver's error text.

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
| Instance `TenantId` set and different from the partition | Throw `RuntimeSecretResolutionException(name, "TenantMismatch", false)`; resolver never called. |
| Resolver composed, resolution succeeds | Convert with the envelope's plan; hydrate; register value with `IRuntimeSecretMask`; nothing written back. |
| Resolution fails | Throw `RuntimeSecretResolutionException`; the handler's existing fault boundary records a fault whose `IsRetryable` and code come from `IRuntimeFaultClassification`, also when the exception is masked (the masking wrapper copies both). |
| Conversion fails | Throw `RuntimeSecretResolutionException(name, "TypeMismatch", false)`. A backstop: publish compiles only plans from text that cannot fail on a string (research R11), so this is reached only by an artifact that skipped publish. |
| No `IRuntimeSecretResolver` composed | Throw the activation failure classified by `ActivityActivationFailureHandler` (new kind, recovery "compose `SecretsWorkflows`"); the activity waits with an incident and is not faulted (§E2.6.1). |
| Withheld envelope of kind `PolicyRequiresEncryption` | Throw `VF-ACT-010`: the value was withheld and cannot be recovered. Never hydrate null. A backstop only: publish refuses literal and expression bindings on encryption-required inputs (`VF-ACT-011`). |

Resolution happens only in the activator's hydration branch, which runs for strategies with
`RequiresInputHydration = true` (the CLR strategy). Every CLR activation goes through it: invoke, bookmark resume,
structural parent evaluation, and the second activation on a re-materialized snapshot after a child completes. Each
one re-resolves (FR-002). The boundary retry never activates anything: it clones graph boundary inputs and
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
