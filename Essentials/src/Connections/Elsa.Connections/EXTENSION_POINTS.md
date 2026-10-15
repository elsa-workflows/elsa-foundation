# Connections Extension Points

The `Elsa.Connections` package provides default services and shell feature registration for service
connections. It depends on `Elsa.Secrets`.

## Service Overrides

Use `services.AddConnections()` to register defaults, then replace these services as needed:

- `IConnectionRepository` for durable persistence. Phase 1 ships `InMemoryConnectionRepository` only:
  restarting the host drops connection definitions. Owned secrets (`connection:<name>:<field>`) stay
  in the encrypted secret store, so a published workflow that still names the connection faults until
  the definition is created again. A Groundwork repository on the Secrets provider matrix is deferred.
- `IConnectionManager` for lifecycle.
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
