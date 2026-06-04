# Extension points — Activities.Design.Reconciliation.Clr

A reconciliation **source** module (§2.6.1). It contributes to the activity catalog reconciliation
lifecycle anchored at `Elsa.Activities.Design.Reconciliation`; it owns no catalog of its own.

---

## Contributions to other domains

### `IActivityReconciliationSource` *(Core contract — `Elsa.Activities.Design.Reconciliation.Core`)*
- **Implementation:** `ClrActivityReconciliationSource` (`SourceKind => "CLR"`).
- **What it does:** scans `ClrReconciliationOptions.FolderPath` via `ClrAssemblyScanner` and returns
  one `ActivityVersionReconciliationModel` per discovered `IActivity` implementation.
- **Register:** `services.AddScoped<IActivityReconciliationSource, ClrActivityReconciliationSource>()`
  (done by `ClrActivityReconciliationFeature`).
- **Consumed by:** `ActivityVersionsReconcilingHandler` in `Elsa.Activities.Design.Reconciliation`,
  which injects every `IActivityReconciliationSource` and reconciles the returned versions.
- **Catalog:** [`Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](../Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md).

---

## Replaceable services (per §2.6.2)

Every collaborator is registered against a contract and injected as that contract (§2.5), so an
inheriting feature can replace one in isolation: call `base.ConfigureServices(services)` and
re-register just the contract afterwards. All three are **scoped** — they execute logic rather than
holding application-wide static state (§2.5.1).

- **`IActivityTypeVersionResolver` → `ActivityTypeVersionResolver`** — resolves an activity's
  author-controlled SemVer. Re-register to change the version-precedence policy; the default (FR-020:
  `[Version]` → `AssemblyInformationalVersion` → 4-part assembly version) is the sanctioned rule.
- **`IActivityTypeCategoryResolver` → `ActivityTypeCategoryResolver`** — resolves an activity's catalog
  category. The default takes the last dot-separated segment of the declaring assembly's simple name
  (e.g. `Elsa.Runtime.Activities.Primitives` → `Primitives`). Re-register for a different scheme
  (e.g. a type-level `[Category]` attribute).
- **`IClrAssemblyScanner` → `ClrAssemblyScanner`** — the reflection-only folder scanner that produces
  one `ActivityVersionReconciliationModel` per discovered `IActivity`. Re-register to change the
  scanning strategy without touching `ClrActivityReconciliationSource`, which depends only on the
  contract.

---

## Options

- **`ClrReconciliationOptions.FolderPath`** — the folder scanned for activity-bearing assemblies.
- **`ClrReconciliationOptions.SourceId`** — recorded as `SourceId` on every contributed row; defaults
  to the normalised `FolderPath` (R3).

---

## Events

This module declares no `IEvent` types. It handles none directly; its source is pulled by the
reconciliation feature's handler.

---

## Cross-references

- Reconciliation lifecycle + the source contract: [`Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md`](../Elsa.Activities.Design.Reconciliation/EXTENSION_POINTS.md).
- Activity abstractions + `[Version]`: `Elsa.Activities.Runtime.Core`.
- Repo-wide index: [`../../EXTENSION_POINTS.md`](../../EXTENSION_POINTS.md).
- Constitutional basis: §2.6.1 (DI-source), §E2.2 (Design→Runtime edge), §E2.8 (author semver).
