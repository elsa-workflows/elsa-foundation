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
  version missing from git (present ⇒ skip), then pushes fast-forward-only. It decides from git state,
  not from files on disk: what HEAD's tree holds, which tags HEAD's history carries, and whether HEAD is
  ahead of the remote ([#2197](https://github.com/elsa-workflows/elsa-foundation/issues/2197)). A pass
  that stopped after a write, after a commit, or at a failed push is completed by the next one.

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
| `Writer` | ✅ bootstrap + idempotent | ✅ | persistent working copy (`fetch`; keeps its unpushed export commits while the remote has not moved, otherwise is brought to the remote, see below) |

Single-writer is enforced structurally: a Writer pushes fast-forward-only (a divergent remote is
**refused**, never forced/merged), and the reconciler's Model X tripwire surfaces a same-`(id,version)`
different-content import.

### Several Writer nodes of one catalog

Replicas that share a configuration are all Writers, each with a clone of its own. That is supported
([ADR 0034](../../../../../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md),
D7 and D11 as amended for [#2197](https://github.com/elsa-workflows/elsa-foundation/issues/2197)): one
writer per repository branch is kept by the remote, not by a lock or an election.

- **Every Writer node exports at shell start**, without a lock; the export task is not a `[SingleNodeTask]`.
- **The push is the fence.** Only a fast-forward is accepted, so the branch advances by one writer's
  commits at a time. A writer whose push is refused because another writer pushed first resets onto the
  remote and sweeps again (at most `GitWorkflowExporter.MaxPushAttempts` times a pass), normally finding
  nothing left to commit. A push refused for any other reason still fails the pass.
- **A clone that cannot be moved never fails a start.** On every pass (import and export alike) a Writer clone that
  is only behind fast-forwards; one that diverged resets to the remote when all its unpushed commits were
  made by the export identity (author name, author email and committer email all
  `Elsa Design <design@elsa.local>`, so a human's amend or rebase of an export commit counts as theirs),
  since the export regenerates them from the catalog, and otherwise is left untouched with an error in
  the log, once per clone per process, because a commit anyone else made there is never discarded.
  Uncommitted changes under `WorkflowsPath` are residue of an interrupted export and are discarded at
  every pass, so keep no work of your own there. Uncommitted changes elsewhere in the clone are kept,
  and a move to the remote that would overwrite one is refused the same way: the clone stays as it
  stands, with an error in the log, and the start goes on. A clone left behind the remote ends the
  pass's rebuilding with one warning; the export's push is refused until the clone is reconciled by hand.
- **Push modes.** Only `Immediate` has a fence. With `Manual` the push happens out-of-band from one
  clone; the others discard their own export commits once that push moves the remote.
- **A distinct `Export.Branch`.** The writer rebuilds only onto the branch it tracks. A push to another
  export branch that was refused because that branch moved is logged as an error, without failing the
  start, until the export branch is brought back in line with the tracked one.
- **One clone per process.** The default `LocalCachePath` is a per-source directory under the OS temp
  dir that includes the process id, so two processes on one machine never share a clone and collide on
  its `index.lock`. The cost is that a default clone is not reused across restarts: the next process
  clones again. Set `LocalCachePath` to keep a clone, and give each process its own.
- **`WorkflowsPath` is validated.** It must be a relative folder path inside the repository: not empty,
  not rooted, with no `.` or `..` segment. It reaches git as the pathspec of `clean -f -d` and
  `restore`, so the feature refuses to register with anything else.

## Configuration (CShells feature `WorkflowsDesignGitReconciliation`)

Enable the feature on a shell. **Do not** enable it in `shells.baseline.json` without a real
`RemoteUrl` — an unreachable remote fails the startup reconcile pass by design.

```jsonc
"WorkflowsDesignGitReconciliation": {
  "RemoteUrl": "git@github.com:acme/workflows.git",
  "Branch": "main",
  "WorkflowsPath": "workflows",      // relative, inside the repo
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
