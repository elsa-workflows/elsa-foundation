# File-based workflow deployment (spec 147)

Proves the GitOps deployment story end to end: workflow definition JSON files in a folder are
**imported and published — executable — at startup**, with zero API calls (issue #1157, spec
`specs/147-file-workflow-deployment`). The gate is `GET /health/ready` (200 only after shell
activation, which includes the reconcile pass and publish-on-reconcile); `GET /` is not a gate.

| Script | What it exercises |
|---|---|
| `Test-FileBasedDeployment.ps1` | Authors a definition file (pinned `definitionId`, resolved `actver_*` id), restarts the server with `JsonWorkflowReconciliation` composed (`SourceId` + `FolderPath` + `PublishOnReconcile=true`), waits on `/health/ready`, asserts the definition is imported, the publication slot holds an Active publication, and the artifact executes to completion; then restarts with unchanged files and asserts no republish and no duplicate definition (SC-002). |

## Composition mechanism

Unlike other suites, this one needs a feature the stock `shells.json` does not enable (deliberately:
`JsonWorkflowReconciliation` requires a `SourceId` and a path and fails registration on empty options).
The suite composes it **via environment variables** — they layer above `shells.json`, and setting the
section enables the feature — on a server process it manages itself (durability-suite lifecycle:
the already-built `Elsa.Workbench.dll` is launched directly):

```text
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__SourceId
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__FolderPath
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__PublishOnReconcile
```

No repo file is edited; cleanup restarts the server without the feature.

## Caveats

- Manages the server process on port 5095 (stop/start, like `durability/`) — don't run it while
  another suite is mid-flight.
- Requires the standard from-source setup (build + Groundwork schema deploy) per
  [`../README.md`](../README.md).
- Definition ids/names are timestamped per run; the imported definitions remain in the dev SQLite
  catalog afterwards (reconciliation never deletes version rows).
