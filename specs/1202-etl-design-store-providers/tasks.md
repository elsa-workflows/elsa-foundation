# Tasks: The ETL design store on all four EF engines

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Model and registration

- [x] T001 Provider contexts for SQL Server, PostgreSQL and MySQL with `EfOrdinalCollation` (provider-default text types); SQLite applies the comparers only; `[EfModule]` names all four.
- [x] T002 `Binding.Select` in `EtlDesignEntityFrameworkCoreRegistration`; the runtime keeps its guard.
- [x] T003 The SQLite model snapshot is unchanged (pending-changes check passes).

## Store behaviour

- [x] T004 Drop `EF.Functions.Collate(..., "BINARY")` from `EfEtlDefinitionCatalog`.
- [x] T005 Run `PublishAsync` as a whole unit through `EfWriteRetry` (`Transient`); clear the tracker per attempt; exhaustion rethrows.
- [x] T005a (review) An update reports what it wrote and retries a transient conflict; a failed create or publication attempt leaves nothing tracked.
- [x] T005b (review) Store and catalog reads retry a transient conflict.

## Tooling and migrations

- [x] T006 `Elsa.Etl.Tooling` with internal factories; `InternalsVisibleTo` for it and the provider suite; slnx and architecture-test build reference.
- [x] T007 `tools/etl/generate-etl-design-migrations.sh`; generate internal `Initial` for the three new contexts.
- [x] T008 Exclude `Extensions/Etl` and `Extensions/Rules` from the shared generator's deletion loop.

## Contract tests

- [x] T009 Design-store tests become abstract contract classes; SQLite subclasses in `Elsa.Etl.Tests`; SQLite-only cases stay SQLite-only.
- [x] T010 Add the concurrent publish and update races to the contract (the case-only catalog case already existed).
- [x] T011 `Elsa.Etl.Persistence.ProviderTests` with PostgreSQL, SQL Server and MySQL fixtures; migrate–validate–no-pending-changes and collation checks; register in `test-tiers.json`, slnx and lock files.

## Evidence and docs

- [x] T012 Bite-prove provider selection, the collation and the publish retry; list what is not bite-proven.
- [x] T013 ETL README, persistence README, manifest description, evidence README.
