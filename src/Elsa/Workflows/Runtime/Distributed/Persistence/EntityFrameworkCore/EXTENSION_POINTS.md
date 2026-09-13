# Extension points — distributed runtime EF Core placement persistence

This opt-in provider replaces exactly `IExecutionPlacementStore` with
`EfExecutionPlacementStore`. It owns one provider-neutral EF Core model and does not register,
replace, or depend on the distributed command transport. `WorkflowsRuntimeDistributed` remains the
default in-memory composition; the Groundwork feature remains the default durable composition for
the existing distributed family until a separately authorized default-flip and deletion slice. In a mixed
composition Groundwork retains its two command-transport units, while the unused Groundwork placement-unit
declaration is skipped or withdrawn in either registration order so only the EF D01 schema is provisioned.

## Provider boundary

The production project references only `Microsoft.EntityFrameworkCore`,
`Microsoft.EntityFrameworkCore.Relational`, and `Elsa.Persistence.EntityFramework`. SQLite, SQL
Server, PostgreSQL, and MySQL engine packages are host/test responsibilities. Derived contexts bind
only provider column details; provider selection uses the shared reflection binding.

## Persistence semantics

Rows are scoped by a SHA-256 identity digest over length-prefixed raw UTF-16 scope and execution
identity. The scope column is a provider-safe Base64 encoding of its UTF-16 code units, retaining
even malformed scope values losslessly; execution and owner contracts retain their original
well-formed strings. An explicit revision is an EF concurrency token. Claims
use bounded read/insert-or-update/save retries; first claims use the primary-key uniqueness boundary,
renewals and expired takeovers use revision CAS, and release marks a durable tombstone rather than
deleting the row. The tombstone preserves the per-execution fencing high-water mark, so reclaim
advances both token and revision and a delayed release cannot match a successor.
Listing filters live rows and applies `Take` in SQL, ordering by UTC expiry ticks and an ordinal
UTF-16 order key encoded as fixed-width big-endian binary data, so database collation cannot alter
the result. Provider, persisted-state, and exhausted-contention failures cross the adapter boundary
as `ExecutionPlacementEntityFrameworkPersistenceException`, preserving the infrastructure cause and
operation identity. Missing scope, invalid input, and cancellation are rejected before provider I/O.
The selected placement backend captures its exact service descriptor and revalidates exclusive
ownership through options startup validation, catching host registrations added after feature setup.

Schema creation is deliberately test-owned (`EnsureCreated`); this slice adds no migration or
default-flip artifacts.
