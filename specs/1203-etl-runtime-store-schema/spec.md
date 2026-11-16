# Feature Specification: The ETL runtime store's schema on all four EF engines

**Feature Branch**: `agent/claude/2908-etl-runtime-providers` (stacked on `agent/claude/2908-etl-design-providers`)

**Created**: 2026-11-04

**Status**: Implemented — delivered by this implementation PR when merged

**Input**: The [store qualification obligation](https://github.com/elsa-workflows/elsa-foundation/issues/2908#issuecomment-6323246976) of [#2908](https://github.com/elsa-workflows/elsa-foundation/issues/2908) (ADR 0073 D1). [Spec 1202](../1202-etl-design-store-providers/spec.md) qualified the design store; this slice gives the **runtime** store a correct schema and model on SQL Server, PostgreSQL and MySQL. Making its transitions safe under concurrent writers on those engines is the next slice (spec 1206); until then the runtime registration keeps refusing them. Part of [Program #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892).

## Context

The runtime store (`EtlRuntimeDbContext`, `EfEtlRuntimeStore`, the connector stores) has one SQLite context and a hand-written SQLite migration chain. Its model cannot be created on the other engines:
- `RunKey`, the base64 encoding of the run id (up to 1,200 characters), is in every key and foreign key, past SQL Server's 900-byte clustered key and MySQL's 3,072-byte index limit.
- Its check constraints are written in SQLite's dialect: unquoted PascalCase identifiers (PostgreSQL folds them), `length()` (SQL Server has `LEN`; MySQL's counts bytes) and booleans compared with 0 and 1 (an error on PostgreSQL).
- Two alternate-key names are longer than MySQL's 64-character limit and PostgreSQL's 63.
- Key columns carry no binary collation, so keyset paging and grouping on run keys would follow a linguistic order.
- MySQL aggregates `long` columns as `DECIMAL`; the provider must materialize the exact `long` totals the store contracts require.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A runtime database on any first-party engine (Priority: P1)

A host's operator migrates the ETL runtime module into a SQL Server, PostgreSQL or MySQL database. The schema is created, validates, and agrees with the model, and single-writer runtime operations behave as on SQLite.

**Independent Test**: on real containers, a fresh database migrates, validates with no pending model changes, and the runtime-store contracts that do not race pass on each engine and on SQLite.

**Acceptance Scenarios**:
1. **Given** a fresh database on each server engine, **When** the runtime module migrates, **Then** the schema is created, the module validates, and the model has no pending changes.
2. **Given** a run id of the maximum length, and run ids that differ only by case, **When** they are admitted, run and read back on a server engine, **Then** each is a distinct run with its own id, and no index rejects a key.
3. **Given** a row that violates a check constraint (an invalid digest length, an inconsistent flag), **When** it is written on any engine, **Then** the database refuses it as SQLite does.
4. **Given** runs, partitions and batches on a server engine, **When** the store pages runs or totals a run's partitions, **Then** paging is stable in key order and totals are exact `long` values.

### User Story 2 — SQLite unchanged (Priority: P1)

1. **Given** an existing SQLite runtime database, **When** the host upgrades, **Then** the retained SQLite chain applies unchanged and the SQLite model snapshot does not change, including its check constraint text.

### Edge Cases

- **Run keys.** On the server engines `RunKey` is a 64-character domain-separated SHA-256 of the run id; `RunId` keeps the full encoded id, which is what every read decodes. A row whose key is not its id's hash is refused as invalid data, so a collision can never merge two runs. SQLite keeps its encoded key.
- **Run order.** The store's run listing is in key order, which on the server engines is hash order and on SQLite the encoded id's order; the store contract already leaves the order to the provider.
- **Concurrency.** Concurrent transitions on one run, or on one partition, are not qualified on the server engines in this slice (spec 1206); the registration still refuses them, and the provider suite builds stores directly.

## Requirements *(mandatory)*

- **FR-001** The runtime store has SQL Server, PostgreSQL and MySQL contexts beside SQLite, each applying the binary collation and, on MySQL, the utf8mb4 character set; they join `[EfModule]` when spec 1206 lifts the registration guard.
- **FR-002** A context-level run-key seam derives `RunKey` from the run id: the encoded id on SQLite, a 64-character hash on the server engines, where every `RunKey` column is 64 characters; identity checks compare the stored key with the id's key.
- **FR-003** Check constraints are rendered per engine from one declaration (identifier quoting, length function, boolean literals); the SQLite text is byte-identical to today's. The two long alternate-key names are shortened on the server engines.
- **FR-004** Run-partition totals and the inspector's counts are exact `long` values on every engine; retain the existing aggregate queries where the provider contracts qualify their materialization.
- **FR-005** Each server engine has generated, internal runtime migrations made by the ETL migration tooling, including the compaction model adopted from current main; the SQLite chain is untouched.
- **FR-006** The runtime-store contracts that do not race run on SQLite in `Elsa.Etl.Tests` and on the three server engines in the `etl-ef` suite, with migrate/validate/model-agreement, recorded key collation, maximum-length identity and check-constraint checks.
- **FR-007** The runtime registration keeps refusing non-SQLite providers until spec 1206; docs say so.

## Success Criteria *(mandatory)*

- **SC-001** On each server engine a fresh runtime database migrates and validates with no pending model changes, and the non-racing contracts pass.
- **SC-002** Maximum-length and case-only run ids round-trip as distinct runs on every engine.
- **SC-003** Every new guard is bite-proven or named as not bite-proven.
- **SC-004** SQLite runtime databases, the SQLite model snapshot and existing artifacts are unchanged.

## Decisions taken by default (owner may revisit)

- Hash-only run-key uniqueness on the server engines, rather than shortening the 450-character run-id limit, following the design store and the Workflows runtime.
- Run-list order follows each engine's key (allowed by the store contract).
- Serializable isolation is kept; spec 1206 retries its transitions as wholes, as spec 1202's publication does, and rethrows the provider failure when retries run out (#3758 tracks wrapping).
