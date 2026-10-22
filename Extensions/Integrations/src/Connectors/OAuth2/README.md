# Generic OAuth2 connector

Elsa Integrations OAuth2 **authentication scheme** for the Elsa Connector Builder.
Tracked by [#2727](https://github.com/elsa-workflows/elsa-foundation/issues/2727).

This package is not a second HTTP activity and does not carry a `connector.json` activity
catalog. HTTP is `SendHttpRequest` through the foundation `http` connection. The scheme
registers as `IAuthenticationSchemeProvider` and `IHttpRequestAuthenticator`.

## Scheme (D3)

`oauth2` is one plug-in authentication scheme:

- `authorization_code` with PKCE S256
- `client_credentials`
- Configurable `authorizationUrl`, `tokenUrl`, `scopesUrl`, and `scopes`
- BYO client id (setting) and client secret (connection secret field or named credential)

Grants require a writable secret store (`RequiresWritableStore`).

## Callback (D2)

The provider redirects to `/_elsa/connections/oauth/callback`, built from `PublicBaseUrl`.
This package claims that reserved route through `IOAuthCallbackHandler`.
`POST /connections/{name}/oauth/authorize` starts the authorization-code flow through
`IOAuthAuthorizeBeginHandler`.

## Refresh (AC 5)

On 401 or expiry, the new grant is written with **compare-and-swap on the secret version**
(`IConnectionGrantWriter`). Concurrent refreshes rely on that compare-and-swap.

## Secrets (AC 7)

Access and refresh tokens are resolved through foundation `ISecretValueResolver` and
registered with `IRuntimeSecretMask` so they are replaced in logs, outputs and errors.

## SDK

[#2726](https://github.com/elsa-workflows/elsa-foundation/issues/2726) / PR #2825 is the real
SDK. This package does not reference the local stub, so no `Elsa.Integrations.Sdk` package is
produced from this tree.

## Tests

`Elsa.Integrations.Connectors.OAuth2.Tests` drives `SendHttpRequest` against an in-process mock
OAuth server. A live-provider run on the lab shard is pending. Signing/scan is a stub until
[valence-works/elsa-control#802](https://github.com/valence-works/elsa-control/issues/802).
