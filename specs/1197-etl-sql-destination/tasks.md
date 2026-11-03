# Tasks: Transactional keyed SQL delivery

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

## Phase 1 — Model and compiler
- [ ] T001 `EtlSqlDestination`, `EtlConnectorKind.Sql` (appended), `ConnectorSqlEffectKey`, PublicAPI. Test: existing artifact bytes and identities unchanged.
- [ ] T002 Compiler rules: main route only, identifiers, case-insensitive distinct fields, key from required fields (1 to 8). Tests.

## Phase 2 — Engine contract and writer
- [ ] T003 `IEtlSqlEngine` with `EtlSqlTarget` and `EtlSqlErrorKind`. A SQLite test engine.
- [ ] T004 `EtlSqlRouteWriter`: ledger ensure (creation race), insert, replay, conflict, key check, ordered chunked upserts, commit, error wrapping. Tests on the test engine.

## Phase 3 — Bindings, dispatch and publication
- [ ] T005 `EtlHostSqlBindingOptions`, `HostSqlBindings` (secret-free handle, allowed tables, engine resolution), `HostConnectorBindings`, `EtlRuntimeFeature.SqlBindings`.
- [ ] T006 Output processor: the JSON-array route body for `sql` and dispatch through the writer; execution injects the engines; the executable reader admits `sql`.
- [ ] T007 Every publication site admits `Sql` (R7), with a new `EffectMechanism` value. Tests.
- [ ] T008 A journey on the SQLite test engine: CSV to SQL, upsert, rerun, replay after a simulated crash.

## Phase 4 — Engine packages and the container suite
- [ ] T009 `Elsa.Etl.Sql.PostgreSql` and `Elsa.Etl.Sql.SqlServer` (engines, features, READMEs, PublicAPI).
- [ ] T010 The container suite: writer protocol and key detection per engine; a host journey; real-process kills between the commit and the record. CI tiers and container suite entries.

## Phase 5 — Studio, docs and evidence
- [ ] T011 Studio authoring and inspection, with Vitest and Playwright (axe).
- [ ] T012 READMEs, `EXTENSION_POINTS.md`, solution filters.
- [ ] T013 Bite-proofs, Workbench journey (PostgreSQL), evidence README, the whole ETL, architecture and container suites.
