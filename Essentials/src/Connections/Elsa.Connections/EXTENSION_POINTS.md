# Connections Extension Points

The `Elsa.Connections` package provides default services and shell feature registration for service
connections. It depends on `Elsa.Secrets`.

## Service Overrides

Use `services.AddConnections()` to register defaults, then replace these services as needed:

- `IConnectionRepository` for durable persistence (in-memory is the default).
- `IConnectionManager` for lifecycle.
- `IConnectionResolver` for point-of-use resolution.
- `IConnectionGrantWriter` for compare-and-swap grant writes.
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
`{PublicBaseUrl}/_elsa/connections/oauth/callback`. Token refresh writes the grant with
compare-and-swap on the expected secret version (`IConnectionGrantWriter`).
