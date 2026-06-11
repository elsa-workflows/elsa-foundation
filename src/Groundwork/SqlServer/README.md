# Groundwork.SqlServer

`Groundwork.SqlServer` provides SQL Server materialization and document storage for portable Groundwork document workloads.

## Current Scope

- Creates document, declared-index, and schema-history tables.
- Saves, loads, updates, deletes, and equality-queries JSON document envelopes.
- Maintains declared indexes transactionally with document writes.
- Enforces unique declared indexes with a filtered unique index.
- Uses optimistic concurrency through expected document versions.

## Deliberate Limits

- Equality queries only.
- JSON content is stored as text.
- Document kinds, document ids, declared index names, declared index values, and physicalized projection values are constrained to `NVARCHAR(450)` because they participate in SQL Server keys or indexes. Keep portable identifiers and indexed values within that limit, or add validation before writing.
- No Entity Framework dependency.
- No Elsa dependency.
