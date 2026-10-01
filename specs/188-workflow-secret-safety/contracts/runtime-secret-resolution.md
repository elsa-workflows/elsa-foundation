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

public sealed class RuntimeSecretResolutionException(string referenceName, string failureCode, bool isRetryable)
    : Exception($"Secret '{referenceName}' could not be resolved ({failureCode}).");
```

Rules:

- `TenantId` is supplied by the runtime from `IWorkflowExecutionPartitionAccessor.Current` at activation. No
  binding, request payload, setting or default supplies it. A global or across-scope access context throws from
  `RequireScope()` before resolution.
- `RuntimeSecretResolution.Value` and any resolved value never reach a log, exception message, metric, span
  attribute or persisted state. The fault message carries the reference name and the code only.
- `FailureCode` is one of the codes below or `TypeMismatch` from conversion. It is a code name, not the resolver's
  error text.

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
| Snapshot has no withheld secret envelopes | Unchanged path, no resolver call. |
| Resolver composed, resolution succeeds | Convert with the envelope's plan; hydrate; register value with `IRuntimeSecretMask`; nothing written back. |
| Resolution fails | Throw `RuntimeSecretResolutionException`; the handler's existing fault boundary records a fault whose `IsRetryable` comes from the exception. |
| Conversion fails | Throw `RuntimeSecretResolutionException(name, "TypeMismatch", false)`. |
| No `IRuntimeSecretResolver` composed | Throw the activation failure classified by `ActivityActivationFailureHandler` (new kind, recovery "compose `SecretsWorkflows`"); the activity waits with an incident and is not faulted (§E2.6.1). |
| Withheld envelope of kind `PolicyRequiresEncryption` | Throw `VF-ACT-010`: the value was withheld and cannot be recovered. Never hydrate null. |

Every activation path goes through the activator: invoke, bookmark resume, the retry boundary and structural parent
evaluation. Each one re-resolves (FR-002).

## Feature

```text
[ShellFeature(name: "SecretsWorkflows", DependsOn = { "Secrets", "ActivitiesRuntime" })]
public class SecretsWorkflowsFeature : IShellFeature   // public, not sealed; ConfigureServices virtual (§2.5)
```

Registers `IRuntimeSecretResolver -> SecretValueRuntimeResolver` (scoped, because `ISecretValueResolver` is scoped).
Enabled in every Workbench and compose shells file that enables `Secrets`. The exact runtime dependency name is
verified when the slice is implemented (`ActivitiesRuntime` registers the activator).
