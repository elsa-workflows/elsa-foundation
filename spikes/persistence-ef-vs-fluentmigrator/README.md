# Spike: derived-context EF migrations vs FluentMigrator + EF

Isolated comparison for the proposed **EF-first** persistence direction (dual-family EF + optional Mongo).
This folder is not in `Elsa.Server.slnx` and is not a Secrets product pilot.

**Do not** treat this as an ADR. Use it to write one.

## How to run

From this directory (`spikes/persistence-ef-vs-fluentmigrator/`):

```bash
dotnet test PersistenceSpike.slnx --filter "FullyQualifiedName~Sqlite"
```

Sqlite tests are required and need no container.

Postgres tests (`*PostgreSql*`) use Testcontainers (`postgres:16-alpine`) and skip when Docker is unavailable:

```bash
dotnet test PersistenceSpike.slnx
```

## Domain (Secrets-shaped)

| Piece | Role |
|---|---|
| `SecretRecord` | Id, TenantId, Name, Payload (JSON), RowVersion (OCC) |
| `ISecretStore` | Add + GetByName |
| History / version table | Per-module names in `SchemaNames` |

## Variant A — derived-context EF migrations

Claude’s constraint: **one `DbContext` type has one `ModelSnapshot`**. Provider-specific SQL must live on **derived contexts**.

| Project | References | Role |
|---|---|---|
| `Elsa.Persistence.Spike.VariantA.Module` | `EFCore` + `EFCore.Relational` only | Base context, entity config, store, `MigrateAsync` |
| `Elsa.Persistence.Spike.VariantA.Tooling` | Module + `Sqlite` + `Npgsql.EntityFrameworkCore.PostgreSQL` | Design-time factories + generated migrations |

**Authoring (already run; artifacts committed):**

```bash
dotnet ef migrations add Initial_Sqlite \
  --project src/VariantA/Tooling --startup-project src/VariantA/Tooling \
  --context Elsa.Persistence.Spike.VariantA.SecretsSqliteDbContext \
  --output-dir Migrations/Sqlite

dotnet ef migrations add Initial_PostgreSql \
  --project src/VariantA/Tooling --startup-project src/VariantA/Tooling \
  --context Elsa.Persistence.Spike.VariantA.SecretsPostgreSqlDbContext \
  --output-dir Migrations/PostgreSql
```

SqlServer would be a third derived context + folder — same pattern, not implemented.

**Apply:** `VariantAMigrations.ApplyAsync` → provider guard → `Database.MigrateAsync()`.

**History table:** `__EFMigrationsHistory_ElsaSecrets` (`MigrationsHistoryTable` on the provider `UseSqlite` / `UseNpgsql` options in the tooling factories — not on the module context type).

**EF9 migrate lock:** `Database.MigrateAsync` already calls `IHistoryRepository.AcquireDatabaseLockAsync` (EF Core 9+). This spike does not reimplement it. Hosts that call `IMigrator.Migrate` / `GetPendingMigrations` + apply themselves must take the same lock or they race.

## Variant B — FluentMigrator schema + one EF context for data

| Project | References | Role |
|---|---|---|
| `Elsa.Persistence.Spike.VariantB.Module` | `EFCore` + `Relational` + `FluentMigrator` (attributes only) | Single `SecretsDbContext` + `[Migration]` classes |
| `Elsa.Persistence.Spike.VariantB.Host` | Module + FM runners + both EF providers | Runner, version table, drift check |

**Apply:** `FluentMigratorApply.Apply` scans the **module** for `[Migration]` and the **host** for `IVersionTableMetaData`.

**Version table:** `VersionInfo_ElsaSecrets` (`SecretsVersionTable`). Do not inherit `DefaultVersionTableMetaData` in FM 8 — its constructor requires DI.

**Drift API used:** after FM apply,

1. `IDesignTimeModel.Model.GetRelationalModel()` — EF’s intended schema.
2. Provider `IDatabaseModelFactory.Create(connectionString, options)` — actual database (`EF1001`; types are internal).
3. `IMigrationsModelDiffer.GetDifferences(source: null, target: efRelational)`.

That is **not** a two-sided `GetDifferences(db, ef)`. `IScaffoldingModelFactory` is not on the runtime provider. The check still failed when FM omitted a column and passed when schemas matched (the Sqlite test).

`IfDatabase(ProcessorIdConstants.SQLite | PostgreSQL)` is required for Guid / JSON / OCC. The FluentMigrator set is **not** fully provider-neutral.

## Shared concerns

### Per-module history / version table

Required so two modules in one database do not share `__EFMigrationsHistory` or `VersionInfo`. Names are in `Shared/SchemaNames.cs`.

### Provider guard

| Variant | Check |
|---|---|
| A | `context.Database.ProviderName` must match the expected provider for that derived type (`ProviderGuard.Ensure`) |
| B | Host-selected runner (`AddSQLite` / `AddPostgres`) must match `IProcessorAccessor.Processor.DatabaseType` |

A throws **before** SQL if the context’s live provider does not match the caller’s expected name. B’s guard is runner/processor pairing: a Postgres runner reports `DatabaseType = PostgreSQL` even when the connection string is a Sqlite file, so the guard stays silent and `MigrateUp` fails at connect/SQL time (the Sqlite test uses `ThrowsAny`). A host must still pick the runner from configuration, not from “whatever file we opened.”

### How Nuplane would hook this (not integrated)

Current host path (`src/apps/Elsa.Nuplane/`):

1. `FeaturesController` → `IFeatureManagementService.ApplyAsync` (`Elsa.Nuplane.Application`).
2. Persist enablement, then `IFeatureCatalogRefresh.RefreshAsync`, then `IShellReloader.ReloadAsync`.
3. Reload rebuilds the host; `TaskManager` / `IStartupTask` run after the new container exists.

**In-process (feature enable):** a persistence feature registers an `IStartupTask` (or equivalent post-reload hook) that:

- A: resolve the **provider-derived** `DbContext` and call `Database.MigrateAsync()` (EF9 lock included).
- B: build an FM runner for the **configured** dialect, `AddVersionTableMetaData`, `MigrateUp()`.

The **module** package still must not reference provider packages. The **shell / provider package** supplies Sqlite or Npgsql (A tooling stays out of the running host).

**CI out-of-process:**

- A: `dotnet ef database update --context SecretsXxxDbContext` against the CI connection string, **or** a tiny host that news the derived context and calls `Migrate()`.
- B: `dotnet fm migrate` / a console that calls `FluentMigratorApply.Up`, **then** the drift check from `EfModelDrift` as a gate.

Mongo stays a second family (driver / migrations of its own). Neither variant puts Mongo in these contexts.

## Dependency graphs

```
Variant A
  Module  --> Microsoft.EntityFrameworkCore
          --> Microsoft.EntityFrameworkCore.Relational
  Tooling --> Module
          --> Microsoft.EntityFrameworkCore.Sqlite
          --> Microsoft.EntityFrameworkCore.Design
          --> Npgsql.EntityFrameworkCore.PostgreSQL
  Tests   --> Module + Tooling + xunit + Testcontainers.PostgreSql

Variant B
  Module  --> EF Core + Relational + FluentMigrator (migration attributes)
  Host    --> Module
          --> FluentMigrator.Runner.Core
          --> FluentMigrator.Runner.SQLite
          --> FluentMigrator.Runner.Postgres
          --> both EF providers + Design
  Tests   --> Module + Host + xunit + Testcontainers.PostgreSql
```

Do **not** reference the `FluentMigrator.Runner` meta-package here: it pulls IBM/Firebird/etc. and this repo’s `NuGet.config` package-source mapping will fail restore.

## LOC-ish (hand-counted, generated `obj/` excluded)

| Surface | Lines |
|---|---:|
| Shared | 49 |
| A module (authored) | 134 |
| A tooling factories | 75 |
| A generated migrations (2 providers, after OCC fix) | 326 |
| B module (context + 36-line FM migration) | 118 |
| B host (runner + version table + drift) | 208 |
| A tests | 148 |
| B tests | 129 |

Generated A SQL is already larger than the handwritten FM migration. A third provider (SqlServer) adds another snapshot + designer pair (~160 lines) plus a factory.

## Footguns observed

1. **`IsRowVersion()` on Sqlite.** EF’s Sqlite provider inserts `NULL` into a `IsRowVersion` column; `NOT NULL` then fails. Use `IsConcurrencyToken()` and stamp `RowVersion` in `SaveChanges` (this spike uses a 16-byte GUID). SqlServer can keep `rowversion` later; do not assume one OCC mapping for all providers.
2. **One snapshot per context type.** Confirmed by generating two folders from two derived types. Putting both providers on one context type is the old matrix.
3. **Nested `NuGet.config`.** A spike-local config **replaces** parent package-source mapping. FluentMigrator patterns were added to the **repo-root** `NuGet.config` instead. Do not add FluentMigrator to product `Directory.Packages.props`.
4. **FluentMigrator.Runner meta-package** pulls dialects this feed mapping does not allow.
5. **FM 8 `IVersionTableMetaData`.** Implement the interface; do not subclass `DefaultVersionTableMetaData` without a service provider.
6. **FM is not dialect-free.** Guid affinity, `jsonb` vs `TEXT`, and OCC still need `IfDatabase`.
7. **Drift check uses internal EF types** (`EF1001`). Fine for a CI gate; awkward as a product runtime API.
8. **`:memory:` Sqlite.** Two `DbContext` instances do not share one in-memory database. Tests use a temp file.
9. **Architecture / maps scanners** walk `src/` + `tests/` (maps) or **all** `*.csproj` (EF surface + zero-EF restore). `spikes/` is ignored by those tools so this folder does not expand evidence ledgers. See `spikes/README.md`.

## Recommendation for the Secrets pilot

**Pick Variant A (derived-context EF migrations).**

Decision criteria used:

| Criterion | Winner | Why |
|---|---|---|
| Minimize **owned** persistence code for **one** module (Secrets) | **A** | No second schema DSL, no mandatory drift service, no FM runner stack in the host |
| Keep the **module** package free of provider engines | Tie | Both do this if tooling/host stay out of the module |
| Honest multi-provider SQL | Tie | A snapshots are explicit; B still branches with `IfDatabase` |
| Concurrent migrate | **A** | `MigrateAsync` already takes the EF9 database lock |
| Operational familiarity (EF + Elsa) | **A** | One toolchain (`dotnet ef`, `MigrateAsync`) |
| N-module × 3-provider snapshot volume | **B** (only if that becomes the pain) | Secrets is one table; A’s extra snapshot is cheap |
| Mongo dual-family | Tie | Neither variant solves Mongo; keep it a second family |

Revisit FluentMigrator if the product later has **many** relational modules and snapshot review cost dominates. For the Secrets pilot, A is enough and is closer to “EF-first.”

## What the ADR should say

### If derived EF wins (this spike’s recommendation)

- Persistence direction remains **EF-first** for the relational family; Mongo optional and separate.
- **One `DbContext` type may not own three provider migration sets.** Each SQL provider gets a derived context + its own `Migrations/` folder + `ModelSnapshot`.
- The **module** package references `Microsoft.EntityFrameworkCore` + `Relational` only. Design-time factories and `dotnet-ef` live in a **tooling** (or host-provider) project.
- History table is **per module** (`__EFMigrationsHistory_<Module>`).
- Apply with `Database.MigrateAsync()` so the EF9 migrate lock is used. Document that custom apply loops must take `IHistoryRepository.AcquireDatabaseLockAsync`.
- OCC: do not use `IsRowVersion()` as the cross-provider default; define an explicit token strategy per provider.
- Groundwork modules stay frozen; Secrets is the first new module on this pattern.

### If FluentMigrator had won

- EF remains the **query/unit-of-work** API (single context is fine).
- Schema ownership moves to FluentMigrator. Module ships `[Migration]` classes; host/CI runs `MigrateUp` with a **per-module** `IVersionTableMetaData`.
- Provider packages stay out of the module; runner packages stay in host/CI.
- **Require** a CI drift gate (`IMigrationsModelDiffer` + `IDatabaseModelFactory` as in `EfModelDrift`) on every provider you ship. Without it, EF and FM diverge silently.
- Record that FM migrations are still provider-conditional (`IfDatabase`) for Guid/JSON/OCC — this is not a free escape from the dialect matrix.
- Do not take the `FluentMigrator.Runner` meta-package under this repo’s NuGet mapping.

## Isolation / ratchet

| Change outside the spike | Why |
|---|---|
| `EfCoreSurfaceScanner` ignores `spikes/` | Disposable EF must not expand `ef-core-surface.json` |
| Restore-zero-ef scripts skip repo-root `spikes/` | Same for the all-project restore receipt |
| Root `NuGet.config` maps `FluentMigrator*` → nuget.org | Variant B restore; not a product dependency |

No Groundwork module, no `src/modules/**`, and no evidence-ledger JSON was edited.
