# Implementation Plan: The ETL design store on all four EF engines

**Branch**: `agent/claude/2908-etl-design-providers` | **Spec**: [spec.md](spec.md)

## Summary

Follow the Rules module, whose EF contexts are internal like ETL's: provider contexts per engine over the existing provider-neutral `EtlDesignDbContext`, selected at registration with `EfModuleBinding.Select`; an ETL-owned design-time tooling project and script that generate internal migrations; and an ETL-owned provider contract suite. Replace the SQLite-only collation function with per-column binary collation, and run publication through `EfWriteRetry`.

## Constitution check

- **Persistence family** (ADR 0073 D1): EF Core only; the module package stays provider-free (EF Core and Relational); hosts bring the provider package.
- **Visibility and domains** (§2.23.3): the contexts and migrations stay internal. `InternalsVisibleTo` goes only to ETL-domain assemblies (`Elsa.Etl.Tooling`, `Elsa.Etl.Persistence.ProviderTests`), as Rules grants `Elsa.Rules.Tooling`; the shared tools and migration matrix are not used, because both live in other domains (`InternalsVisibleTo_grants_stay_within_the_granting_domain`).
- **Compatibility:** the SQLite migration chains and model snapshot are unchanged: `EfOrdinalCollation.Sqlite` declares no collation, so the SQLite context only gains ordinal value comparers, which are not part of the snapshot (`EtlDefinitionStoreTests` checks pending model changes). No persisted shape changes; artifacts keep their bytes. Publication's, the draft update's and the reads' retries run on SQLite too, where a busy or locked database is a transient conflict that previously propagated at once.
- **Wrapping** (§2.23.5): ETL has no persistence exception type; a publication, draft update or read that runs out of attempts rethrows the last provider failure unchanged, as the store's other storage failures already surface (a pre-existing shape, not widened here). Wrapping the design store's storage failures is follow-up #3758.
- **Tests:** the design-store tests become abstract contract classes run by a SQLite subclass in `Elsa.Etl.Tests` and by PostgreSQL, SQL Server and MySQL subclasses in the provider suite.

## Changes by project

- **Persistence (`Elsa.Etl.Persistence.EntityFrameworkCore`)**
  - `EtlDesignDbContext`: provider contexts apply `EfOrdinalCollation` (key and index columns); unbounded text keeps each provider's default type; MySQL also sets the utf8mb4 character set.
  - New `EtlDesignSqlServerDbContext`, `EtlDesignPostgreSqlDbContext`, `EtlDesignMySqlDbContext`; `[EfModule]` names all four.
  - `EtlDesignEntityFrameworkCoreRegistration`: `Binding.Select` over the four contexts instead of the SQLite guard. The runtime registration keeps its SQLite guard until the next slice.
  - `EfEtlDefinitionCatalog`: order and seek on `DefinitionKey` directly.
  - `EfEtlDefinitionStore.PublishAsync`: the whole unit, from the existing-publication check through the transaction's disposal, runs through `EfWriteRetry` with `EfWriteConflict.Transient`; each attempt clears the change tracker; a rollback after the server already aborted the transaction does not hide the conflict; exhaustion rethrows the last failure.
  - `EtlDesignReadRetry`: store and catalog reads retry a transient conflict, since on SQL Server a read-committed read can be the victim of a deadlock between publications; a publication attempt's own reads are unretried, because the publication's retry runs its whole unit again.
  - `EfEtlDefinitionStore` draft writes: an update retries its one conditional statement on a transient write conflict (on SQL Server it can be a publication's deadlock victim) and reports the revision, pipeline, hash, format, schema version and timestamp it wrote rather than a re-read another writer may have advanced; a failed create or publication attempt leaves nothing tracked on the context.
  - Generated internal migrations under `Migrations/EtlDesign/{SqlServer,PostgreSql,MySql}`; the manifest's provider setting and the csproj description name four providers.
- **Tooling (`Extensions/Etl/src/Tooling`, `Elsa.Etl.Tooling`)**: design-time factories for the three server-engine design contexts (internal, over the linked `ModuleDesignTimeFactory`; the SQLite chains are hand-maintained), and `tools/etl/generate-etl-design-migrations.sh` modelled on the Rules script (refuses the SQLite context, makes the migration internal, strips provider calls). Added to `Elsa.Server.slnx` and, build-only, to the architecture tests.
- **Shared EF tooling**: `generate-module-migrations.sh` excludes `Extensions/Etl` and `Extensions/Rules` from its snapshot-deletion loop, which today would delete their retained chains on an unfiltered run.
- **ETL tests**: design-store contract classes in `Extensions/Etl/tests/Shared` (or linked files), the SQLite subclasses in `Elsa.Etl.Tests`; SQLite-only cases (the publication trigger, the retained fixture database, and the update/re-read interleave, retry exhaustion, throwing rollback, cancelled create and busy reads) stay SQLite-only.
- **Provider suite (`Extensions/Etl/tests/Persistence/ProviderTests`, `Elsa.Etl.Persistence.ProviderTests`)**: fixtures for PostgreSQL, SQL Server and MySQL (a supplied connection string or a container, skipping without Docker unless `ELSA_ETL_EF_REQUIRE_NATIVE_PROVIDERS` is set); a database per test; the migrate–validate–no-pending-changes check and a check of the server's recorded key-column collation per engine (the model side is the model-agreement check); the concurrent publish and update races. Registered in `tools/ci/test-tiers.json` (`projects`, `containerSuites`, an ef shard) and `Elsa.Server.slnx`; lock files committed.
- **Docs**: ETL README persistence section, the persistence project README, the evidence README.

## Risks

- **Oracle MySQL provider**: binary collation needs the `MySQL:Collation` annotation as well (`EfOrdinalCollation` sets both); verify the generated migration carries it.
- **SQL Server deadlock victims**: `RollbackAsync` can throw on a transaction the server already rolled back; dispose instead, so the deadlock is what the retry sees.
- **Read-committed alternative**: on the server engines, publication could use READ COMMITTED with its conditional update instead of Serializable; kept Serializable here so SQLite behaviour and the existing tests are unchanged, with the retry absorbing serialization failures.
