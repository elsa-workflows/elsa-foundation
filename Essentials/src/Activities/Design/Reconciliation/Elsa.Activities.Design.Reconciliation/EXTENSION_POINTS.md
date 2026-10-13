# Extension points — Activities.Design.Reconciliation domain

The per-domain catalog (framework §2.22.1). Anchored at `Elsa.Activities.Design.Reconciliation` — the composition root where `CollectActivityVersions` is registered.

---

## Implementable contributor interfaces

### `IActivityReconciliationSource` *(Core contract — `Elsa.Activities.Design.Core`)*
- **Kind:** Source (returns reconciliation models — pull pattern).
- **Contract defined in:** `Elsa.Activities.Design.Core` (namespace `Elsa.Activities.Design.Core.Reconciliation`). Implementing a source needs only that package; this feature supplies the handler that consumes it.
- **Signature:**
  ```
  string SourceId { get; }
  string SourceKind { get; }
  ValueTask<IEnumerable<ActivityVersionReconciliationModel>> Read(CancellationToken cancellationToken);
  ```
- **`SourceId`:** a unique, stable identifier for this source (e.g. a file path, assembly name, remote URL).
- **`SourceKind`:** the category of the source (e.g. `"CLR"`, `"Json"`, `"http"`).
- **Register:** `services.AddScoped<IActivityReconciliationSource, MySource>()`.
- **Stable ids:** `ActivityCatalogStableIds` (same package) computes the version id the handler assigns to an entry, and the definition id when the entry leaves `Id` unset, so a source can reference an activity it contributes before reconciliation runs.
- **Consumed by:** `CollectActivityVersions : IEventHandler<ActivityVersionsReconciling>` (this feature), which injects all sources, reads each, and reconciles the returned activity versions against the catalog.

**Known implementations (shipped):**
- `ClrActivityReconciliationSource` *(intra-domain — default)* (`Elsa.Activities.Design.Reconciliation.Clr`, `SourceKind = "CLR"`) — scans a configured assembly folder, reads each activity's author-controlled SemVer (the `[Version]` attribute, falling back to the declaring assembly version), and contributes one reconciliation model per discovered activity. Registered as a **standalone source feature** (`ClrActivityReconciliationFeature : IShellFeature`) that does *not* derive from the reconciliation feature — it only adds an `IActivityReconciliationSource` to DI, which the universal handler discovers. Add both features to a shell to scan a folder.
- `JsonActivityReconciliationSource` *(intra-domain — default)* (`Elsa.Activities.Design.Reconciliation.Json`, `SourceKind = "Json"`) — reads activity definitions from configured JSON files. Registered by the standalone `JsonActivityReconciliationFeature`, which likewise does *not* derive from the reconciliation feature.

---

## Events

Every `IEvent` type in the `Elsa.Activities.Design.Core.Reconciliation` namespace has a `### <EventClassName>` heading here, and every such heading names one; review keeps the two in step.

### ActivityVersionsReconciling
`(ICollection<IActivityDefinitionVersion> Versions)`

**Semantic.** The activity catalog reconciliation pass is running. Sources contribute their activity versions to the `Versions` collection; the reconciler diffs against the stored catalog.

**Delivery strategy.** Sequential — all versions must be contributed before reconciliation proceeds.

**Publication site.** `ActivityVersionReconcilerStartupTask` (`Elsa.Activities.Design.Reconciliation`) — a startup task.

**Expected handler.** Exactly one: `CollectActivityVersions` (this feature).

---

## Cross-references

- Activity catalog persistence: [`Elsa.Activities.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md`](../Elsa.Activities.Design.Persistence.EntityFrameworkCore/EXTENSION_POINTS.md).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 + §2.22.1.
