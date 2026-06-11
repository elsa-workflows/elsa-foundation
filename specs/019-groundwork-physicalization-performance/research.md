# Research: Groundwork Physicalization And Performance

## Decision: Optimize Declared Single-Field Equality Indexes First

Rationale: All existing document-store providers already support declared single-field equality indexes. Projecting that same shape into provider-native structures creates a small, testable optimization without expanding the query contract.

Alternatives considered: Compound indexes and sort-specific projections. These are useful but would expand query semantics before G8 benchmark evidence exists.

## Decision: Keep `IDocumentStore` As The Caller Contract

Rationale: G7 is a provider optimization slice. Callers should not choose different APIs based on physicalization policy.

Alternatives considered: A separate optimized document-store interface. This would expose provider internals and weaken the generic Groundwork boundary.

## Decision: Use SQLite As The Relational Proof

Rationale: SQLite exercises the shared relational document store and materializer with fast deterministic tests. SQL Server and PostgreSQL inherit the shared behavior, while provider-specific SQL can be validated in later hardening if needed.

Alternatives considered: Running SQL Server and PostgreSQL optimized tests in G7. This would increase test cost without proving a different code path for the store logic.

## Decision: MongoDB Stores Optimized Projection Values Separately From Content

Rationale: Querying projected fields lets MongoDB use stable provider-owned field names and indexes independent of nested document content paths.

Alternatives considered: Reusing `content.<path>` indexes only. That already exists from G5 and would not prove a new physicalization path.
