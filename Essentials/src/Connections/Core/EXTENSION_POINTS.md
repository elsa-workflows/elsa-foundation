# Connections Core Extension Points

The Connections core package defines the provider-neutral contracts for service connections: named,
tenant-owned instances of a connection type that hold non-secret settings plus secret-field
references. Hosts and connector packages replace or extend these seams; core stays free of cloud
vendor types.

## Vocabulary

"Connection" in code and UI is a **service connection** (glossary). It is not a flowchart edge, a
database connection string, or `RuntimeConnectionToken`.

## Override

- `IConnectionRepository`: persistence of connection definition and state. The default is in-memory;
  a durable package replaces it.
- `IConnectionManager`: lifecycle facade (create, update, test, revoke, delete).
- `IConnectionResolver`: point-of-use resolution. Tenant-scoped; owner/run-as is reserved and only
  `tenant` is valid in v1.
- `IConnectionCredentialSource` implementations, keyed by `Kind`: how a connection field's material
  is obtained. Built-ins are `secret` (`ISecretValueResolver`) and `named-credential`
  (`IAccessTokenSource`). Hosts add kinds without changing core.
- `IAccessTokenSource` + `IAccessTokenSourceRegistry`: named credentials declared under
  `Elsa:Credentials:<name>` with a pluggable `Kind`. Azure (or any other cloud) kinds live in
  extension packages, never in this assembly.
- `IConnectionGrantWriter`: compare-and-swap write of OAuth grant material on the expected secret
  version. The distributed lock is an optimization only.

## Extend

- `IConnectionTypeProvider`: a kind of external system (`http`, `google-drive`, …). Must declare
  supported schemes, non-secret settings, an allowed-hosts policy, and `TestAsync`.
- `IAuthenticationSchemeProvider`: an open auth-scheme plugin (`api-key`, `basic`, `bearer`,
  `oauth2`, …). Core never hard-codes a scheme list.
- `IHttpRequestAuthenticator`: applies a scheme to an outbound `HttpRequestMessage`. Refuses to
  attach material when the request host is not allowed.

## Safety Rules

- Public metadata models must not expose raw secret or token values.
- Runtime resolution returns secrets only through `ISecretValueResolver` or
  `IConnectionCredentialSource`, wrapped in `SensitiveValue`.
- Tokens injected into outbound requests are registered with `IRuntimeSecretMask`.
- `SensitiveValue` has no implicit string conversion; `ToString` returns `[redacted]`; JSON
  serialization throws.
