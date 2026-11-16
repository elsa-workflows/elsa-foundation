# Tasks: Transactional keyed SQL delivery

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

## Phase 1 — Model and compiler
- [x] T001 `EtlSqlDestination`, `EtlConnectorKind.Sql` (appended), `ConnectorSqlEffectKey`, PublicAPI. Test: existing artifact bytes and identities unchanged.
- [x] T002 Compiler rules: main route only, identifiers, case-insensitive distinct fields, key from required fields (1 to 8). Tests.

## Phase 2 — Engine contract and writer
- [x] T003 `IEtlSqlEngine` with `EtlSqlTarget` and `EtlSqlErrorKind`. A SQLite test engine.
- [x] T004 `EtlSqlRouteWriter`: ledger ensure (creation race), insert, replay, conflict, key check, ordered chunked upserts, commit, error wrapping. Tests on the test engine.

## Phase 3 — Bindings, dispatch and publication
- [x] T005 `EtlHostSqlBindingOptions`, `HostSqlBindings` (secret-free handle, allowed tables, engine resolution), `HostConnectorBindings`, `EtlRuntimeFeature.SqlBindings`.
- [x] T006 Output processor: the JSON-array route body for `sql` and dispatch through the writer; execution injects the engines; the executable reader admits `sql`.
- [x] T007 Every publication site admits `Sql` (R7); SQL shares the recorded-publication `EffectMechanism` value 2. Tests.
- [x] T008 A journey on the SQLite test engine (CSV to SQL, upsert, rerun, missing engine) and real worker processes killed between the commit and the record, replayed through the ledger.

## Phase 4 — Engine packages and the container suite
- [x] T009 `Elsa.Etl.Sql.PostgreSql` and `Elsa.Etl.Sql.SqlServer` (engines, features, READMEs, PublicAPI).
- [x] T010 The container suite: the writer protocol and key detection per engine, and PostgreSQL error classification. CI tiers and container-suite entries. (The host journey and process kills run on the SQLite test engine in T008, and on PostgreSQL in the Workbench journey.)

## Phase 5 — Studio, docs and evidence
- [x] T011 Studio authoring and inspection, with Vitest and Playwright (axe).
- [x] T012 READMEs, `EXTENSION_POINTS.md`, solution filters.
- [x] T013 Bite-proofs, Workbench journey (PostgreSQL), evidence README, the whole ETL, architecture and container suites.
