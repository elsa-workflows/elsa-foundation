# Connections Extension Points

The `Elsa.Connections` package provides default services and shell feature registration for service
connections. It depends on `Elsa.Secrets`.

## Service Overrides

Use `services.AddConnections()` to register defaults, then replace these services as needed:

- `IConnectionRepository` for durable persistence. Phase 1 ships `InMemoryConnectionRepository` only:
  restarting the host drops connection definitions. Owned secrets (`connection:<name>:<field>`) stay
  in the encrypted secret store, so a published workflow that still names the connection faults until
  the definition is created again. An EF Core repository is tracked in #2854. A replacement must keep
  the compare-and-swap contract on `SaveAsync`: it saves only when the stored `State.Version` equals
  the document's and then increments it; otherwise, including after a delete, it throws
  `ConnectionConflictException` and writes nothing. Derive the replacement's tests from
  `ConnectionRepositoryContractTests` in `Essentials/tests/Connections/Tests/`.
- `IConnectionManager` for lifecycle. The default manager writes a new value for an owned secret the
  stored connection already resolves only after the update's `SaveAsync` wins, expecting the secret
  version it read first. A request that loses the save therefore leaves what the stored connection
  resolves unchanged, and one that finds an active secret rotated after its save gets
  `ConnectionConflictException` (HTTP 409) rather than overwriting the newer value. The expected
  version is read before the save, so a rotation between that read and the save also yields 409,
  although the save then wins; so does a secret soft-deleted, or created under the same name, by a
  concurrent writer. For the soft-deleted case the 409 message says so and that a plain retry will not
  work: retrying with an inline value fails with `ConnectionCredentialWriteException` (HTTP 500) until
  Secrets releases the names of deleted secrets (a separate Secrets change), so until then the field must
  be re-pointed with `ExistingSecretName`. The check compares the secret's current version, so a secret with no active
  version (revoked or expired) is rotated without an expected version; if Secrets then reports a
  concurrent writer as a plain `InvalidOperationException` and the secret's version, status or
  update time differs from what the manager read, that too is a 409. Every changed field is
  attempted; the exception names the fields whose values were not written, and the update's other
  changes stay saved. When a field also fails for another reason, that failure is not hidden behind
  a 409: the update throws `ConnectionCredentialWriteException` (HTTP 500 from the API, with only the
  connection and field names in the message), carrying the failed field names and the store's
  failure as its inner exception (an `AggregateException` when several failed, each also logged).
  Store exceptions after the save do not cross `IConnectionManager` unwrapped. Secrets the save
  stopped referencing are deleted even then. After a successful rotation, a secret that cannot be
  deleted still surfaces to the caller as `ConnectionCredentialWriteException` naming the field: the
  update committed, and the secret stays Active and unreferenced (issue #2855 item 2.2 remains
  open). Only when the rotation also failed is the cleanup failure logged and swallowed, so the
  rotation failure is the one reported. A request that loses the save does not roll back a new
  `connection:<name>:<field>` secret it created beforehand for a field moving to inline storage:
  Secrets deletes softly and keeps the name, so removing it would block that field for good. The
  secret stays Active and unreferenced, and a retried inline write for that field is rejected as a
  managed-name conflict.
  Known limits of this write path:
  - A custom `ISecretAuditSink` that throws after Secrets committed a write makes that write look
    failed: it is reported as not written (HTTP 500), or as a false 409 when the failure reads as a
    concurrent change.
  - A secret version that expires between the manager's read and the rotation gives a 409 that heals
    on retry, because the retry reads the new state.
  - A secret with no active version (revoked or expired) is rotated without an expected version, and
    Secrets has no guard there, so concurrent updaters are last-writer-wins.
  - A cleanup failure after a conflicted rotation is logged and not returned: the 409 is the one the
    caller sees, and the replaced secret stays Active and unreferenced.
  - A store failure before the save throws `ConnectionStoreException` (HTTP 500, no store detail in
    the body) with nothing confirmed saved, so the request is safe to retry. `Revoke`, `Delete`,
    `Find` and `List` do not wrap store failures.
- `IConnectionResolver` for point-of-use resolution.
- `IConnectionGrantWriter` for compare-and-swap grant writes (expected secret version).
- `IConnectionPublishTenantAccessor` — owned by `ConnectionsDesign` (principal tenant, then persistence scope).
- `IAccessTokenSource` implementations, keyed by `Kind`, for named credentials
  (`Elsa:Credentials:<name>`). Cloud-vendor kinds belong in an extension package.

## Extend

- `IConnectionTypeProvider` for a new connection type.
- `IAuthenticationSchemeProvider` for a new auth scheme (OAuth2 plugs in here; see #2728).
- `IHttpRequestAuthenticator` for applying a scheme to `HttpRequestMessage`.
- `IConnectionCredentialSource` for a new credential source kind.

## HTTP type (phase 1)

The built-in `http` connection type supports `api-key` (header or query), `basic`, and `bearer`.
OAuth schemes are ordinary plugins on the same type; they are not a second HTTP activity.

## OAuth callback (phase 2 groundwork)

The API maps `GET /_elsa/connections/oauth/callback`. The redirect URI is
`{PublicBaseUrl}/_elsa/connections/oauth/callback`. `PublicBaseUrl` is required; the API does not
fall back to the request host. Token refresh writes the grant with compare-and-swap on the expected
secret version (`IConnectionGrantWriter`).
