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

> *Amended 2026-10-01 ([#2197](https://github.com/elsa-workflows/elsa-foundation/issues/2197); [ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md), D7 and D11 amendments):* the Writer clone is not ff-only and
> may reset onto the remote when every commit it holds that the remote lacks was made by the export identity; it never
> discards a commit anyone else made. Every Writer node runs the export at start, without a lock; a push refused
> because another writer pushed first is rebuilt onto the remote and swept again. The default clone path is per process.

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
