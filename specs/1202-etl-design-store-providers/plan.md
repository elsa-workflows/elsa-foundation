# Implementation Plan: The ETL design store on all four EF engines

**Branch**: `agent/claude/2908-etl-design-providers` | **Spec**: [spec.md](spec.md)

## Summary

Follow the Rules module, whose EF contexts are internal like ETL's: provider contexts per engine over the existing provider-neutral `EtlDesignDbContext`, selected at registration with `EfModuleBinding.Select`; an ETL-owned design-time tooling project and script that generate internal migrations; and an ETL-owned provider contract suite. Replace the SQLite-only collation function with per-column binary collation, and run publication through `EfWriteRetry`.

## Constitution check

- **Persistence family** (ADR 0073 D1): EF Core only; the module package stays provider-free (EF Core and Relational); hosts bring the provider package.
- **Visibility and domains** (§2.23.3): the contexts and migrations stay internal. `InternalsVisibleTo` goes only to ETL-domain assemblies (`Elsa.Etl.Tooling`, `Elsa.Etl.Persistence.ProviderTests`), as Rules grants `Elsa.Rules.Tooling`; the shared tools and migration matrix are not used, because both live in other domains (`ArchitectureGuardTests.InternalsVisibleTo`).
- **Compatibility:** the SQLite context, model and migration chains are unchanged: `EfOrdinalCollation.Sqlite` declares nothing and value comparers are not part of the snapshot (`EtlDefinitionStoreTests` checks pending model changes). No persisted shape changes; artifacts keep their bytes.
- **Wrapping** (§2.23.5): a publication that runs out of attempts rethrows the provider failure as the store's persistence failure, as its other storage failures already surface.
- **Tests:** the design-store tests become abstract contract classes run by a SQLite subclass in `Elsa.Etl.Tests` and by PostgreSQL, SQL Server and MySQL subclasses in the provider suite.

## Changes by project

- **Persistence (`Elsa.Etl.Persistence.EntityFrameworkCore`)**
  - `EtlDesignDbContext`: provider contexts apply `EfOrdinalCollation` (key and index columns) and set the text column type (`TEXT`, `nvarchar(max)`, `text`, `longtext`); MySQL also sets the utf8mb4 character set.
  - New `EtlDesignSqlServerDbContext`, `EtlDesignPostgreSqlDbContext`, `EtlDesignMySqlDbContext`; `[EfModule]` names all four.
  - `EtlDesignEntityFrameworkCoreRegistration`: `Binding.Select` over the four contexts instead of the SQLite guard. The runtime registration keeps its SQLite guard until the next slice.
  - `EfEtlDefinitionCatalog`: order and seek on `DefinitionKey` directly.
  - `EfEtlDefinitionStore.PublishAsync`: the whole unit, from the existing-publication check through the transaction's disposal, runs through `EfWriteRetry` with `EfWriteConflict.Transient`; each attempt clears the change tracker; a rollback after the server already aborted the transaction does not hide the conflict; exhaustion rethrows the last failure.
  - Generated internal migrations under `Migrations/EtlDesign/{SqlServer,PostgreSql,MySql}`; the manifest's provider setting and the csproj description name four providers.
- **Tooling (`Extensions/Etl/src/Tooling`, `Elsa.Etl.Tooling`)**: design-time factories for the four design contexts (internal, over the linked `ModuleDesignTimeFactory`), and `tools/etl/generate-etl-design-migrations.sh` modelled on the Rules script (refuses the SQLite context, makes the migration internal, strips provider calls). Added to `Elsa.Server.slnx` and, build-only, to the architecture tests.
- **Shared EF tooling**: `generate-module-migrations.sh` excludes `Extensions/Etl` and `Extensions/Rules` from its snapshot-deletion loop, which today would delete their retained chains on an unfiltered run.
- **ETL tests**: design-store contract classes in `Extensions/Etl/tests/Shared` (or linked files), the SQLite subclasses in `Elsa.Etl.Tests`; SQLite-only cases (the publication trigger, the retained fixture database) stay SQLite-only.
- **Provider suite (`Extensions/Etl/tests/Persistence/ProviderTests`, `Elsa.Etl.Persistence.ProviderTests`)**: fixtures for PostgreSQL, SQL Server and MySQL (a supplied connection string or a container, skipping without Docker unless `ELSA_ETL_EF_REQUIRE_NATIVE_PROVIDERS` is set); a database per test; the migrate–validate–no-pending-changes check and a collation-in-the-model check per engine; the concurrent publish and update races. Registered in `tools/ci/test-tiers.json` (`projects`, `containerSuites`, an ef shard) and `Elsa.Server.slnx`; lock files committed.
- **Docs**: ETL README persistence section, the persistence project README, a follow-up issue for the shared generator hazard if not fixed here, the evidence README.

## Risks

- **Oracle MySQL provider**: binary collation needs the `MySQL:Collation` annotation as well (`EfOrdinalCollation` sets both); verify the generated migration carries it.
- **SQL Server deadlock victims**: `RollbackAsync` can throw on a transaction the server already rolled back; dispose instead, so the deadlock is what the retry sees.
- **Read-committed alternative**: on the server engines, publication could use READ COMMITTED with its conditional update instead of Serializable; kept Serializable here so SQLite behaviour and the existing tests are unchanged, with the retry absorbing serialization failures.
