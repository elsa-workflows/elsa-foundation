# Contract: `Elsa3JsonReconciliation` feature

## Shell feature

| Item | Value |
|---|---|
| Feature id | `Elsa3JsonReconciliation` |
| DependsOn | `Elsa3ImportActivitiesPublishing`, `WorkflowsPublishing` (transitively `Elsa3ImportJsonActivities`, `Elsa3Mapping`, `WorkflowsPublishingApi`) |
| Startup task | `Elsa3JsonReconciliationStartupTask`, `[Order(4)]` |
| Endpoints | none |

## Configuration

```jsonc
"Elsa3JsonReconciliation": {
  "Options": {
    "SourceId": "elsa3-demo",
    "FolderPath": "/app/elsa3-workflows",   // or "FilePath", or "Files": [{ "Order": 0, "FilePath": "…" }]
    "FailOnUnconvertible": false
  }
}
```

Misconfiguration (no `SourceId`, not exactly one path option) fails shell activation at registration with an error
naming the rule. A missing folder or file, or a file that is not an Elsa 3 definition or array of them, fails the
start naming the path. An empty folder is logged and imports nothing.

## File format

A file holds one Elsa 3 workflow definition (the Elsa 3 Studio export of one workflow) or a JSON array of them (the
shape the collection upload accepts).

## Logs

- One warning per skipped item: `{Code}`, `{SourceId}`, `{FilePath}`, `{DefinitionId}`, `{Version}`, `{Cause}`,
  `{BlockedBy}`.
- One information line per conversion warning (`ELS3-BEHAVIOUR-*`) of an imported item.
- One summary per persistence scope: `{SourceId}`, `{ImportedActivities}`, `{ImportedWorkflows}`,
  `{PublishedWorkflows}`, `{Skipped}`.

## Strict mode

With `FailOnUnconvertible: true`, the start fails with `Elsa3ReconciliationFailedException`, listing the skipped
items; items skipped before the commit fail it before anything is written.

## Guarantees

- Same engine as the HTTP collection import: identities, versions `{n}.0.0`, conversion rules and diagnostics.
- A restart with the same input writes and publishes nothing.
- Removing a file removes nothing.
