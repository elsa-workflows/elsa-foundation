# Connections Extension Points

The `Elsa.Connections` package provides default services and shell feature registration for service
connections. It depends on `Elsa.Secrets`.

## Service Overrides

Use `services.AddConnections()` to register defaults, then replace these services as needed:

- `IConnectionRepository` for persistence. The default `InMemoryConnectionRepository` drops metadata
  on restart. Opt into [EF persistence](../Persistence/EntityFrameworkCore/README.md) to retain it;
  choose durable Secrets storage separately for owned credentials (`connection:<name>:<field>`).
  `GetCapabilitiesAsync` identifies the actual configured metadata profile; unspecified contributions
  do not claim durability. A replacement must keep
  the compare-and-swap contract on `SaveAsync`: it saves only when the stored `State.Version` equals
  the document's and then increments it; otherwise, including after a delete, it throws
  `ConnectionConflictException` and writes nothing. Its expected-version `DeleteAsync` deletes only while
  the stored version equals the expected one, atomically. Derive the replacement's tests from
  `ConnectionRepositoryContractTests` in `Essentials/tests/Connections/Tests/`.
- `IConnectionManager` for lifecycle. A replacement must keep the compare-and-swap contract on
  `IConnectionRepository.SaveAsync`, and the exception contract in the `IConnectionManager` XML docs:
  `ArgumentException` (HTTP 400), `ConnectionConflictException` (409), `ConnectionUnavailableException`,
  `ConnectionStoreException` for a store failure before the save is confirmed (500), and
  `ConnectionCredentialWriteException` for a failure after it (500). The default manager's write ordering and
  error classification are documented on `DefaultConnectionManager`. Known limits:
  - A replaced secret that cannot be deleted after a committed save is reported as a 500 although the update was
    saved; after a conflicted rotation that cleanup failure is logged and not returned, and the replaced secret stays
    Active and unreferenced (#2855 item 2.2).
  - `DeleteAsync` deletes the owned secrets before the connection row, without a compare-and-swap (#2855 item 2.4).
  - A create's rollback deletes its row only at the version it added it at, so a connection saved since is kept.
    A row that another request deleted and a second create re-added, still unsaved, is at that same version and
    can be deleted; that second create's save then fails with 409 and rolls itself back (#2855 item 2.1).
  - A create's or update's rollback keeps an owned secret a stored connection still references, but that check only
    protects a commit the next read can see. When a durable store fails with the commit's outcome unknown and the
    commit is not yet visible, the rollback can delete a secret the committed row uses, so the connection fails to
    resolve that credential. Retrying the create or update with the inline value writes the secret again. The
    in-memory repository commits visibly or not at all; adding durable metadata does not resolve this limit.
  - A custom `ISecretAuditSink` that throws after Secrets committed a write makes that write look failed: it is
    reported as not written (500), or as a false 409 when the failure reads as a concurrent change (#3153).
  - A secret with no active version (revoked or expired) is rotated without an expected version, and Secrets has
    no guard there, so concurrent updaters are last-writer-wins (#3153).
  - A credential version that expires between the update's read and its rotation is reported as a 409 although no
    other request changed it; retrying the update succeeds (#3153).
  - `Revoke`, `Delete`, `Test`, `Find` and `List` do not wrap store failures (#3139).
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

The built-in `http` connection type supports `api-key` (header or query), `basic`, `bearer`, and
`oauth2`. The `oauth2` scheme is provided by the Integrations OAuth2 connector (#2728); creating an
`http` connection with that scheme requires the connector's `IAuthenticationSchemeProvider` and
`IHttpRequestAuthenticator`. OAuth is not a second HTTP activity.

## OAuth callback (phase 2 groundwork)

The API maps `GET /_elsa/connections/oauth/callback` and
`POST /connections/{name}/oauth/authorize`. The redirect URI is
`{PublicBaseUrl}/_elsa/connections/oauth/callback`. `PublicBaseUrl` is required; the API does not
fall back to the request host. Token refresh writes the grant with compare-and-swap on the expected
secret version (`IConnectionGrantWriter`). `HttpConnectionApplication`,
`HttpConnectionRequestOptionKeys` and `HttpConnectionHostGuard` stay internal; authenticators use
`HttpConnectionCredentials`.
