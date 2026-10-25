# Elsa Integrations

Optional Elsa Integrations modules. Elsa 4 only.

A connector is one NuGet package. [#2726](https://github.com/elsa-workflows/elsa-foundation/issues/2726)
forbids a second activity catalog: no hand-authored `connector.json` for activity metadata, no
parallel secret store, no Elsa 3 shims. HTTP uses the foundation `http` connection and
`SendHttpRequest`.

## Layout

```
Extensions/Integrations/
  src/Sdk/                         Elsa.Integrations.Sdk v1 (#2726 / #2825)
  src/Connectors/OAuth2/           Generic OAuth2 scheme (#2727)
  tests/Tests/                     SDK contract tests
  tests/Connectors/OAuth2/Tests/   Tests against a mock OAuth server
```

## SDK

`Elsa.Integrations.Sdk` is the packable connector activity base. `AddOAuth2Connector` calls
`AddElsaIntegrationsRuntime` so the named `Elsa.Activities.Http` client and unbound applicator
are registered when Connections is not composed.

## OAuth2 scheme

`oauth2` is one `IAuthenticationSchemeProvider` / `IHttpRequestAuthenticator` on the foundation
`http` connection. Tokens apply through `IHttpConnectionApplicator`. There is no second
`DelegatingHandler` on the named `Elsa.Activities.Http` client and no `IActiveConnectionAccessor`.

- Callback: reserved `GET /_elsa/connections/oauth/callback` via `IOAuthCallbackHandler`
- Begin: `POST /connections/{name}/oauth/authorize` via `IOAuthAuthorizeBeginHandler`
- Refresh: `IConnectionGrantWriter` compare-and-swap on the secret version; a lost race uses the winner
- Client secret: connection secret field or named credential (`IAccessTokenSource`)
- Access and refresh tokens: `IRuntimeSecretMask`
- Grants: `RequiresWritableStore`
