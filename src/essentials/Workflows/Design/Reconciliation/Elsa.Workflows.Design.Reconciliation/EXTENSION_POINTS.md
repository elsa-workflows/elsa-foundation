# Extension points — Workflows.Design.Reconciliation domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Workflows.Design.Reconciliation` — the composition root where `WorkflowVersionsReconcilingHandler` is registered.

---

## Implementable contributor interfaces

### `IWorkflowReconciliationSource` *(Feature contract — `Elsa.Workflows.Design.Reconciliation`)*
- **Kind:** Source (returns reconciliation models — pull pattern).
- **Contract defined in:** `Elsa.Workflows.Design.Reconciliation` (this feature project, not a `.Core`). Implementing it requires a reference to this feature.
- **Signature:**
  ```
  string SourceId { get; }
  string SourceKind { get; }
  bool RequestsPublication => false;   // default interface member (spec 147)
  ValueTask<IEnumerable<WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken);
  ```
- **`SourceId`:** a unique, stable identifier for this source.
- **`SourceKind`:** the category of the source (e.g. `"code-first"`, `"json"`, `"yaml"`).
- **`RequestsPublication`:** opt-in publish-on-reconcile (spec 147). A source returning `true` asks for the latest reconciled version of each definition it contributes to be published after a successful pass; the flag is snapshotted per contribution onto `WorkflowVersionSourceClaim.PublishRequested` and acted on by the Publishing-side subscriber of `WorkflowVersionsReconciled` (below). Defaults to `false` — import-only.
- **Register:** `services.AddScoped<IWorkflowReconciliationSource, MySource>()`.
- **Consumed by:** `WorkflowVersionsReconcilingHandler : IEventHandler<WorkflowVersionsReconciling>` (this feature), which injects all sources, reads each, and reconciles against the workflow catalog.

**Known implementations (shipped):**
- `GitWorkflowReconciliationSource` (`Elsa.Workflows.Design.Reconciliation.Git`, `SourceKind = "git"`) — reads immutable version files from a git repository (spec 085 / ADR 0034). Feature: `WorkflowsDesignGitReconciliation`. On a Writer node whose clone diverged from the remote, `Read` does not throw (#2197): the clone resets to the remote when its unpushed commits are all export commits, and is otherwise left as it is with an error logged; see the [Git reconciliation README](../Git/README.md#several-writer-nodes-of-one-catalog).
- `JsonWorkflowReconciliationSource` (`Elsa.Workflows.Design.Reconciliation.Json`, `SourceKind = "Json"`) — reads workflow-definition versions from JSON files (exactly one of a single `FilePath`, an ordered `Files` list, or a scanned `FolderPath`; required `SourceId`; optional `PublishOnReconcile` → `RequestsPublication`), mirroring the Activities-side `JsonActivityReconciliation` source. Feature: `JsonWorkflowReconciliation` (opt-in; not enabled in any default shell).
- Add a further source to integrate other providers (e.g. a code-first attribute-decorated C# provider, or a CRM pull).

---

## Events

`CatalogParityTests` scans the `Elsa.Workflows.Design.Core.Reconciliation` namespace for `IEvent` types and asserts alignment with `### On…` headings here.

### WorkflowVersionsReconciling
`(ICollection<IWorkflowDefinitionVersion> Versions, ICollection<WorkflowVersionSourceClaim> Claims)`

**Semantic.** The workflow catalog reconciliation pass is running. Sources contribute their workflow versions; the reconciler diffs against the stored catalog. `Claims` (spec 147) carries one provenance record per contributed version — `(DefinitionId, Version, SemVerSortKey, SourceId, SourceKind, PublishRequested, Deleted)` — populated by the aggregating handler beside `Versions`; source identity is not persisted on design entities, so this is the only carrier that survives the pass.

**Version ids.** The aggregating handler gives each contributed version the id `WorkflowReconciliationVersionIds.For(definitionId, semVerSortKey)` derives, never a generated one (#2189). Two nodes reconciling the same source therefore send identical materialization requests, and the one that writes second replays the first one's write instead of failing its shell start. A version already stored under a generated id keeps it: the reconciler finds existing versions by definition and sort key and does not materialize them again.

**Rolling-upgrade window.** A node still on a release from before #2189 generates a random version id and fingerprints a definition's `DeletedAt` time. If it and an upgraded node materialize the same new definition or version at the same moment, their requests differ and conflict once: the shell start of whichever writes second fails once, and restarting it succeeds, because by then the definition and version exist and are not materialized again. Once every node runs the new release, the window is closed.

**Sources must supply `DefinitionId`.** Convergence across nodes rests on every node deriving the same ids, and the version id is derived from the definition id. When an entry omits `DefinitionId`, `WorkflowDefinitionFactory` falls back to a generated id, which differs per node, so two nodes importing that entry still conflict (and each start imports it as a new definition). A source meant for a cluster must therefore set `DefinitionId` on every entry. The git source always does (the definition's folder name); the JSON source logs a warning for an entry without one.

**Delivery strategy.** Sequential.

**Publication site.** `WorkflowsVersionReconcilerStartupTask` (`Elsa.Workflows.Design.Reconciliation`) — a startup task.

**Expected handler.** Exactly one: `WorkflowVersionsReconcilingHandler` (this feature).

### WorkflowVersionsReconciled
`(IReadOnlyList<WorkflowVersionSourceClaim> Claims)`

**Semantic.** A workflow-version reconcile pass completed with every contributed version materialized, verified present, or skipped: as outdated, or refused by the credential-literal rule (spec 188, FR-008). Not published when the pass aborts. Payload is the claims of the versions the pass reconciled, in contribution order; a skipped version's claim is left out.

**Delivery strategy.** Sequential (`IInlineEventPublisher`) — subscribers run inside the reconcile startup task, before shell activation completes (and therefore before `/health/ready` turns ready). The task takes no lock and runs on every node (#2192), so subscribers on several nodes can run at once and must converge as the reconciler's own writes do (#2189).

**Dispatcher failure policy.** No exception shielding: a subscriber throw fails the reconcile pass and shell activation. **Subscribers MUST NOT throw** — recoverable per-item failures are logged and swallowed by the subscriber itself.

**Publication site.** `WorkflowsVersionReconciler.Reconcile` (this feature), after the per-version loop completes without error.

**Expected handler audiences.** Independent subscribers reacting to completed reconciliation — publication, cache invalidation, telemetry. Not a fan-in contribution event: no contributor interface, no aggregating-handler constraint.

**Known subscribers:**
- `PublishReconciledWorkflowVersions` *(cross-domain — `Elsa.Workflows.Publishing`)* — publishes the latest reconciled version of each definition whose claim has `PublishRequested = true` via the in-process `PublishWorkflow` request; idempotent (publication-slot pre-check + the publish handler's unchanged-artifact replay), per-definition failure isolation, never throws (spec 147).

---

## Cross-references

- Workflow catalog persistence: [`Elsa.Workflows.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
