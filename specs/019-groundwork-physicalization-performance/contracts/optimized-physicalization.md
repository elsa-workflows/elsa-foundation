# Contract: Optimized Physicalization

Optimized physicalization is an opt-in provider behavior driven by `StorageUnit.Physicalization`.

## Portable Units

- Use the existing document-store behavior.
- Materializers create only the portable document/index/schema-history structures.
- Equality queries use the portable declared-index path.

## Optimized Units

- Continue to save canonical JSON content through `IDocumentStore`.
- Derive physicalized fields from declared single-field equality indexes.
- Materializers create provider-owned optimized structures.
- Save, update, and delete operations keep optimized structures consistent with canonical content.
- Equality queries on eligible declared indexes use optimized structures.
- Query results and write statuses match portable document-store semantics.

## Non-Goals

- No provider-specific caller APIs.
- No automatic migration of runtime hot paths.
- No compound-index, full-text, range, or sort-optimized query contract in G7.
- No requirement that runtime-defined entities use optimized physicalization by default.
