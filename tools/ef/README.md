# generate-ef-migrations

Convention for first-party EF modules under proposed ADR 0072 (Secrets pilot).

## Layout

- **Module package** (`src/Elsa/<Domain>/Persistence/EntityFrameworkCore/`): `*DbContext` + derived
  provider contexts + `Migrations/<Provider>/`. References EF Core + Relational only.
- **Tooling project** (`.../EntityFrameworkCore/Tooling/`): `IDesignTimeDbContextFactory` per
  derived context, provider PackageReferences, `Microsoft.EntityFrameworkCore.Design`.
- **Policy package** (`src/Elsa/Persistence/EntityFramework/`): history table name, provider guard,
  AutoMigrate vs Validate. No provider engines.

## Command shape

Always pass `--startup-project` as the tooling project and `--project` as the module so generated
files compile into the assembly Nuplane loads at apply time.

History table: `EfMigrationsHistory.TableName("<Module>")` → `__EFMigrationsHistory_<Module>`.
Set it on the factory's `Use*` options (`MigrationsHistoryTable`), not as a magic base-context property.

## Secrets

See `src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/README.md`.

## Phase 2 (not in this PR)

A Nuplane dual-migrate CI tool (apply + fail-if-pending for each derived context) is deferred.
Hook: `dotnet ef database update --context <Derived>` and
`dotnet ef migrations has-pending-model-changes --context <Derived>`.
