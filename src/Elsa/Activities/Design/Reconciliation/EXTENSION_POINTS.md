# Extension points — Activities.Design.Reconciliation domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Design.Reconciliation` — the composition root where `CollectActivityVersions` is registered.

---

## Implementable contributor interfaces

### `IActivityReconciliationSource` *(Feature contract — `Elsa.Activities.Design.Reconciliation`)*
- **Kind:** Source (returns reconciliation models — pull pattern).
- **Contract defined in:** `Elsa.Activities.Design.Reconciliation` (this feature project, not a `.Core`). Implementing it requires a reference to this feature.
- **Signature:**
  ```
  string SourceId { get; }
  string SourceKind { get; }
  ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken);
  ```
- **`SourceId`:** a unique, stable identifier for this source (e.g. a file path, assembly name, remote URL).
- **`SourceKind`:** the category of the source (e.g. `"clr"`, `"json"`, `"http"`).
- **Register:** `services.AddScoped<IActivityReconciliationSource, MySource>()`.
- **Consumed by:** `CollectActivityVersions : IEventHandler<OnActivityVersionsReconciling>` (this feature), which injects all sources, reads each, and reconciles the returned activity versions against the catalog.

**Known implementations (shipped):**
- `ClrActivityReconciliationSource` (`Elsa.Activities.Design.Reconciliation.Clr`, `SourceKind = "CLR"`) — scans a configured assembly folder, reads each activity's author-controlled SemVer (the `[Version]` attribute, falling back to the declaring assembly version), and contributes one reconciliation model per discovered activity. Registered as a **standalone source feature** (`ClrActivityReconciliationFeature : IShellFeature`) that does *not* derive from the reconciliation feature — it only adds an `IActivityReconciliationSource` to DI, which the universal handler discovers. Add both features to a shell to scan a folder.
- `Elsa3.Activities.Design.Import` — *(cross-domain — imports activity definitions from Elsa3 JSON; provides IActivityCollectionJsonSource-backed reconciliation)* — verify exact class name in that project.

---

## Events

`CatalogParityTests` scans `Elsa.Activities.Design.Reconciliation.Core` for `IEvent` types and asserts alignment with `### On…` headings here.

### OnActivityVersionsReconciling
`(ICollection<IActivityDefinitionVersion> Versions)`

**Semantic.** The activity catalog reconciliation pass is running. Sources contribute their activity versions to the `Versions` collection; the reconciler diffs against the stored catalog.

**Delivery strategy.** Sequential — all versions must be contributed before reconciliation proceeds.

**Publication site.** `ActivityVersionReconcilerStartupTask` (`Elsa.Activities.Design.Reconciliation`) — a startup task.

**Expected handler.** Exactly one: `CollectActivityVersions` (this feature).

---

## Cross-references

- Activity catalog persistence: [`Elsa.Activities.Design.Persistence.EFCore/EXTENSION_POINTS.md`](../Elsa.Activities.Design.Persistence.EFCore/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
