# Phase 1 Contracts — Workflow-Definition GitOps (085)

Interfaces this feature introduces (all in `Elsa.Workflows.Design.Reconciliation.Git`) plus the two
extended shared contracts. "Contract" here = the C# surface other components bind to (there are no HTTP
endpoints in v1).

## New — git feature internal

### `IGitWorkspace`
Owns the local working clone; role-driven clone modes (D11/R7).
```csharp
public interface IGitWorkspace
{
    /// Clone-if-absent, then integrate per role (Writer: fetch + ff-only merge; Consumer: fetch +
    /// reset --hard). Applies credentials into the clone's git config first (R8). Returns repo path.
    /// Throws on a non-ff Writer divergence (the D7 single-writer signal) — never reset --hard the Writer.
    Task<string> EnsureReadyAsync(CancellationToken cancellationToken);

    /// Absolute path to the ready working clone (valid after EnsureReadyAsync).
    string RepositoryPath { get; }
}
```

### `IGitWorkflowExporter`
Writer-only export reconciler (set-diff sweep, D4/R11).
```csharp
public interface IGitWorkflowExporter
{
    /// Ensure every catalog version has versions/{semver}.json (write+commit if absent; skip if
    /// present), refresh definition.json on metadata change, then push per PushMode (ff-only). No-op
    /// for present versions. Never runs on a Consumer.
    Task ExportAsync(CancellationToken cancellationToken);
}
```

### `GitWorkflowReconciliationSource : IWorkflowReconciliationSource`
Inbound source (`SourceKind = "git"`, FR-003).
```csharp
// SourceId  => resolved RemoteUrl+Branch (stable per repo/branch)
// SourceKind => "git"
// Read: EnsureReadyAsync → enumerate {WorkflowsPath}/*/versions/*.json →
//   per file: deserialize State, read sibling definition.json (name/description/deleted),
//   SourceCreatedAt via git log -1 %cI, ContentHash via canonical re-serialize →
//   one WorkflowVersionReconciliationModel per version file. Malformed file → skip + diagnostic.
```

### `GitCanonicalJson` (static helper)
```csharp
public static class GitCanonicalJson
{
    // state -> compact canonical (IPayloadSerializer.Serialize)
    // compact -> indented (JsonNode pure-whitespace transform) for the on-disk file
    // fileText -> compact canonical (JsonNode.Parse(...).ToJsonString()) for hashing
    // compact -> SHA-256 hex
}
```

## New — startup tasks
- `WorkflowsVersionReconcilerStartupTask` — **already exists**; now registered by the base feature (R3).
- `GitWorkflowExportStartupTask : IStartupTask` — `[SingleNodeTask] [Order(3)]`, Writer-only, calls
  `IGitWorkflowExporter.ExportAsync` under a distributed lock.

## New — feature + config
- `WorkflowsDesignGitReconciliationFeature : WorkflowsDesignReconciliationFeature` (`[ShellFeature]`).
- `GitReconciliationOptions` (bound), `GitExportOptions`, enums `GitReconciliationRole` /
  `GitCredentialsMode` / `GitPushMode`.

## Extended — shared contracts (additive, back-comp N/A — unreleased)

### `WorkflowVersionReconciliationModel` (record)
+ `string? ContentHash = null`, `+ bool Deleted = false` (see data-model.md).

### `IWorkflowDefinition` (`Elsa.Workflows.Design.Core`)
+ `DateTimeOffset? DeletedAt { get; }`.

### `IWorkflowDefinitionFactory`
`Create(string name, string? description = null, string? id = null, bool deleted = false)`.

### `WorkflowsVersionReconciler` (behavior)
- FR-008a: `UpdateDefinitionMetadata` relocated after the outdated-version skip (R9).
- `UpdateDefinitionMetadata` widened to reconcile `DeletedAt` (R10).
- Duplicate path: recompute-and-compare canonical `State` hash → warn on mismatch (R13). Adds
  `IPayloadSerializer` dependency (+ a by-definition version load).

## Contract test obligations (→ tasks.md)
1. Source: N version files → N models with correct SemVer, committer-date `SourceCreatedAt`, populated `ContentHash`; malformed file skipped.
2. Source: `definition.json` name/description/`deleted` mapped onto every model for the definition.
3. Canonical: `hash(compact(indent(x))) == hash(compact(x))`; string-internal whitespace preserved.
4. Exporter: absent versions written+committed, present skipped (idempotent); second run no-op; ff-only push refused on divergence.
5. Reconciler FR-008a: older incoming entry does NOT change definition metadata; newest still does.
6. Reconciler R10: incoming `deleted` soft-deletes (no row deletion); `deleted:false` un-deletes.
7. Reconciler R13: same `(id,version)` different `State` → warning logged (Throw mode still throws).
8. Feature: `Role=Writer` registers exporter + export task; `Role=Consumer` registers neither; base registers the import startup task.
9. Round-trip: export a version, import on Writer and Consumer → both no-ops for that version.
