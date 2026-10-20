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
  resolves unchanged, and one that finds the secret rotated after its save gets
  `ConnectionConflictException` rather than overwriting the newer value.
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
