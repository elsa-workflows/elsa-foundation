# Groundwork.PostgreSql

`Groundwork.PostgreSql` provides PostgreSQL materialization and document storage for portable Groundwork document workloads.

## Current Scope

- Creates document, declared-index, and schema-history tables.
- Saves, loads, updates, deletes, and equality-queries JSON document envelopes.
- Maintains declared indexes transactionally with document writes.
- Enforces unique declared indexes with a partial unique index.
- Uses optimistic concurrency through expected document versions.

## Deliberate Limits

- Equality queries only.
- JSON content is stored as text.
- No Entity Framework dependency.
- No Elsa dependency.
