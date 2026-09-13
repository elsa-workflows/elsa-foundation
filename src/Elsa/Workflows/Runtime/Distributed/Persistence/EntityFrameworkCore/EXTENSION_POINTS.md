# Extension points — distributed runtime EF Core placement persistence

This opt-in provider replaces exactly `IExecutionPlacementStore` with
`EfExecutionPlacementStore`. It owns one provider-neutral EF Core model and does not register,
replace, or depend on the distributed command transport. `WorkflowsRuntimeDistributed` remains the
default in-memory composition; the Groundwork feature remains the default durable composition for
the existing distributed family until a separately authorized default-flip and deletion slice.

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
renewals and expired takeovers use revision CAS, and release uses owner/token compare-and-delete.
Listing filters live rows and applies `Take` in SQL, ordering by UTC expiry ticks and an ordinal
UTF-16 order key. Missing scope, invalid input, and cancellation are rejected before provider I/O.

Schema creation is deliberately test-owned (`EnsureCreated`); this slice adds no migration or
default-flip artifacts.
