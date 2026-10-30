# Contract: `Elsa3JsonReconciliation` feature

## Shell feature

| Item | Value |
|---|---|
| Feature id | `Elsa3JsonReconciliation` |
| DependsOn | `Elsa3ImportActivitiesPublishing`, `WorkflowsPublishing` (transitively `Elsa3ImportJsonActivities`, `Elsa3Mapping`, `WorkflowsPublishingApi`) |
| Also compose | the import's persistence provider, today `Elsa3ImportActivitiesEntityFrameworkCore` |
| Startup task | `Elsa3FolderImportTask`, `[Order(4)]` |
| Decorates | `IActivityPublishingAuthorizationContext` (scoped grant for the task's own operation scope) |
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
naming the rule. A missing folder or file, a file that is not valid JSON, a file whose root is neither an object nor an array, or an
array with a null entry fails the start naming the path. A JSON object that is not a workflow definition (no root
activity) is read, and the analysis skips it. An empty folder is logged and imports nothing.

## File format

A file holds one Elsa 3 workflow definition (the Elsa 3 Studio export of one workflow) or a JSON array of them (the
shape the collection upload accepts). The upload's size and count limits do not apply.

## Logs

- One warning per skipped item: `{Code}`, `{SourceId}`, `{FilePath}`, `{DefinitionId}`, `{Version}`, `{Cause}`,
  `{BlockedBy}`.
- One information line per conversion warning (`ELS3-BEHAVIOUR-*`) of an imported item.
- One summary per persistence scope (Information): `{SourceId}`, `{Scope}`, `{Reusables}` and `{Workflows}` in place,
  `{Published}` by this start, `{Skipped}`.
- A workflow whose publication fails: warning, then a skip with `ELS3-RECONCILE-PUBLISH-FAILED`.

## Strict mode

With `FailOnUnconvertible: true`, the start fails with `Elsa3ReconciliationFailedException`, listing the skipped
items; items skipped before the commit fail it before anything is written in that persistence scope. Publication
refusals and failures are found after the commit, so they fail the start after it, and what was committed stays.

## Always fatal

A credential-literal refusal and an identity collision (a changed workflow under an Elsa 3 version already imported)
fail the start in either mode, with the import's message and guidance: the import does not attribute them to one item.

## Guarantees

- Same engine as the HTTP collection import: identities, versions `{n}.0.0`, conversion rules and diagnostics.
- A restart with the same input writes and publishes nothing.
- Each imported workflow version is published at most once, so a slot an operator moved to another version is never
  taken back. A new Elsa 3 version supersedes whatever is active.
- Removing a file removes nothing.
