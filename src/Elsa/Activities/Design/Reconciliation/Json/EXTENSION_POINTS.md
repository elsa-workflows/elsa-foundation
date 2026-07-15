# Extension points — Activities.Design.Reconciliation.Json

A reconciliation **source** module (§2.6.1). It contributes to the activity catalog reconciliation
lifecycle anchored at `Elsa.Activities.Design.Reconciliation`; it owns no catalog of its own.

---

## Contributions to other domains

### `IActivityReconciliationSource` *(Core contract — `Elsa.Activities.Design.Reconciliation.Core`)*
- **Implementation:** `JsonActivityReconciliationSource` (`SourceKind => "Json"`).
- **What it does:** reads either the single `JsonReconciliationOptions.FilePath` or each
  `JsonReconciliationOptions.Files` entry (in ascending `Order`) via `IJsonActivityCatalogReader` and
  returns the concatenated `ActivityVersionReconciliationModel[]`. Each model's `Descriptor` is left as
  raw `JsonElement` paired with explicit provider and consumer key/schema identities. Reconciliation
  persists those values opaquely and never resolves a CLR descriptor type. Ordering lets an author stage dependencies
  (e.g. plain activities first, workflow-backed activities that reference them second).
- **Register:** `services.AddScoped<IActivityReconciliationSource, JsonActivityReconciliationSource>()`
  (done by `JsonActivityReconciliationFeature`).
- **Consumed by:** `CollectActivityVersions` in `Elsa.Activities.Design.Reconciliation`,
  which injects every `IActivityReconciliationSource` and reconciles the returned versions.
- **Catalog:** [`Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](../Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md).

---

## Replaceable services (per §2.6.2)

Every collaborator is registered against a contract and injected as that contract (§2.5), so an
inheriting feature can replace one in isolation: call `base.ConfigureServices(services)` and
re-register just the contract afterwards. The reader is **scoped** — it executes logic rather than
holding application-wide static state (§2.5.1); the options instance is the only singleton.

- **`IJsonActivityCatalogReader` → `JsonActivityCatalogReader`** — reads + deserializes the JSON file
  into reconciliation models via the shared `IPayloadSerializer`. Re-register to change the file
  layout (e.g. a different envelope, an embedded resource, or a remote fetch) without touching
  `JsonActivityReconciliationSource`, which depends only on the contract.

---

## Options

- **`JsonReconciliationOptions.FilePath`** — the single-file shorthand; mutually exclusive with `Files`.
- **`JsonReconciliationOptions.Files`** — the ordered set of files (`JsonActivityReconciliationFileOption`,
  carrying `Order` + `FilePath`) read for reconciliation models; read in ascending `Order` and concatenated.
  Mutually exclusive with `FilePath`.
- **`JsonReconciliationOptions.SourceId`** — recorded as `SourceId` on every contributed row. Required.

`JsonActivityReconciliationFeature.ConfigureServices` validates the composition (exactly one of
`FilePath`/`Files`, non-empty `SourceId`) and throws `InvalidOperationException` otherwise.

---

## Events

This module declares no `IEvent` types. It handles none directly; its source is pulled by the
reconciliation feature's handler.

---

## Cross-references

- Reconciliation lifecycle + the source contract: [`Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](../Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md).
- The sibling CLR source: [`Elsa.Activities.Design.Reconciliation.Clr/EXTENSION_POINTS.md`](../Elsa.Activities.Design.Reconciliation.Clr/EXTENSION_POINTS.md).
- Shared serializer contract: `Elsa.Serialization.Core` (`IPayloadSerializer`).
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 (DI-source), §2.5 / §2.5.1 (contract registration, lifetimes),
  §2.23.5 (domain-scoped faults).
