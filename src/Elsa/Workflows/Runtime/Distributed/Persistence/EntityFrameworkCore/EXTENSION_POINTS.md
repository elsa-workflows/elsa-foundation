# Extension points — distributed runtime EF Core placement persistence

This opt-in provider replaces exactly `IExecutionPlacementStore` with
`EfExecutionPlacementStore`. It owns one provider-neutral EF Core model and does not register,
replace, or depend on the distributed command transport. `WorkflowsRuntimeDistributed` remains the
default in-memory composition; this feature is the durable one, and placement and command-transport
ownership stay independently selectable.

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

## D02-D03 command-stream and transport persistence

The opt-in D02-D03 provider replaces exactly `IExecutionCommandTransport` with the EF command
transport adapter. It owns both the per-execution stream-head and ordered transport-item entities in
one dedicated EF context, so send, lease, and acknowledgement can update the projection and item in
one relational transaction. It does not register or replace `IExecutionPlacementStore`; D01 placement
ownership remains independently selectable; the in-memory transport remains in place until this EF
transport is explicitly selected.

The production project references only provider-neutral EF Core/Relational packages. SQLite, SQL
Server, PostgreSQL, and MySQL engine packages remain test or host responsibilities. Provider contexts
bind the same model and no domain contract exposes EF types, persistence entities, `IQueryable`, or SQL.

The complete transport item is persisted as a lossless JSON payload alongside typed routing and
queue-management columns. The original UTF-16 workflow-execution identity, partition, envelope
sequence, payload, metadata, timestamps, offsets, and idempotency data are retained without relying
on provider collation. Scope and identity keys use provider-safe deterministic projections so the
same execution ID in different persistence scopes cannot collide.

The stream head is the authoritative high-water and pending projection. Mutations use serializable
transaction boundaries with bounded retries for provider-reported serialization, deadlock, and CAS
conflicts. A send advances its sequence with compare-and-swap and inserts the item atomically; a
transaction rolled back before commit leaves neither an orphan item nor a committed gap. Addressed
reads cross-check the exact item count, sequence range, earliest visibility projection, encoded scope,
and lossless payload before trusting the head. Leases select visible rows in sequence order with a provider-side
bound, advance the persisted visibility summary, and increment a monotonic delivery/lease token.
Acknowledgements require an exact live owner/token match and atomically delete one item while
recomputing the head. Expired leases are redeliverable after context or process restart, while stale
owners cannot acknowledge a successor lease. Pending execution discovery is visible-only,
deterministically ordinal, provider-filtered, and bounded in SQL; pending count includes leased
rows and comes from the durable head.

The D02-D03 implementation is tracked by [#1720](https://github.com/elsa-workflows/elsa-foundation/issues/1720).
Its SQLite behavior suite and SQL Server/PostgreSQL/MySQL live smoke suite are the executable proof;
provider-specific migrations, default flips, and broad host journeys remain
deferred by that task.
