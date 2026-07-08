# Quickstart — Workflow-Definition GitOps (085)

## Consumer: import versions authored elsewhere
Add the feature to a shell in `shells.*.json`, `Role = Consumer`:
```jsonc
"WorkflowsDesignGitReconciliation": {
  "RemoteUrl": "git@github.com:acme/workflows.git",
  "Branch": "main",
  "WorkflowsPath": "workflows",
  "Role": "Consumer",
  "CredentialsMode": "SshKey",
  "KeyPath": "/run/secrets/deploy_key"
}
```
On startup the reconcile pass clones/mirrors the repo (`fetch` + `reset --hard`), reads every
`workflows/{id}/versions/{semver}.json` + `definition.json`, and upserts versions into the catalog with
commit-time `SourceCreatedAt`. Re-runs are idempotent. The catalog stays the only runtime read path.

## Writer: author in Studio, mirror to git
Exactly one node, `Role = Writer`:
```jsonc
"WorkflowsDesignGitReconciliation": {
  "RemoteUrl": "git@github.com:acme/workflows.git",
  "Branch": "main",
  "Role": "Writer",
  "CredentialsMode": "SshKey",
  "KeyPath": "/run/secrets/deploy_key",
  "Export": { "PushMode": "Immediate", "Tag": true }
}
```
The Writer clone is a persistent working copy (`fetch` + **ff-only** integrate, never `reset --hard`).
The export task writes+commits any catalog version missing from git as `indent(canonical State)`, refreshes
`definition.json`, and (Immediate) pushes ff-only. A divergent remote refuses the push — no force, no merge.

## Verify locally (tests)
```bash
dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj \
  --filter "FullyQualifiedName~Reconciliation"
dotnet test tests/Elsa/Workflows/Design/Reconciliation/Git/... # new git test project
```
Round-trip smoke: seed a temp bare repo with two versions + `definition.json`; run a Consumer pass →
both `WorkflowDefinitionVersion` rows exist; run a Writer export → the same files re-appear and a second
pass is a no-op.

## Invariants (don't regress)
- Git is never an `IWorkflowDefinitionStore` and never read during execution (FR-015).
- Drafts never cross the boundary (D6).
- Single-writer enforced by ff-only push + the Model X hash tripwire (D7); no silent divergence.
- Content identity is the canonical serialization, not raw bytes (D3).
