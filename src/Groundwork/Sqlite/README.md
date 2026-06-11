# Groundwork.Sqlite

`Groundwork.Sqlite` is the first concrete Groundwork provider. It materializes portable document storage for a `StorageManifest` using `Microsoft.Data.Sqlite` directly, without Entity Framework or Elsa dependencies.

## Current Scope

- Creates the shared document, declared-index, and schema-history tables.
- Saves, loads, updates, deletes, and queries JSON document envelopes.
- Maintains declared index rows transactionally with document writes.
- Enforces optimistic concurrency with expected document versions.
- Rejects queries for undeclared indexes.
- Enforces unique declared indexes through SQLite constraints.

## Deliberate Limits

- Equality queries only.
- Single-field index extraction only.
- JSON content is stored as text; provider-specific JSON indexing is deferred.
- This package is a Groundwork provider package, not an Elsa integration package.
