# External authentication flow persistence

Optional `FoundationIdentityExternalAuthenticationEntityFrameworkCore` stores one-time authentication and
preview artifacts. It is separate from Identity IAM and provider-configuration contexts. Canonical contracts
and flow behavior: [managed authentication](../../Elsa.Foundation.Identity.ExternalAuthentication/README.md).

Programmatic registration is `AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite",
ConnectionString = ... })`. The host supplies the selected provider engine. The shell feature additionally
registers module migrations; programmatic hosts must run their ordinary EF module migration lifecycle before
handling authentication requests. The module name is `Identity.ExternalAuthentication`, with its own migration
history and `IdentityExternalAuthentication` schema family at `1.0.0`. New Initial migrations/snapshots exist for
SQLite, PostgreSQL, SQL Server and MySQL. This does not establish native runtime qualification for every engine.

Each immutable row holds hashed ID/tenant/browser/admin bindings, purpose, creation/expiry milliseconds,
protected payload, a random version and a schema stamp. Consumption reads only a matching unexpired row,
checks schema readability, then conditionally deletes that same version; only the successful delete returns
the payload. Caller-owned EF or ambient transactions are rejected because a returned consume must already be
committed. Uncertain provider failures propagate and cannot issue a session. A retry after a committed delete
finds no artifact. Wrong tenant/purpose/browser/admin does not consume the legitimate row.

Payloads are bounded to 196608 characters, keys/bindings to SHA-256 lower-case hex and artifact lifetime to
twenty minutes. Creation removes at most 64 expired current-schema rows of its own tenant. Foreign tenants and
future-schema rows survive cleanup. Data Protection keys are host-owned; use the same application name/shared
key ring on every node and the host's normal at-rest key protection. Artifacts are encrypted independently of
the database; this module neither generates a private node-local authority nor stores upstream credentials.

Native SQLite tests prove migrations, reopen, deliberately synchronized concurrent consumption, all bindings,
expiry, schema skew, scope checks, transaction rejection and bounded cleanup. Flow component tests also prove
real HTTP code exchange and preview across independent service providers sharing SQLite and owned key files.
Separate-process browser/session and server-database qualification remain required by spec 1205.
