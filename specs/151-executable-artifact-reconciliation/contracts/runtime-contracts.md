# Contract: Runtime-layer service contracts (spec 151)

Signatures are normative in shape; exact parameter/record spelling settles in tasks. All follow §E6 naming (see research.md D8) and §2.23 test obligations.

## `IRuntimeRequirementChecker` — `Elsa.Workflows.Runtime.Core` (new; FR-B-005)

```csharp
public interface IRuntimeRequirementChecker
{
    /// Evaluates the artifact's declared RuntimeRequirements + StorageDriverRequirements
    /// against the installed runtime registries, AND per-node CLR activity-type presence
    /// against IWellKnownTypeRegistry (second axis). Exact ordinal semantics, unchanged
    /// from the publishing preflight this was extracted from.
    Task<RuntimeRequirementCheckResult> CheckAsync(WorkflowExecutable executable, CancellationToken ct = default);
}
```

- Default `RuntimeRequirementChecker` in `Elsa.Workflows.Runtime`, registered by `AddWorkflowRuntime()` (`TryAddScoped`).
- Consumers: publishing's `RuntimeRequirementPreflight` (thin wrapper — keeps retained-set scope, views, `ActivityDiagnostic` formatting; **gains** the missing activity-consumer diagnostics) and the artifact importer's gate (FR-B-005a).
- Depends only on: `IRuntimeActivityConsumerCapability` (Activities.Runtime.Core), `IRuntimeDurableValueStorageDriverRegistry` (Runtime.Core), `IWellKnownTypeRegistry` (Serialization.Core). All already inside Runtime.Core's reference envelope.

## `IPublicationSlotStore` — relocated to `Elsa.Workflows.Runtime.Core` (FR-B-006 / A2)

Contract, `PublicationSlot`, and `PublicationSlotTransitionResult` move **unrenamed** from `Elsa.Workflows.Publishing.Core.Contracts/Models`. Semantics unchanged (definition-keyed authority, `Revision` CAS, returns `ReplacedPublicationId`). Replacement contract with **one durable implementation, one physical home**: a slot document kind in the runtime Groundwork store family, registered with the other runtime stores; the publishing-family slot store is **deleted** (no consumers yet → nothing to migrate); in-memory default otherwise. Groundwork historical-schema baselines update as a named task.

**Publication-id namespace (new convention, opaque strings):** publish = `publication-{shortId}`; import = `import:{sourceId}:{shortId}`. Cross-authority guard: an actor MUST NOT supersede an `ActivePublicationId` carrying the other namespace — reject with a diagnostic naming the conflicting authority.

## `IWorkflowExecutableHasher` — extracted to `Elsa.Workflows.Runtime.Core` (FR-B-010; Greptile re-review)

The canonical content-hash derivation (`ComputeHash(executable) → "sha256:…"`, `CreateArtifactId(prefix, hash)`) moves from `Elsa.Workflows.Publishing.Services.WorkflowExecutableHasher` to a runtime-layer contract + default (third extraction, same pattern as the checker and slot authority). Its canonical payload reads only `WorkflowExecutable` model data, so the move is dependency-clean (verify exact set at task time). Consumers: the compiler (existing derivation site, now via the contract) and the importer (recompute-before-persist; mismatch → broken-source diagnostic). Deterministic and public — an integrity guard for the ADR 0038 content-addressing invariant, not a security boundary.

## `IWorkflowArtifactReconciliationSource` — `Elsa.Workflows.Runtime.Reconciliation.Core` (new; FR-B-002)

```csharp
public interface IWorkflowArtifactReconciliationSource
{
    string SourceId { get; }     // required, self-identifying (mirrors IWorkflowReconciliationSource)
    string SourceKind { get; }   // e.g. "json-folder"
    IAsyncEnumerable<WorkflowArtifactClosureFile> ReadAsync(CancellationToken ct = default);
}
```

v1 implementation: JSON folder/file source configured by `JsonWorkflowArtifactReconciliationOptions` (FilePath | ordered Files | FolderPath; `SourceId` required; optional `TenantId`). Exception taxonomy (§2.23.5), preserving the scope distinction: **file-level** infrastructure failures (unreadable, malformed JSON, unknown format version) wrap as `InvalidWorkflowArtifactClosureException` (carries the file path); **pass-aborting** conditions (e.g. configured folder missing) use the `WorkflowArtifactReconciliationException` family; empty folder → no-op; **per-artifact** rejections are diagnostics on the pass result, never exceptions (batch isolation).

## `IWorkflowArtifactReconciler` — `Elsa.Workflows.Runtime.Reconciliation` (new)

```csharp
public interface IWorkflowArtifactReconciler
{
    /// One pass over all registered sources: parse → closure validation → requirements
    /// gate → idempotency/supersession → activate. Per-artifact isolation: rejections are
    /// diagnostics on the result, never batch failures.
    Task<WorkflowArtifactReconciliationResult> ReconcileAsync(CancellationToken ct = default);
}
```

Triggered by `WorkflowArtifactReconcilerStartupTask` (`[SingleNodeTask]`, distributed lock, ordered after `RegisterActivityTypesStartupTask` via `[TaskDependency]`); re-triggered by the existing shell-reload path (FR-B-008).

## `IWorkflowArtifactClosureFactory` — `Elsa.Workflows.Publishing` (new; FR-B-010)

```csharp
public interface IWorkflowArtifactClosureFactory
{
    /// Builds the portable closure for one Published version: executable + transitive
    /// dependency closure + Published-scope source references + active trigger bindings.
    /// Destination-agnostic. Throws domain exceptions for missing dependencies or
    /// non-Published references (FR-B-011).
    Task<WorkflowArtifactClosure> CreateAsync(string definitionVersionId, CancellationToken ct = default);
}
```

## `IWorkflowArtifactExportTarget` — `Elsa.Workflows.Publishing.Core` (new; FR-B-010a)

```csharp
public interface IWorkflowArtifactExportTarget
{
    string TargetId { get; }  // "download" (v1 built-in); "folder", "blob" (deferred)
    Task<WorkflowArtifactExportDelivery> DeliverAsync(WorkflowArtifactClosure closure, CancellationToken ct = default);
}

public sealed record WorkflowArtifactExportDelivery(
    string TargetId,
    ExportDeliveryKind Kind,          // InlinePayload | Receipt
    ReadOnlyMemory<byte>? Payload,    // InlinePayload: the closure bytes
    string? Location);                // Receipt: where the target delivered it
```

Strategy (§2.24.2 #9); fan-in via `TryAddEnumerable`; future targets contribute, never replace. The v1 GET endpoint binds to the `download` target only (safe-method semantics); receipt-producing targets (blob/folder) ship with their own POST command surface carrying an idempotency contract.

## Feature surface

| Feature class | Id (`[ShellFeature]` name) | Project | DependsOn |
|---|---|---|---|
| `WorkflowsArtifactReconciliationFeature` (abstract, no attribute) | — | Runtime.Reconciliation | — |
| `JsonWorkflowArtifactReconciliationFeature` | `JsonWorkflowArtifactReconciliation` | Runtime.Reconciliation | `Tasks` (+ calls `AddWorkflowRuntime()` itself, idempotent per ADR 0029) |
| `WorkflowsPublishingFeature` / `WorkflowsPublishingApiFeature` (modified) | existing | Publishing / Publishing.Api | unchanged |
