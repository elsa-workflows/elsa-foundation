# Generic OAuth2 connector

Elsa Integrations OAuth2 **authentication scheme** for the Elsa Connector Builder.
Tracked by [#2727](https://github.com/elsa-workflows/elsa-foundation/issues/2727).

This package is not a second HTTP activity and does not carry a `connector.json` activity
catalog. [#2726](https://github.com/elsa-workflows/elsa-foundation/issues/2726) forbids a
duplicate manifest: activity metadata stays on the foundation attributes. HTTP is
`SendHttpRequest` through the foundation `http` connection (connections plan D1).

## Scheme (D3)

`OAuth2AuthenticationScheme` (`oauth2`) is one plug-in authentication scheme:

- `authorization_code` with PKCE S256
- `client_credentials`
- Configurable `authorizationUrl`, `tokenUrl`, `scopesUrl`, and `scopes`
- `clientSource`: `valence` (Valence-registered app) or `byo` (bring-your-own client)

Grants require a writable secret store (`RequiresWritableStore`). A read-only store is refused.

## Callback (D2)

The provider redirects to `/_elsa/connections/oauth/callback`, built from `PublicBaseUrl`.
This package does not host that route (activities/scheme only). The stub harness and, later,
the connections platform (#801) serve it.

## Refresh (AC 5)

On 401 or expiry, the new grant is written with **compare-and-swap on the secret version**.
A lock is only an optimization.

## Secrets (AC 7)

Tokens are resolved through foundation `ISecretValueResolver` and registered with
`IRuntimeSecretMask` (spec 188 FR-012). They never appear in workflow JSON or activity results.
The named `Elsa.Activities.Http` client stays header-free in logs (spec 188 FR-019).

## Blockers

Foundation connections phase 1 (the `http` connection that replaces `SendHttpRequest.Authorization`)
has no tracking issue yet. This package binds the scheme to that connection through a harness stub.
[#2726](https://github.com/elsa-workflows/elsa-foundation/issues/2726) has no branch or PR; the
SDK here is a marked stub that reuses foundation contracts instead of shims.

## Tests

`Elsa.Integrations.Connectors.OAuth2.Tests` drives `SendHttpRequest` against an in-process mock
OAuth server for both grants, Valence and BYO, CAS refresh, writable-store refusal, callback path,
and secret isolation.

A live-provider run on the lab shard is pending. Signing/scan is a stub until
[valence-works/elsa-control#802](https://github.com/valence-works/elsa-control/issues/802).
