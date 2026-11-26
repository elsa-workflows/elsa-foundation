# Extension points — Workflows.Design.Reconciliation.Json

A reconciliation **source** module (§2.6.1). It contributes to the workflow reconciliation lifecycle anchored at
`Elsa.Workflows.Design.Reconciliation`; it owns no catalog of its own beyond its JSON file format.

---

## Contributions to other domains

### `IWorkflowReconciliationSource` *(contract — `Elsa.Workflows.Design.Reconciliation`)*
- **Implementation:** `JsonWorkflowReconciliationSource` (`SourceKind => "Json"`).
- **What it does:** reads exactly one of `FilePath`, `Files` (ascending `Order`) or `FolderPath` (top-level `*.json`,
  ordinal file-name order) through `IJsonWorkflowCatalogReader` and returns the concatenated
  `WorkflowVersionReconciliationModel`s. With `PublishOnReconcile`, it requests publication of the latest reconciled
  version of each definition it owns.
- **Register:** `services.AddScoped<IWorkflowReconciliationSource, JsonWorkflowReconciliationSource>()`, plus one per
  `AdditionalSources` entry (done by `JsonWorkflowReconciliationFeature`).
- **Consumed by:** the reconciler in `Elsa.Workflows.Design.Reconciliation`.
- **Catalog:** [`Elsa.Workflows.Design.Reconciliation/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Reconciliation/EXTENSION_POINTS.md).

---

## Feature inheritance

`JsonWorkflowReconciliationFeature` extends the abstract `WorkflowsDesignReconciliationFeature` and calls
`base.ConfigureServices`, so enabling it also arms the reconcile lifecycle. `ConfigureServices` is `override`; an
inheriting feature calls it and re-registers a contract afterwards.

---

## Replaceable services (per §2.6.2)

Each collaborator is registered against a contract and injected as that contract (§2.5). Both are **scoped** — they
execute logic rather than hold application-wide state (§2.5.1); the options instance is the only singleton.

- **`IJsonWorkflowCatalogReader` → `JsonWorkflowCatalogReader`** — reads one file into
  `WorkflowVersionReconciliationModel[]` through the shared `IPayloadSerializer`; every IO/parse fault becomes an
  `InvalidWorkflowCatalogJsonException` naming the path. Re-register to change the file layout without touching
  `JsonWorkflowReconciliationSource`.
- **`IJsonWorkflowCatalogWriter` → `JsonWorkflowCatalogWriter`** — writes models as the JSON array the reader reads,
  through the same `IPayloadSerializer`; the same models give the same bytes. Not registered by
  `JsonWorkflowReconciliationFeature`, so reconciliation behaviour does not depend on it; export tooling (the Elsa 3
  migration CLI, spec 1237) registers it with `services.AddScoped<IJsonWorkflowCatalogWriter, JsonWorkflowCatalogWriter>()`.

---

## Options

`JsonWorkflowReconciliationOptions` (`SourceId`, exactly one of `FilePath` / `Files` / `FolderPath`,
`PublishOnReconcile`) for `Options` and each `AdditionalSources` entry. The feature validates them at registration;
see the [README](README.md#options).

---

## Events

This module declares no `IEvent` types and handles none directly; its source is pulled by the reconciler.

---

## Cross-references

- Reconciliation lifecycle and the source contract: [`Elsa.Workflows.Design.Reconciliation/EXTENSION_POINTS.md`](../Elsa.Workflows.Design.Reconciliation/EXTENSION_POINTS.md).
- The Activities-side JSON source: [`Activities/Design/Reconciliation/Json/EXTENSION_POINTS.md`](../../../../Activities/Design/Reconciliation/Json/EXTENSION_POINTS.md).
- Shared serializer contract: `Elsa.Serialization.Core` (`IPayloadSerializer`, `PayloadCatalogFile`).
- Repo-wide index: [`EXTENSION_POINTS.md`](../../../../../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 (DI-source), §2.5 / §2.5.1 (contract registration, lifetimes), §2.23.5 (domain-scoped
  faults).
