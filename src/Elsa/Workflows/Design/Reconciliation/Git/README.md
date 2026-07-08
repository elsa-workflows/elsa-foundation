# Elsa.Workflows.Design.Reconciliation.Git

GitOps for workflow definitions ([ADR 0034](../../../../../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md),
[spec 085](../../../../../../specs/085-workflow-definition-gitops/spec.md)). Git is a **reconciliation
source + export sink layered on the operational catalog** — never a replacement store, never on the
runtime read path. v1 is **single-writer**.

## What it does

- **Inbound** (`GitWorkflowReconciliationSource`, `SourceKind = "git"`): reads immutable
  `versions/{semver}.json` + mutable `definition.json` from a working clone into the catalog through
  the standard reconciliation seam.
- **Outbound** (`GitWorkflowExporter`, Writer only): a set-diff sweep that writes+commits any catalog
  version missing from git (present ⇒ skip), then pushes fast-forward-only.

## On-disk layout

```
{WorkflowsPath}/                         # default "workflows"
  {definitionId}/
    definition.json                      # { name, description, deleted } — mutable, latest-wins
    versions/
      1.0.0.json                         # indent(canonical WorkflowDefinitionState) — immutable, hashed
      2.0.0.json
```

Content identity is the SHA-256 of the **compact** canonical serialization; the file is its **indented**
form (a pure-whitespace transform via `GitCanonicalJson`), so reviewable diffs never change the hash.

## Roles & clone modes (D11)

| Role | Import | Export | Clone mode |
|---|---|---|---|
| `Consumer` | ✅ read-only | ✕ | disposable mirror (`fetch` + `reset --hard`) |
| `Writer` | ✅ bootstrap + idempotent | ✅ | persistent working copy (`fetch` + **ff-only** merge, never `reset --hard`) |

Single-writer is enforced structurally: a Writer pushes fast-forward-only (a divergent remote is
**refused**, never forced/merged), and the reconciler's Model X tripwire surfaces a same-`(id,version)`
different-content import.

## Configuration (CShells feature `WorkflowsDesignGitReconciliation`)

Enable the feature on a shell. **Do not** enable it in `shells.baseline.json` without a real
`RemoteUrl` — an unreachable remote fails the startup reconcile pass by design.

```jsonc
"WorkflowsDesignGitReconciliation": {
  "RemoteUrl": "git@github.com:acme/workflows.git",
  "Branch": "main",
  "WorkflowsPath": "workflows",
  "Role": "Consumer",                 // Writer | Consumer
  "CredentialsMode": "SshKey",        // SshKey | Token | HostDefault
  "KeyPath": "/run/secrets/deploy_key",
  "Token": "",                         // secret; Token mode only
  "Export": { "PushMode": "Manual", "Branch": "", "Tag": true }   // honored only when Writer
}
```

Credentials are applied as per-invocation `-c …` git config (or a 0600 credential file for Token) so
nothing secret rides the command line; `GIT_TERMINAL_PROMPT=0` guarantees fail-fast on missing creds.

## Boundaries

- Never registered as an `IWorkflowDefinitionStore`; never read during execution (FR-015).
- Drafts never cross the reconciliation boundary (D6).
- Design-only dependencies (no app/runtime reference); the dependency-envelope guard stays green.
