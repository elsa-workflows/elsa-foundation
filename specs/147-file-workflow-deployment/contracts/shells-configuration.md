# Contract: Shell configuration surface

**Feature id**: `JsonWorkflowReconciliation` (unchanged — §2.19: the feature name is the config binding key and is stable).

## Feature entry (Options-wrapper shape)

```jsonc
// shells.json → CShells.Shells.default.Features
"JsonWorkflowReconciliation": {
  "Options": {
    "SourceId": "mounted-definitions",          // required, non-empty
    "FolderPath": "/app/workflow-definitions",  // exactly one of FolderPath | FilePath | Files
    "PublishOnReconcile": true                  // optional, default false (import-only)
  }
}
```

Alternative path options (mutually exclusive with `FolderPath` and each other):

```jsonc
"Options": { "SourceId": "s", "FilePath": "/app/defs/workflows.json" }
```

```jsonc
"Options": { "SourceId": "s", "Files": [ { "Order": 1, "FilePath": "/app/defs/a.json" },
                                          { "Order": 2, "FilePath": "/app/defs/b.json" } ] }
```

## Environment-variable overrides (CShells precedence: shells.json → shells.{Env}.json → env vars → CLI)

```text
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__SourceId=mounted-definitions
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__FolderPath=/app/workflow-definitions
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__PublishOnReconcile=true
```

Setting these creates the feature section — the feature is enabled by presence (no `Enabled` flag convention).

## Validation matrix (registration time; violation fails shell activation with `InvalidOperationException`)

| SourceId | FilePath | Files | FolderPath | Outcome |
|---|---|---|---|---|
| empty | any | any | any | ✗ SourceId required |
| set | — | — | — | ✗ exactly-one rule (none) |
| set | ✔ | — | — | ✓ single file |
| set | — | ✔ | — | ✓ ordered list |
| set | — | — | ✔ | ✓ folder scan |
| set | ✔ | ✔ | — | ✗ exactly-one rule |
| set | ✔ | — | ✔ | ✗ exactly-one rule |
| set | — | ✔ | ✔ | ✗ exactly-one rule |
| set | ✔ | ✔ | ✔ | ✗ exactly-one rule |

## Folder scan semantics

- Top level of `FolderPath` only (non-recursive; deliberate — mount-implementation symlink trees such as Kubernetes ConfigMap `..data` would double-read under recursion).
- `*.json` files only; other entries ignored.
- Deterministic order: file name, `StringComparer.Ordinal`.
- Missing folder ⇒ startup fails with an error naming the configured path.
- Empty folder / no matches ⇒ startup succeeds; scan result logged at information level.
- Read-only mounts (`:ro`) fully supported — the source only reads.

## Composition prerequisites

- `PublishOnReconcile: true` requires the `WorkflowsPublishing` engine feature active in the same shell (enabled in the default Workbench shells; `DependsOn` chain otherwise). Without it the reconciled event has no publishing subscriber — definitions import but nothing publishes.
- Deployment completion gate: `GET /health/ready` (200 only after shell activation, which includes reconcile + publish). `GET /` returns 200 unconditionally and is **not** a deployment gate.
