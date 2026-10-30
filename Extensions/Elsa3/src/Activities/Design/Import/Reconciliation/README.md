# Elsa3.Activities.Design.Import.Reconciliation

Feature `Elsa3JsonReconciliation` imports a folder (or files) of Elsa 3 workflow definitions every time the engine
starts, and publishes them (spec 235). Export the workflows from Elsa 3 Studio, put the files in a folder, point the
host at it and start it: no import call and no Studio step.

It stays the one-way, one-time import of constitution §E2.7: each Elsa 3 version is imported once, a later start finds
it and writes nothing, nothing is translated back, and Elsa 3 never runs.

It runs the Elsa 3 collection import (`Elsa3ImportJsonActivities`) in-process, so the folder and the HTTP import
convert the same way, with the same diagnostics, identities and versions:

- Reusable workflows ("Usable as activity") become graph activities, published dependencies first (spec 246).
- Every other workflow, and one wrapper per reusable workflow, becomes a published workflow definition that keeps its
  Elsa 3 definition id. Elsa 3 version n becomes Elsa 4 version `n.0.0`.

It is its own project because it publishes workflows (Workflows Publishing) and decorates the activity publisher's
authorization context (Workflows Publishing API), which the import itself must not reference.

## Configuration

Compose `Elsa3JsonReconciliation` (it depends on `Elsa3ImportActivitiesPublishing` and `WorkflowsPublishing`) and the
import's persistence provider, today `Elsa3ImportActivitiesEntityFrameworkCore`.

```jsonc
"Elsa3JsonReconciliation": {
  "Options": {
    "SourceId": "elsa3",                        // required; names the input in logs
    "FolderPath": "/app/elsa3-workflows",       // or "FilePath", or "Files": [{ "Order": 0, "FilePath": "…" }]
    "FailOnUnconvertible": false                // true: a skipped workflow fails the start
  }
}
```

Exactly one of `FilePath`, `Files` and `FolderPath`; otherwise the shell refuses to activate. `FolderPath` reads the
folder's top-level `*.json` files in ordinal file-name order. A file holds one Elsa 3 workflow definition (an Elsa 3
Studio export) or a JSON array of them; the upload's size and count limits do not apply. A missing or unreadable
folder or file fails the start, naming the path; an empty folder imports nothing.

## What a start does

The startup task runs at `[Order(4)]`, after activity and workflow reconciliation, so conversion finds the host's
built-in activities in the catalog. It runs on every node, once per persistence scope, and takes no lock.

1. Reads the files. A blank source version id, a source version id that occurs twice, or a definition id and version
   that occur twice are skipped (`ELS3-RECONCILE-DUPLICATE-INPUT`).
2. Analyzes them. A workflow with an error diagnostic is skipped with that diagnostic's code.
3. Applies the rest. A workflow the conversion refuses is skipped with its code, and the rest is applied again. The
   import publishes the reusables.
4. Publishes the newest imported version of each workflow, once.

Every workflow that uses a skipped reusable workflow, directly or through others, is skipped too
(`ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY`, naming the reusable). A workflow whose publication fails stays imported and
is reported (`ELS3-RECONCILE-PUBLISH-FAILED`); the next start tries again. Each skip is logged as a warning with its
code, file, definition id, version and cause, each conversion warning (`ELS3-BEHAVIOUR-*`) as information, and every
start ends with one information line per persistence scope:

```text
Elsa 3 import 'elsa3' in persistence scope 'default': 4 reusable activities and 5 workflows in place; 5 workflows published by this start; 0 skipped.
```

"In place" counts what this start imported (or found imported) and what has been published at least once, by this
start or an earlier one; it does not say which version a slot serves now. A restart with unchanged input reports
`0 workflows published by this start`.

## Restarts and changes

- A start with input that is already imported writes nothing and publishes nothing.
- A new Elsa 3 version of a workflow (a re-export with a higher version) is imported and published; the older one
  stays as a retired version.
- Each imported version is published at most once. If an operator publishes another version in Studio, or rolls back
  to an older imported one, later starts leave it alone; only a new Elsa 3 version supersedes it.
- What a start committed but did not publish, including a reusable the publisher refused, is published by the next
  start.
- Removing a file removes nothing: no definition is deleted, retired or unpublished.
- **Converter upgrades.** Versions are `n.0.0`, so a converter that converts the same Elsa 3 version differently
  meets the import's identity check and the start fails. Until Elsa 4 is in production, a host that imported with an
  earlier converter rebuilds its store after such an upgrade (spec 235 research R-10).

## When the start fails

- `FailOnUnconvertible` is set and a workflow is skipped. A skip found before the commit fails the scope before
  anything is written in it. A reusable the publisher refuses, the workflows it blocks, or a workflow whose
  publication fails are found after the commit: the start fails then, and what was committed and published stays.
- The import refuses something it does not attribute to one workflow: a credential literal in a converted node, a
  changed workflow under an Elsa 3 version already imported (identity collision), or an analysis error that names no
  workflow.

The shell then does not become ready, and the exception (`Elsa3ReconciliationFailedException`) carries the import's
message and guidance.

## Diagnostic codes

| Code | Meaning |
|---|---|
| `ELS3-RECONCILE-DUPLICATE-INPUT` | A blank source version id, or the same source version id or definition id and version more than once in the input. |
| `ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY` | A reusable workflow this workflow uses was skipped or not published. |
| `ELS3-RECONCILE-PUBLISH-FAILED` | The imported workflow could not be published; it stays imported, and the next start tries again. |

Every other skip carries the import's own code; see the
[Elsa 3 mapping README](../../../../Mapping/README.md) and the
[migration readiness guide](../../../../../../../docs/reference/elsa3-migration-readiness.md).

## Cross-domain contributions

- **Tasks:** one `IStartupTask` (`Elsa3FolderImportTask`, Order 4).
- **Workflows Publishing API:** a decorator of `IActivityPublishingAuthorizationContext` (below).
- **Workflows Publishing:** it sends `PublishWorkflow`, and reads the publication policy, slots and journal through
  the Publishing and Runtime core contracts. Resolving a definition's default target slot is copied from
  `PublishReconciledWorkflowVersions`, where it is private; a shared query in `Elsa.Workflows.Publishing.Core` would
  remove the copy.

## Publishing authorization at startup

The activity publisher authorizes each Design row's tenant through `IActivityPublishingAuthorizationContext`, whose
default reads the HTTP user's tenant claim. At startup there is no request. This feature decorates the context: inside
the operation scope the startup task runs its import in, and only there, the scope's tenant (and no tenant) is
granted. Everywhere else the decorated context answers unchanged.
