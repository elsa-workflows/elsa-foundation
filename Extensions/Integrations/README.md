# Elsa Integrations

Optional Elsa Integrations modules. Elsa 4 only.

A connector is one NuGet package. [#2726](https://github.com/elsa-workflows/elsa-foundation/issues/2726)
forbids a second activity catalog: no hand-authored `connector.json` for activity metadata, no
parallel secret store, no Elsa 3 shims. HTTP uses the foundation `http` connection and
`SendHttpRequest`.

## Layout

```
Extensions/Integrations/
  src/Sdk/                         Minimal non-packable STUB until #2726 / PR #2825
  src/Connectors/OAuth2/           Generic OAuth2 scheme (#2727)
  tests/Connectors/OAuth2/Tests/   Tests against a mock OAuth server
```

## SDK stub (#2726)

`Elsa.Integrations.Sdk` is **not packable** (`IsPackable=false`) until PR #2825 merges. It is a
marker assembly only (`IntegrationsSdkStub.IsStub`). The packable OAuth2 connector does not
reference it. When #2825 lands, delete `src/Sdk` and PackageReference the real SDK.

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
