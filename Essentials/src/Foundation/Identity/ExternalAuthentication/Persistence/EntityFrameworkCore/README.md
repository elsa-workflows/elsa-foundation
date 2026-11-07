# External authentication flow and session persistence

Optional `FoundationIdentityExternalAuthenticationEntityFrameworkCore` stores one-time authentication and
preview artifacts plus durable canonical session records. It is separate from Identity IAM and provider-configuration contexts. Canonical contracts
and flow behavior: [managed authentication](../../Elsa.Foundation.Identity.ExternalAuthentication/README.md).

Programmatic registration is `AddExternalAuthenticationEntityFrameworkCore(new() { Provider = "Sqlite",
ConnectionString = ... })`. The host supplies the selected provider engine. The shell feature additionally
registers module migrations; programmatic hosts must run their ordinary EF module migration lifecycle before
handling authentication requests. The module name is `Identity.ExternalAuthentication`, with its own migration
history and `IdentityExternalAuthentication` schema family at `1.2.0`. The additive `DurableSessions` migration
and `QualifiedLinkSessionBinding` schema step preserve Initial history and prior one-time flows through the
1.0.0→1.1.0→1.2.0 chain for SQLite, PostgreSQL, SQL Server and MySQL. Old external session payloads lacking the
exact link-revision binding require a fresh sign-in; migration never invents that binding. Schema-chain support
does not establish native runtime qualification for every engine.

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

`IAuthenticationSessionStore` owns independently committed tenant/user-scoped records in
`identity_authentication_sessions`. Each has a random 256-bit ID, creation/absolute expiry, immutable protected
account-version/provider/link provenance, a random revision and explicit revocation time. No upstream token,
password, client secret or raw framework security stamp is stored. Purpose protection binds the payload to its
tenant/ID/user/time indexes. Paging is deterministic and limited to 100 records. Revocation conditionally updates
one expected revision; an already revoked session is idempotent, a race conflicts and a provider failure remains
a failure. Create/revoke reject caller-owned transactions. Cleanup removes at most 64 expired current-schema rows
of the current tenant per creation, preserving foreign tenants and unknown future schemas.

The canonical ASP.NET authority binds normal local/external cookies to these records. First-party exchange and
refresh retain that ID; every bearer validates it through the authority's independent tenant scope and live
account/qualified-link checks. Cookies reject absent/revoked/expired/wrong-version sessions. The default absolute
lifetime is eight hours (`AspNetCoreIdentityOptions.SessionLifetime` and the corresponding shell setting,
one minute–thirty days); cookie renewal and refresh cannot extend it. User status transitions rotate the account
security stamp atomically, including whole-record saves, so disable→enable cannot resurrect prior credentials.
The optional [API](../../Api/README.md#link-and-session-administration) and Studio module provide bounded
session inspection and selected revocation, guarded link administration and redacted security notifications.
Link removal checks the exact revision and recovery before committing; removing/recreating a link cannot revive
earlier sessions or recovery evidence.

Native SQLite tests prove migrations, reopen, deliberately synchronized concurrent consumption, all bindings,
expiry, schema skew, scope checks, transaction rejection and bounded cleanup. Flow component tests also prove
real HTTP code exchange and preview across independent service providers sharing SQLite and owned key files.
Native session tests additionally prove protected reopen/shared keys, bounded exact-owner paging, revisioned
selected revocation, forced two-node races, write-failure retry, wrong-key/tamper/schema refusal, transaction
rejection and content-preserving incremental schema upgrade. Actual HTTP tests prove cookie/access/refresh
invalidation and unrelated-session survival. Separate-process restart, cross-node completion/replay, concurrent
revocation and link/catalog races pass on native SQLite (same machine) and PostgreSQL 16.15, in managed and
deployment-only profiles. See [runners](../../../../../../../tools/identity/ExternalAuthentication.Probe/README.md)
and [bounded evidence](../../../../../../../specs/1212-external-authentication/evidence.md) for configuration and limits.

Infrastructure failures cross the public artifact/session contracts as `AuthenticationStoreException`, with
operation/tenant/record identifiers and the original `InnerException`. Cancellation and schema-version refusal
retain their existing meaning. A wrapped failure does not imply rollback: an acknowledgement may have failed
after a mutation committed. No authentication result is issued on that failure, and a consumed artifact never
becomes reusable. Duplicate creation and stale CAS outcomes retain their explicit results.
