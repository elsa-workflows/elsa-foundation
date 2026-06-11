# Phase 1 Data Model — Activity Semantic Versioning

The unit's core data change is `int → string` (SemVer 2.0.0) for the activity version on every surface, plus one new persistence-only sort key and one new attribute. Member names below are verbatim against the current code.

## Entities & contracts changed

### `ActivityDefinitionVersion` (entity — `Elsa.Activities.Design.Persistence.Core`)
| Member | Before | After | Notes |
|---|---|---|---|
| `Version` | `int` | `string` | Author semver. `[Immutable]` retained; constructor-supplied. Persisted as string column. |
| `SemVerSortKey` | — | `string` **(NEW)** | Persistence-only normalised sort key (R1). Real CLR property, `[Immutable]`, **omitted from `IActivityDefinitionVersion`** (§2.9.1). Computed once at creation from `Version`. |
| `ReconcilliationHash` | `string?` | `string?` | Unchanged (double-l spelling intentional). |

### `IActivityDefinitionVersion` (read contract — `Elsa.Activities.Design.Core`)
| Member | Before | After |
|---|---|---|
| `Version` | `int` | `string` (read-only) |
- `SemVerSortKey` is **not** added to this interface (hidden persistence concern).

### `ActivityDefinitionVersionInfo` (projection — `Elsa.Activities.Design.Core.Models`)
- `Version`: `int → string`.

### `ActivityVersionReconciliationModel` (contribution model — moves to `.Reconciliation.Core`, FR-021)
- `Version`: `int → string`. All other members unchanged (`ActivityTypeKey`, `DisplayName`, `Category`, `Description`, `ImplementationKind`, `ImplementationDescriptor`, `Inputs`, `Outputs`, `DesignFacets`, `ExecutionType`).

### `ActivityVersionOrderDefinition` (`Elsa.Activities.Design.Persistence.Core`)
- Before: `OrderDefinition<ActivityDefinitionVersion, int>(v => v.Version, …)`.
- After: `OrderDefinition<ActivityDefinitionVersion, string>(v => v.SemVerSortKey, OrderDirection.Descending)` — DB-side semver-precedence ordering via the normalised key (R1).

### `ActivityVersionHashMismatchException` (`.Reconciliation.Core`)
- Version field/ctor param: `int → string` (FR-006). Reports the offending semver.

### API surface (`AddVersion` command + endpoint, version details view(s), `ListVersions` request/handler/projection)
- Every version field becomes the semver string (FR-005). No integer version member remains on any request/response/view model.

### Runtime activity abstraction (`IActivity` / `ActivityBase` — move to `Elsa.Activities.Runtime.Core`, FR-009/FR-018)
- `Version`: `int → string` (semver). Unifies the version meaning across design + runtime.

## New types

### `[Version]` attribute (`Elsa.Activities.Runtime.Core`, FR-009)
- `[AttributeUsage(AttributeTargets.Class)] sealed VersionAttribute(string version)`.
- **Optional.** Absent → activity inherits its declaring assembly's version (FR-012).
- Carries the author semver string verbatim; validated as SemVer 2.0.0 at scan time.

### SemVer value + comparator (R1)
- A `SemVer` value type (parse / precedence / normalise-to-sort-key / equality-ignoring-build-metadata) and `SemVerComparer : IComparer<SemVer>` (Strategy, §2.24.2 row 9).
- Home: co-located with the activity version concern (final placement decided in tasks — candidate: a small value type in `Elsa.Activities.Runtime.Core` alongside `[Version]`, or `Elsa.Primitives` if it proves domainless and meets the three-repetition rule). Zero heavy deps either way.

### `ClrImplementationDescriptor` (`.Reconciliation.Clr`)
- The CLR-kind polymorphic descriptor carried on the contribution model (`ImplementationKind = "CLR"`).

## Identity & invariants
- **Natural key:** `ActivityTypeKey` = CLR type full name (FR-022). Model X identity = `(SourceKind, SourceId, ActivityTypeKey)`; row identity = `(DefinitionId, Version)`.
- **One row per `(DefinitionId, semver)`** (FR-014). Versions never deleted (Model X).
- **Equality** (FR-013): `(DefinitionId, Version)` compared by semver precedence (build metadata ignored) — implemented via `SemVerSortKey`.
- **Immutability:** `Version`, `SemVerSortKey`, `ReconcilliationHash` are `[Immutable]` (§E2.8 Model X).

## Migration
- Fresh SQLite migration for the activities-design context (FR-015). No int→semver backfill (Unit B no-preserved-production-data convention).
