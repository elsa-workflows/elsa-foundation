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
  ValueTask<IEnumerable<WorkflowVersionReconciliationModel>> Read(CancellationToken cancellationToken);
  ```
- **`SourceId`:** a unique, stable identifier for this source.
- **`SourceKind`:** the category of the source (e.g. `"code-first"`, `"json"`, `"yaml"`).
- **Register:** `services.AddScoped<IWorkflowReconciliationSource, MySource>()`.
- **Consumed by:** `WorkflowVersionsReconcilingHandler : IEventHandler<OnWorkflowVersionsReconciling>` (this feature), which injects all sources, reads each, and reconciles against the workflow catalog.

**Known implementations (shipped):**
- None currently in-repo. Add a source to integrate a code-first workflow definition provider (e.g. attribute-decorated C# classes) or a file-based import.

---

## Events

`CatalogParityTests` scans `Elsa.Workflows.Design.Reconciliation.Core` for `IEvent` types and asserts alignment with `### On…` headings here.

### OnWorkflowVersionsReconciling
`(ICollection<IWorkflowDefinitionVersion> Versions)`

**Semantic.** The workflow catalog reconciliation pass is running. Sources contribute their workflow versions; the reconciler diffs against the stored catalog.

**Delivery strategy.** Sequential.

**Publication site.** `WorkflowsVersionReconcilerStartupTask` (`Elsa.Workflows.Design.Reconciliation`) — a startup task.

**Expected handler.** Exactly one: `WorkflowVersionsReconcilingHandler` (this feature).

---

## Cross-references

- Workflow catalog persistence: [`Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Persistence.Groundwork/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
