# Generic OAuth2 connector (#2727)

The Generic OAuth2 connector lives under [`Extensions/Integrations`](../../Extensions/Integrations/README.md).
It contributes one authentication scheme (`oauth2`, connections plan D3) for the foundation `http`
connection. It does not add a second HTTP activity and does not ship a `connector.json` activity
catalog ([#2726](https://github.com/elsa-workflows/elsa-foundation/issues/2726) contract rules).

## Acceptance criteria (13 Oct rewrite)

1. Built against a non-packable, internal SDK stub; OAuth2 is one scheme. No `Elsa.Integrations.Sdk` package is produced until #2726.
2. Authorization-code + PKCE and client-credentials, with configurable auth/token/scopes URLs.
3. Valence-registered app and BYO client.
4. Callback at `/_elsa/connections/oauth/callback`, built from `PublicBaseUrl`.
5. Refresh on 401 or expiry writes the grant with compare-and-swap on the secret version; grants
   require a writable store.
6. `SendHttpRequest` uses the connection through a stub of the foundation `http` connection (D1).
7. Tokens resolve through `ISecretValueResolver` and are registered with `IRuntimeSecretMask`.
8. Harness tests against a mock OAuth server; live lab test pending.
9. Signing/scan stub until valence-works/elsa-control#802.

## Blockers

Foundation connections phase 1 (D1) has no tracking issue yet (Elsa 4 Lead files it). The SDK (#2726)
and hosted connections platform (valence-works/elsa-control#801) have not landed. This tree uses a
non-packable, internal SDK stub that reuses foundation contracts instead of shims.
`IActiveConnectionAccessor` is removed in the same PR that lands D1 and `[ActivityConnection]`.
