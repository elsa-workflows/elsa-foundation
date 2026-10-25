# Generic OAuth2 connector (#2727)

The Generic OAuth2 connector lives under [`Extensions/Integrations`](../../Extensions/Integrations/README.md).
It contributes one authentication scheme (`oauth2`, connections plan D3) for the foundation `http`
connection. It does not add a second HTTP activity and does not ship a `connector.json` activity
catalog ([#2726](https://github.com/elsa-workflows/elsa-foundation/issues/2726) contract rules).

## Acceptance criteria

1. Built against the foundation Connections model and `Elsa.Integrations.Sdk` v1. OAuth2 is one scheme.
2. Authorization-code + PKCE and client-credentials, with configurable auth/token/scopes URLs.
3. BYO client only. Client secret is a connection secret field or a named credential
   (`IAccessTokenSource` / `named-credential`).
4. Callback at `/_elsa/connections/oauth/callback`, built from `PublicBaseUrl`.
   Authorization-code flow starts at `POST /connections/{name}/oauth/authorize`.
5. Refresh on 401 or expiry writes the grant with compare-and-swap on the secret version; a lost
   race re-reads and uses the winner and never overwrites it. Grants require a writable store.
   A refresh that omits `refresh_token` keeps the existing one. A refresh that omits `expires_in`
   stores no expiry.
6. `SendHttpRequest` uses the foundation `http` connection through `IHttpConnectionApplicator`.
7. Access and refresh tokens resolve through `ISecretValueResolver` and are registered with
   `IRuntimeSecretMask` for logs, outputs and errors.
8. Harness tests against a mock OAuth server; live lab test pending.
9. Signing/scan stub until valence-works/elsa-control#802.

## Blockers

Hosted connections platform (valence-works/elsa-control#801) has not landed.
