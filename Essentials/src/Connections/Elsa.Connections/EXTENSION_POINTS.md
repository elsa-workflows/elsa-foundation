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
- `IConnectionManager` for lifecycle. A replacement must keep the compare-and-swap contract on
  `IConnectionRepository.SaveAsync`, and the exception contract in the `IConnectionManager` XML docs:
  `ArgumentException` (HTTP 400), `ConnectionConflictException` (409), `ConnectionUnavailableException`,
  `ConnectionStoreException` for a store failure before the save (500), and `ConnectionCredentialWriteException`
  for a failure after it (500). The default manager's write ordering and error classification are documented on
  `DefaultConnectionManager` (issue #2855). Known limits:
  - A custom `ISecretAuditSink` that throws after Secrets committed a write makes that write look failed: it is
    reported as not written (500), or as a false 409 when the failure reads as a concurrent change.
  - A secret with no active version (revoked or expired) is rotated without an expected version, and Secrets has
    no guard there, so concurrent updaters are last-writer-wins.
  - Secrets keeps the name of a deleted secret, so re-creating an inline credential under a deleted secret's name
    fails (409) until Secrets releases deleted names.
  - A cleanup failure after a conflicted rotation is logged and not returned, so the replaced secret stays Active
    and unreferenced.
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

The built-in `http` connection type supports `api-key` (header or query), `basic`, and `bearer`.
OAuth schemes are ordinary plugins on the same type; they are not a second HTTP activity.

## OAuth callback (phase 2 groundwork)

The API maps `GET /_elsa/connections/oauth/callback`. The redirect URI is
`{PublicBaseUrl}/_elsa/connections/oauth/callback`. `PublicBaseUrl` is required; the API does not
fall back to the request host. Token refresh writes the grant with compare-and-swap on the expected
secret version (`IConnectionGrantWriter`).
