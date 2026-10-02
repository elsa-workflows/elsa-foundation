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
section enables the feature. It does so on a server process the suite owns (`../_ServerLifecycle.ps1`:
the already-built `Elsa.Workbench.dll` is launched directly from a temporary content root, on a free loopback
port unless `-BaseUrl` names one):

```text
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__SourceId
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__FolderPath
CShells__Shells__default__Features__JsonWorkflowReconciliation__Options__PublishOnReconcile
```

No repo file is edited, and the variables are set for the owned server only, not left in your shell.

## Caveats

- The suite starts, restarts and stops its own server, and only that process. It never stops a server you
  started, so it can run while yours is up. If you pass `-BaseUrl` and that port is held by another process,
  the suite fails with the port and the pid in the message instead of stopping it (issue #2329).
- Requires a built Workbench (`dotnet build src/apps/Elsa.Workbench/Elsa.Workbench.csproj`); the EF modules
  migrate on shell activation. See [`../README.md`](../README.md).
- Definition ids/names are timestamped per run. They land in the SQLite files of the temporary content root,
  which is removed after a passing run and kept, with the server logs, after a failing one.
