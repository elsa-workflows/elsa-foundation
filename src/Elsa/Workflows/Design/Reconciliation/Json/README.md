# Elsa.Workflows.Design.Reconciliation.Json

Feature `JsonWorkflowReconciliation` — reconciles workflow-definition versions from JSON files on disk
into the design catalog at startup, and (opt-in) publishes them so they are executable when the server
reports ready. This is the file-based / GitOps deployment story (spec 147, issue #1157): mount a folder
of definition files, enable the feature in `shells.json`, start the server — zero API calls.

## What this feature provides

- `JsonWorkflowReconciliationSource : IWorkflowReconciliationSource` (`SourceKind = "Json"`) — contributes
  version envelopes read from JSON files to the workflow reconcile pass.
- `JsonWorkflowCatalogReader` — reads one file into `WorkflowVersionReconciliationModel[]`; every IO/parse
  fault becomes an actionable `InvalidWorkflowCatalogJsonException` naming the path.
- Because the feature extends the abstract `WorkflowsDesignReconciliationFeature`, enabling it also arms
  the reconcile lifecycle: the reconciler, the `[SingleNodeTask]`/`[Order(2)]` startup task, and the
  universal contribution handler.

## Options

Bound under the feature name (`"JsonWorkflowReconciliation": { "Options": { … } }`).

| Option | Required | Semantics |
|---|---|---|
| `SourceId` | yes | Stable identity recorded as the source of the imported definitions. |
| `FilePath` | exactly one of the three | A single JSON file. |
| `Files` | exactly one of the three | Ordered list of `{ "Order": n, "FilePath": "…" }`, read ascending and concatenated. |
| `FolderPath` | exactly one of the three | Directory scanned for `*.json`: **top level only** (non-recursive — mount layouts like Kubernetes ConfigMap `..data` symlink trees would double-read under recursion), deterministic **ordinal file-name order**. Missing folder fails startup naming the path; an empty scan is logged and contributes nothing. |
| `PublishOnReconcile` | no (default `false`) | After a successful pass, the latest reconciled version of each definition this source owns is published in-process (`PublishWorkflow`) — executable at readiness. Requires the `WorkflowsPublishing` feature in the same shell. Idempotent across restarts: a version whose publication slot already holds an active publication of it is skipped. Publish failures are logged per definition and never fail startup. |

Misconfiguration (no `SourceId`, or not exactly one path option) fails shell activation at registration
with an error naming the rule.

## shells.json example (mounted-folder deployment)

```jsonc
"JsonWorkflowReconciliation": {
  "Options": {
    "SourceId": "mounted-definitions",
    "FolderPath": "/app/workflow-definitions",
    "PublishOnReconcile": true
  }
}
```

```bash
docker run -v ./defs:/app/workflow-definitions:ro -v ./my-shells.json:/app/shells.json:ro …
```

Wait on **`GET /health/ready`** (200 only after shell activation, which includes reconcile + publish).
`GET /` returns 200 unconditionally and is **not** a deployment gate.

## Authoring definition files

A file is a top-level JSON array of version envelopes (camelCase):

```jsonc
[
  {
    "definitionId": "wfdef_orders-intake",   // PIN THIS — omitted ids are regenerated per restart → duplicates (the source logs a warning)
    "name": "orders-intake",
    "description": "Intake pipeline",
    "version": "1.0.0",                       // SemVer 2.0.0, author-controlled
    "state": { "rootActivity": { "activityVersionId": "actver_…" /* … */ }, "variables": [], "inputs": [], "outputs": [] },
    "sourceCreatedAt": "2026-08-06T00:00:00Z",
    "deleted": false
  }
]
```

Rules that keep deployments deterministic:

1. **Pin `definitionId`.**
2. **`activityVersionId` must be a resolved catalog id.** Ids are deterministic
   (`ActivityCatalogStableIds`): `"actver_" + base64url(SHA-256(activityTypeKey + U+001F + SemVer sort key))`.
   Precompute offline or read them from a running server's activity catalog. (Hand-authorable
   `(activityTypeKey, version)` pairs are a tracked follow-up — issue #1157 gap 4.)
3. **Versions are immutable.** Same `(definitionId, version)` with different `state` = broken source; the
   reconciler keeps the stored version and logs the mismatch. Ship a new SemVer instead.
4. **Restarts are no-ops** for unchanged files (`(definitionId, SemVer sort key)` existence check; with
   `PublishOnReconcile`, the publication-slot pre-check).
5. **`"deleted": true`** soft-deletes a *source-owned* definition (never a Studio-authored one); deleted
   definitions are never published.

## Constitutional basis

- Reconciliation policy: Model X (§E2.8 / §E2.9.5) — creation-time reconcile, no per-pass mutating fields.
- Publish-on-reconcile seam: the publish step is a Publishing-engine subscriber on
  `OnWorkflowVersionsReconciled` (§2.6.1 independent subscription; Sequential per §2.6.6) — see the
  [reconciliation extension-point catalog](../EXTENSION_POINTS.md) and the
  [publishing engine README](../../../Publishing/README.md).
