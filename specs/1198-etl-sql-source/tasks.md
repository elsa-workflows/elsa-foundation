# Tasks: SQL snapshot reads

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md)

## Phase 1 — Model and compiler
- [x] T001 `EtlSqlSource`, PublicAPI. Test: existing artifact bytes and identities unchanged; round trip.
- [x] T002 Compiler rules: identifiers, case-insensitive distinct columns, key of 1 to 8 distinct required columns, field count. Tests.

## Phase 2 — Engine contract and reader
- [x] T003 `IEtlSqlEngine` snapshot members; the SQLite test engine implements them, with a transient-failure hook for reads.
- [x] T004 `EtlSqlSnapshotReader`: snapshot transaction, ordered read, typed conversion, JSON object per row, error wrapping. Tests on the test engine.

## Phase 3 — Bindings, preparation and processing
- [x] T005 `HostSqlBindings` role `Source`; `HostConnectorBindings` resolves a `sql` source. Tests.
- [x] T006 Preparer: reuse a published snapshot, else read; index; transient result; execution returns on it. Tests: reuse after the table changed, transient then success, rejection.
- [x] T007 Every JSON Lines source site admits a `sql` source, anchored on the recorded hash (FR-006). Tests.
- [x] T008 A journey on the SQLite test engine (SQL to JSON Lines over two partitions, NULL to the error route, empty table) and real worker processes killed after snapshot publication, reusing it.

## Phase 4 — Engines and the container suite
- [x] T009 PostgreSQL and SQL Server snapshot members.
- [x] T010 Container tests: types and order, isolation against a concurrent change, missing table, SQL Server without snapshot isolation.

## Phase 5 — Studio, docs and evidence
- [x] T011 Studio authoring and inspection, with Vitest and Playwright (axe).
- [x] T012 READMEs, `EXTENSION_POINTS.md`.
- [x] T013 Bite-proofs, Workbench journey (PostgreSQL to PostgreSQL), evidence README, the whole ETL, architecture and container suites.
