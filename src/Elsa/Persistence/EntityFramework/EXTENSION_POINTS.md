# Extension points — Persistence.EntityFramework (policy)

Shared policy helpers for first-party EF Core persistence under accepted
[ADR 0073](../../../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md).
This package does not own domain entities or a mandated `DbContext` base.

## Replacement / composition

There is no replacement contract in this package. Modules own derived `DbContext` types.
Hosts register the derived context that matches the selected relational provider and call
`EfDatabaseMigrator.ApplyAsync` with that provider's expected `Database.ProviderName`.

## Apply policy

`EfMigratePolicy.AutoMigrate` runs `Database.MigrateAsync` (EF 9+ lock).
`EfMigratePolicy.Validate` refuses to start when pending migrations exist.
Secrets registers that apply on both `IHostedService` and CShells `IShellInitializer`
so a feature enable or reload uses the same policy as a cold start.

Which one a deployment runs is an operator setting, not a code seam: `EfModuleMigrator<TContext>`
reads `EfMigrateOptions`, bound from `Elsa:Persistence:EntityFramework:Migrate:Policy`
(`EfMigrateOptions.SectionName`). A host that needs the policy decided in code can still
`services.Configure<EfMigrateOptions>(…)` after composing the module.

## Schema and pooling

Neither is an extension point: a module opts in by passing its `Schema` and `Pooling` options through
`EfModuleBinding.Apply` and `EfModuleBinding.AddContext`, and the shared layer decides what that means per
provider. `EfSchema.Resolve` reads the module setting, then `Elsa:Persistence:EntityFramework:Schema`;
SQLite ignores a schema and MySQL refuses one. A module context applies what it was bound to with
`modelBuilder.HasElsaDefaultSchema(this)` as the first line of `OnModelCreating`, and
`EfSchemaMigrationsAssembly` puts its scaffolded migrations in the same schema. See the package README for
the operator-facing description of both settings.

## Provider binding validation

`EfRelationalProviderBinding` reaches each engine's `Use*` extension by type and method name, so nothing in a
compile notices a missing or mismatched provider package. `AddEfModuleMigrations<TContext>` therefore records the
provider each module context is configured for, and `EfProviderBindingValidator` probes all of them in the CShells
`Prepare` phase ahead of every migrator (and as the first `IHostedService` on a plain host). There is no extension
point here: a module opts in by registering its migrations, and the validator reads what that registration recorded.

## Shared transactions

`EfSharedTransaction` is the owner a cross-module write uses when several module contexts must commit
as one unit. It constructs fresh instances of the configured contexts, shares one connection and one
transaction between them, commits or rolls back once, and refuses contexts that name different providers
or connection strings. Module atomic writers join it through their transaction-factory seam
(`EfSharedTransaction.BeginOperationAsync`); a writer that rolls back makes the owner rollback-only.

## Ordinal string collation

`EfOrdinalCollation` is a pinned decision, not an extension point: one binary collation per provider
(`Latin1_General_100_BIN2`, `C`, `utf8mb4_0900_bin`, and nothing on SQLite, whose default already is
`BINARY`). A module declares which of its columns are compared or ordered ordinally and calls
`EfOrdinalCollation.Apply` from each provider-derived context; a provider nobody has decided for is
refused rather than left on the server's linguistic default.

On MySQL it also sets the provider's own `MySQL:Collation` annotation, because Oracle's provider reads that
out of a migration's target model and ignores the relational column collation.

It is applied **per column**, never through `modelBuilder.UseCollation`, for two reasons: modules can
share one database, so a database-wide collation is one module setting its neighbours' comparison
semantics, and a model-level declaration did not survive migration generation at all
([#1837](https://github.com/elsa-workflows/elsa-foundation/issues/1837)).
