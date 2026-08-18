# Elsa.Workflows.Runtime.Reconciliation

Import lifecycle for **portable workflow-executable artifacts** (spec 151). It lets a runtime that has no
design surface, no publishing surface and no compiler run workflows it was *handed* rather than ones it built:
a closure envelope is dropped on a mount, and at shell activation the engine validates it, persists its
artifacts and activates them.

The envelope is produced elsewhere — by a publish-capable engine's
`GET publishing/workflows/{versionId}/executable-export` — and carries the pinned executable plus its complete
transitive dependency closure, the exporting engine's Published-scope source references, and the trigger
bindings those artifacts own. It is self-contained by construction: an envelope whose dependency edges only
resolve because the *importing* store already happens to hold a child is a broken export and is refused.

Mirrors the design-side [`Elsa.Workflows.Design.Reconciliation`](../../Design/Reconciliation/README.md) —
sources contribute desired state, a reconciler diffs and converges — but one layer down: the unit here is a
compiled artifact closure, not a workflow-definition version, and the two compose independently. An engine may
enable design-side reconciliation, artifact reconciliation, both, or neither.

## What a pass does

The reconciler runs one pass over every registered source. Per closure unit, in this order:

1. **parse + format gate** — an unknown or newer `formatVersion` is refused outright; there is no upcast and no
   partial import of the members a newer envelope happens to share with this build;
2. **closure validation against the envelope alone** — missing child, declared-hash mismatch, duplicate
   identity, cycle. The store is deliberately not consulted, so the same file fails identically on every
   runtime;
3. **content-hash recompute** — each member's canonical hash must equal the id it claims (ADR 0038). The
   executable store is create-only and dedups by id, so persisting an unverified payload would let a corrupted
   file *become* that id's content;
4. **requirements gate** — consumer capabilities, durable-value storage drivers, **and** CLR activity-type
   presence. An artifact this runtime cannot execute is rejected at import with a diagnostic naming what is
   missing, rather than faulting on first activation;
5. **idempotency + supersession** — same artifact already serving → no-op; an `ArtifactVersion` that sorts at or
   below the live one → skipped (latest-wins by SemVer sort key; an unparseable version is rejected);
6. **one activation request** to Runtime's `IWorkflowActivationCoordinator`.

**Every gate completes for the whole unit before the first write.** A unit that fails any gate rejects all its
members and writes nothing, while every other unit in the batch still reconciles — one broken export cannot
take a deploy down. Rejections are entries on the returned `WorkflowArtifactReconciliationResult` and are
logged as warnings by the startup task; they never throw. Only a pass-aborting condition — a configured folder
that does not exist — propagates as `WorkflowArtifactReconciliationException` and fails shell activation.

**The feature owns no activation machinery.** It never takes a lease, writes a projection, notifies an observer
or compensates: `IWorkflowActivationCoordinator` owns that entire sequence for the publish path and the import
path alike, and a second copy here would be exactly the duplicated authority the shared coordinator exists to
remove. The importer also keeps no journal — its recovery unit is the next pass.

## Enabling it

Feature id: **`JsonWorkflowArtifactReconciliation`**. Opt-in; not composed in any default shell.

```jsonc
// inside CShells.Shells.<shell>.Features
{
  "Tasks": {},
  "ActivitiesRuntime": {},                 // registers activity types; the pass orders itself after this
  "WorkflowsRuntimeApi": {},               // or any composition that arms AddWorkflowRuntime()
  "WorkflowsRuntimeTriggers": {},          // required — see below
  "FileSystemDistributedLocking": {},      // required — see below (any Elsa.Locking.* provider)

  "JsonWorkflowArtifactReconciliation": {
    "Options": {
      "SourceId": "prod-artifact-drop",    // required; also the activation ownership descriptor
      "FolderPath": "/mnt/artifacts"       // or "FilePath", or an ordered "Files" list — exactly one
      // "TenantId": "tenant-a"            // optional; null = the untenanted engine
    },
    "StartupTaskOptions": {
      "LockTimeoutMs": 5000                // optional; default 5000
    }
  }
}
```

Note the `Options` nesting — feature settings bind as
`CShells:Shells:<shell>:Features:JsonWorkflowArtifactReconciliation:Options:<name>`, matching
`JsonWorkflowReconciliation` on the design side. `StartupTaskOptions` sits beside `Options`, not inside it,
because it comes from the abstract base feature.

Add a Groundwork runtime persistence feature for durability; without one the executable store, source-reference
store and activation authority are all in-memory and every restart re-imports from scratch.

### Options

| Option | Meaning |
|---|---|
| `Options.SourceId` | **Required.** The source's stable identity — and the **activation ownership descriptor**: the activation authority refuses a transition requested by a source that does not own the definition. Keep it stable across restarts; a path-derived value would make this source's own definitions look foreign the moment the mount moved. |
| `Options.FilePath` | Single-file shorthand. |
| `Options.Files` | Ordered `{ "Order": n, "FilePath": "…" }` entries; ascending `Order`, ordinal file-name tie-break. Use when the import must be staged. |
| `Options.FolderPath` | The mounted-volume / GitOps shape. Top level only (**non-recursive**, so a ConfigMap's `..data` symlink tree is not read twice), `*.json`, ordinal name order. A *missing* folder aborts the pass; an existing folder with no matches is a logged no-op — that distinction is what stops an unmounted volume from looking like a healthy empty one. |
| `Options.TenantId` | The tenant stamped on every minted source reference. `null` = untenanted. One source serves one tenant; per-tenant fan-out is deferred, so a second tenant needs a second source. |
| `StartupTaskOptions.LockTimeoutMs` | How long the pass waits for the distributed reconcile lock (default `5000`). Losing the race is the expected outcome on all but one node, not a failure. |

Exactly one of `FilePath` / `Files` / `FolderPath` must be set, and `SourceId` must be non-empty; both are
validated in `ConfigureServices` and throw `InvalidOperationException` at registration.

## `WorkflowsRuntimeTriggers` is required, and why

`JsonWorkflowArtifactReconciliationFeature` declares `DependsOn = { "Tasks", "WorkflowsRuntimeTriggers" }`.
`AddWorkflowRuntime()` alone does **not** register the trigger binding/schedule/indexer spine — that feature
does. Without it the activation coordinator refuses to activate anything, which is correct: an imported timer-
or HTTP-started workflow with no trigger projection is a definition that is live and can never fire. A loud
refusal at boot beats a workflow that silently never runs.

## Locking is required

The reconcile pass is a `[SingleNodeTask]` guarded by `IDistributedLockProvider`, and **no default
`IDistributedLockProvider` is registered anywhere in the framework — deliberately.**

A process-local stand-in would satisfy DI, behave perfectly on one node, and then silently let two nodes
reconcile the same mount concurrently — the exact condition the single-node guard exists to prevent. The
absence of a default is the safety property, not an oversight: composing without a locking feature fails at
container validation and at boot, and cannot be shipped past.

Compose any `Elsa.Locking.*` provider. `FileSystemDistributedLocking` is sufficient for a single host; a
multi-node deployment needs a genuinely distributed one. This is **not** expressed as a `DependsOn` because
that would pin one provider choice — the design-side reconcilers carry the identical requirement the same way.

## Composition and registrations

`WorkflowsArtifactReconciliationFeature` is `public abstract`, carries no `[ShellFeature]` attribute and is
deliberately not sealed: source-variant features extend it (§2.5), which is why enabling *any* concrete
reconciliation feature arms the lifecycle exactly once, however many sources are composed.

- **Base (`WorkflowsArtifactReconciliationFeature`)** — `AddWorkflowRuntime()` (idempotent, ADR 0029); each
  host-supplied `Sources` entry as a singleton; `StartupTaskOptions`; `IWorkflowArtifactReconciler` →
  `WorkflowArtifactReconciler` (scoped); `IStartupTask` → `WorkflowArtifactReconcilerStartupTask` (scoped).
- **JSON variant (`JsonWorkflowArtifactReconciliationFeature`)** — validates its options, calls
  `base.ConfigureServices`, then adds `Options`; `IWorkflowArtifactClosureReader` →
  `JsonWorkflowArtifactClosureReader` (scoped); `IWorkflowArtifactReconciliationSource` →
  `JsonWorkflowArtifactReconciliationSource` (scoped).

**Event handlers:** none. Unlike the design-side reconciler, this feature fans its sources in by resolving
`IEnumerable<IWorkflowArtifactReconciliationSource>` directly and returns its report as a value; it publishes no
`IEvent` and subscribes to none.

### Cross-domain contributions

- **`IStartupTask`** *(Core — `Elsa.Tasks.Core`)* — `WorkflowArtifactReconcilerStartupTask`. Catalog:
  [`Elsa.Tasks/EXTENSION_POINTS.md`](../../../Tasks/EXTENSION_POINTS.md)

### Registered tasks and cadence

| Task | Cadence |
|---|---|
| `WorkflowArtifactReconcilerStartupTask` | Once per shell activation, **before readiness** (`/health/ready` does not turn ready until the pass finishes), and again on every shell reload — which is why re-reconciliation needs no new trigger machinery. `[SingleNodeTask]` under a distributed lock; `[TaskDependency(typeof(RegisterActivityTypesStartupTask))]`, because the requirements gate's CLR-type axis would answer "not yet" for every type before the assembly scan populates the well-known type registry, rejecting every artifact on a cold start. |

## Rolling out a new version

Copy the new closure onto the mount and reload the shell. The higher `ArtifactVersion` becomes active, the
predecessor's activation is cleared and its minted reference retired with reason `activation-replaced`;
in-flight instances of the old version finish on it. Re-running over an unchanged mount is a no-op — the store
is content-addressed and create-only and the slot transition is revision-checked.

On a combined engine that both publishes and imports, the activation slot's explicit `WorkflowActivationSource`
decides conflicts: the same artifact arriving by both routes is an idempotent no-op, while a *different*
artifact from the non-owning source is rejected with a diagnostic naming the owning source. Ownership is read
from that field only — never inferred from an id prefix.

## See also

- Extending this feature (source contract, reconciler/reader replacement, the inheritance point):
  [EXTENSION_POINTS.md](EXTENSION_POINTS.md).
- Activation, the requirements checker, the executable hasher and the closure codec this feature consumes:
  [Runtime extension points](../EXTENSION_POINTS.md).
- The export side that produces these envelopes:
  [Publishing API extension points](../../Publishing/Api/EXTENSION_POINTS.md).
- [ADR 0038](../../../../../docs/adr/0038-artifact-hash-is-purely-behavioral-and-executables-are-content-addressed.md)
  — why recomputing the hash before persistence is meaningful.
