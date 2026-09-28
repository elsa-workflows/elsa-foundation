# Research: Expand-Only Migration Guard

Inventory behind [spec.md](./spec.md). Everything here was read from the tree on 2026-09-27, with PR #2109's specs 180
to 183 merged in. Paths are repository-relative. Sections are cited by name, not line.

## EF modules and their migrations

Thirteen `[EfModule]` declarations in eleven assemblies. Two assemblies declare two modules each: the Distributed
runtime persistence assembly (placement and command transport) and the Identity persistence assembly (IAM and provider
configuration).

| EF module (context prefix) | Migrations folder | Migrations per provider |
|---|---|---|
| ActivitiesDesign | `src/essentials/Activities/Design/Persistence/EntityFrameworkCore/Migrations/ActivitiesDesign/<Provider>` | 1 |
| OpenTelemetry | `src/essentials/Diagnostics/OpenTelemetry/Persistence/EntityFrameworkCore/Migrations/OpenTelemetry/<Provider>` | 1 |
| StructuredLogs | `src/essentials/Diagnostics/StructuredLogs/Persistence/EntityFrameworkCore/Migrations/StructuredLogs/<Provider>` | 1 |
| IdentityIam | `src/essentials/Foundation/Identity/Persistence/EntityFrameworkCore/Migrations/IdentityIam/<Provider>` | 1 |
| IdentityProviderConfiguration | `src/essentials/Foundation/Identity/Persistence/EntityFrameworkCore/Migrations/IdentityProviderConfiguration/<Provider>` | 1 |
| Secrets | `src/essentials/Secrets/Persistence/EntityFrameworkCore/Migrations/<Provider>` | 2 (SQLite, MySQL) or 3 (SQL Server, PostgreSQL): the historical chain |
| StudioPreferences | `src/essentials/Studio/Preferences/Persistence/EntityFrameworkCore/Migrations/StudioPreferences/<Provider>` | 1 |
| WorkflowsDesign | `src/essentials/Workflows/Design/Persistence/EntityFrameworkCore/Migrations/WorkflowsDesign/<Provider>` | 1 |
| PublishingSnapshotReview (`Workflows.Publishing`) | `src/essentials/Workflows/Publishing/Persistence/EntityFrameworkCore/Migrations/PublishingSnapshotReview/<Provider>` | 1 |
| ExecutionPlacement | `src/essentials/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Migrations/ExecutionPlacement/<Provider>` | 1 |
| ExecutionCommandTransport | `src/essentials/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Migrations/ExecutionCommandTransport/<Provider>` | 1 |
| Runtime | `src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Migrations/Runtime/<Provider>` | 1 |
| Elsa3Import | `src/extensions/Elsa3/src/Activities/Design/Import/Persistence/EntityFrameworkCore/Migrations/Elsa3Import/<Provider>` | 1 |

Providers: `Sqlite`, `SqlServer`, `PostgreSql`, `MySql`. That is 52 provider contexts and 58 migration classes today.
No EF module is declared under `src/apps`, but the guard's source enumeration covers it (FR-019), because a sweep that
covers only one root misses the others.

## Generation

- `tools/ef/generate-module-migrations.sh` regenerates each module context's single `Initial` per provider and keeps
  Secrets' chain. Its header states that regeneration ends at the 4.0 stable release (#1976) and that migrations stay
  provider-free through a post-processing step (`tools/ef/strip-provider-calls.sh`).
- #1976 ("What the freeze involves") plans an "add" mode for incremental migrations, a guard against editing, renaming
  or deleting a shipped migration driven by "a recorded manifest of shipped migration ids and content hashes", and an
  upgrade test per provider. Its manifest does not yet exist. RQ-001 asks that it also say which migrations are the
  baseline.

## Reading operations: the existing precedent

- `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/OrdinalCollationMigrationTests.cs`, its private
  `Operations` helper: for a provider context it takes `IMigrationsAssembly` from the context, orders
  `assembly.Migrations` by id, and calls `assembly.CreateMigration(type, EfRelationalProviderBinding.ExpectedProviderName(provider)).UpOperations`.
  The context is built against `ModuleContextCatalog.PlaceholderConnection(provider)`, which nothing opens.
- `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/ModuleContextCatalog.cs`: every provider context
  of the first-party modules, through one anchor type per module assembly (eleven anchors), and the history table per
  context from `EfModuleCatalog.Discover`. A new module assembly needs a new anchor, which is why FR-019 cross-checks
  against a source enumeration.
- `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/EfModuleDescriptorTests.cs` pins the 13 modules
  by name, context and history module.
- `tests/essentials/Persistence/EntityFrameworkCore/Migrations/Tests/SyntheticEfModules.cs` emits throwaway assemblies
  carrying real `[EfModule]` metadata, for module graphs no first-party module has. FR-021's fixture module follows the
  same approach.

## EF Core operation types and their classification

From `Microsoft.EntityFrameworkCore.Migrations.Operations`. "New table" means a table created in the same migration.

| Operation | Classification |
|---|---|
| `CreateTableOperation` | Allowed (FR-007) |
| `AddColumnOperation` | Allowed on a pre-existing table only when nullable and not computed (FR-008); otherwise a violation |
| `CreateIndexOperation` | Allowed on a new table; on a pre-existing table only when not unique |
| `AddForeignKeyOperation`, `AddPrimaryKeyOperation`, `AddUniqueConstraintOperation`, `AddCheckConstraintOperation` | Allowed on a new table only |
| `InsertDataOperation` | Allowed on a new table only |
| `EnsureSchemaOperation`, `CreateSequenceOperation` | Allowed |
| `DropTableOperation`, `DropColumnOperation`, `DropIndexOperation`, `DropForeignKeyOperation`, `DropPrimaryKeyOperation`, `DropUniqueConstraintOperation`, `DropCheckConstraintOperation`, `DropSchemaOperation`, `DropSequenceOperation` | Violation |
| `RenameTableOperation`, `RenameColumnOperation`, `RenameIndexOperation`, `RenameSequenceOperation` | Violation |
| `AlterColumnOperation`, `AlterTableOperation`, `AlterDatabaseOperation`, `AlterSequenceOperation`, `RestartSequenceOperation` | Violation |
| `SqlOperation`, `UpdateDataOperation`, `DeleteDataOperation` | Violation |
| Any other type | Violation (FR-006) |

Engine facts behind the classification:

- SQL Server treats NULLs as equal in a unique index unless the index is filtered, so a unique index on a new nullable
  column refuses the second insert that leaves it null.
- SQLite supports few `ALTER TABLE` forms. EF Core's SQLite migrations SQL generator rebuilds the table for most
  alterations, which the operation list does not show. The guard classifies the operation, not the SQL.
- Npgsql expresses PostgreSQL extensions and database collations as `AlterDatabaseOperation` annotations, so adding one
  after the freeze needs an opt-out.

## Where the guard runs

The migrations test project already references every first-party module assembly and the four providers, and already
reads operations. It is the natural home for the real scan and the fixtures. It adds no EF reference to any production
project, so `EfCoreDependencyGuardTests` is unaffected. The opt-out attribute is the one production addition, in
`src/essentials/Persistence/EntityFramework`, beside `EfModuleAttribute` and `UsesEfModuleAttribute`.

## Noticed and left alone

- `ModuleContextCatalog`'s anchor list has to be edited by hand when a module assembly is added. FR-019's source
  enumeration makes a missed anchor fail this guard, but the catalog itself is not changed here.
