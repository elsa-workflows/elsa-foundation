# Contract: Planner Kernel

G1 exposes provider-neutral relational and document planners.

## Relational Planner

Input:

- Validated storage manifest.
- Provider capability report or already accepted compatibility result.

Output:

- Relational plan with table-like, column-like, index-like, concurrency, and history operations.
- Diagnostics for warnings and unsupported requirements.

Rules:

- Does not render SQL.
- Does not choose concrete provider data types.
- Does not apply migrations.
- Preserves all declared indexes or reports why planning is blocked.

## Document Planner

Input:

- Validated storage manifest.
- Provider capability report or already accepted compatibility result.

Output:

- Document plan with envelope, generic index, query support, concurrency, and history operations.
- Diagnostics for warnings and unsupported requirements.

Rules:

- Does not create database collections or tables.
- Does not bind to MongoDB, SQLite, SQL Server, or PostgreSQL.
- Does not perform query execution.
- Preserves all declared indexes or reports why planning is blocked.

## Shared Planner Requirements

- Same sample manifest can produce both plan kinds.
- Unsupported workload family blocks planning.
- Unsupported required index capability blocks planning.
- Planned operations must include schema-history intent.
