# Phase 1 Data Model — Workflow-Definition GitOps (085)

No new **persistent** entities. The catalog entities (`WorkflowDefinition`,
`WorkflowDefinitionVersion`) are unchanged on disk; git is a source/sink over them (D1). What changes:
one additive field on an in-memory reconciliation model, one additive facade member, and the on-disk
JSON layout git owns.

## Modified in-memory models

### `WorkflowVersionReconciliationModel` (additive)
`src/Elsa/Workflows/Design/Reconciliation/Models/WorkflowVersionReconciliationModel.cs`

| Member | Type | Change | Notes |
|---|---|---|---|
| `ContentHash` | `string?` | **new**, optional | FR-007. Canonical SHA-256 (hex) the source carries ahead of a persisted home (FR-016a). Default `null`. |
| `Deleted` | `bool` | **new**, default `false` | FR-008/R10. Definition-level soft-delete intent from `definition.json`. |

Final record shape:
```csharp
public sealed record WorkflowVersionReconciliationModel(
    string? DefinitionId,
    string Name,
    string? Description,
    string Version,
    WorkflowDefinitionState State,
    DateTimeOffset? SourceCreatedAt = null,
    string? ContentHash = null,
    bool Deleted = false
);
```

### `IWorkflowDefinition` (additive) — `Elsa.Workflows.Design.Core`
| Member | Type | Change | Notes |
|---|---|---|---|
| `DeletedAt` | `DateTimeOffset?` | **new**, read-only | R10. Definition-level lifecycle timestamp (peer of `CreatedAt`/`LastModifiedAt`). NOT authored content → §E2.9 untouched. |

The persistence entity `WorkflowDefinition` already has settable `DeletedAt`/`DeletedReason`; this only
surfaces the read side on the shared contract so the reconciler and `WorkflowDefinition.From` can honor it.

### `IWorkflowDefinitionFactory.Create` (additive param)
`Create(string name, string? description = null, string? id = null, bool deleted = false)` — the
read-model stamps `DeletedAt` from `deleted` (via `TimeProvider`).

## On-disk model (git — owned by this feature)

```
{WorkflowsPath}/                         # config, default "workflows"
  {definitionId}/
    definition.json                      # MUTABLE metadata, latest-wins
    versions/
      {semver}.json                      # IMMUTABLE canonical WorkflowDefinitionState, hashed
```

### `definition.json`
```jsonc
{ "name": "Order Approval", "description": "…", "deleted": false }
```
Sole authority for name/description/`deleted` (FR-008). Missing/malformed → non-fatal diagnostic;
metadata falls back to id / empty / not-deleted.

### `versions/{semver}.json`
`indent(IPayloadSerializer.Serialize(state))` — the indented canonical `WorkflowDefinitionState`
(R4). Immutable (write-once; export skips if present). Content identity =
`SHA-256(compact canonical form)`; the indent is a pure-whitespace transform so
`hash(compact(gitfile)) == hash(StateSource)`.

## Configuration binding (CShells `[ShellFeature]`)

`WorkflowsDesignGitReconciliationFeature` — bound from
`CShells:Shells:{shell}:Features:WorkflowsDesignGitReconciliation`.

| Property | Type | Default | Manifest | Notes |
|---|---|---|---|---|
| `RemoteUrl` | `string` | `""` | setting | e.g. `git@github.com:acme/workflows.git`. |
| `Branch` | `string` | `"main"` | setting | Tracked branch. |
| `WorkflowsPath` | `string` | `"workflows"` | setting | Repo-relative root. |
| `LocalCachePath` | `string` | `""` | setting | Empty → under host data dir. |
| `Role` | `GitReconciliationRole` | `Consumer` | setting | `Writer` \| `Consumer` — drives clone mode + export (D11). |
| `CredentialsMode` | `GitCredentialsMode` | `HostDefault` | setting | `SshKey` \| `Token` \| `HostDefault` (FR-013). |
| `KeyPath` | `string` | `""` | setting | SSH key path (SshKey mode). |
| `Token` | `string` | `""` | `[ManifestSetting(Secret = true)]` | Token mode only. |
| `Export.PushMode` | `GitPushMode` | `Manual` | setting | `Manual` \| `Immediate` — honored only when Writer. |
| `Export.Branch` | `string` | `""` | setting | Export branch; empty → `Branch`. |
| `Export.Tag` | `bool` | `true` | setting | Emit `wf/{definitionId}/v{version}` tag. |
| `ReconcilerOptions.DuplicateHandling` | `DuplicateHandling` | `Skip` | inherited | From the base feature. |

### Enums (new, git feature)
- `GitReconciliationRole { Writer, Consumer }`
- `GitCredentialsMode { SshKey, Token, HostDefault }`
- `GitPushMode { Manual, Immediate }`

## Relationships / invariants
- One `definition.json` : many `versions/*.json` per `{definitionId}` (mirrors 1 Definition : many Versions).
- Version files immutable; `definition.json` re-committed on metadata change (latest-wins).
- Soft-delete is a flag on `definition.json` / `DeletedAt`; **no** file or row is ever deleted (D1 retention authority).
- Content identity is over the canonical serialization, not the raw stored blob (D3).
