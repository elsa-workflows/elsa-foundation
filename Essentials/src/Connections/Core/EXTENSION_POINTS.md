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
  [EF persistence](../Persistence/EntityFrameworkCore/README.md) replaces it. `GetCapabilitiesAsync`
  returns the configured metadata provider and its durability; existing contributions default to
  unspecified/non-durable until they explicitly supply `ConnectionRepositoryCapabilities`.
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
- `IOAuthCallbackHandler`: claims the reserved `/_elsa/connections/oauth/callback` route.
- `IOAuthAuthorizeBeginHandler`: starts an interactive authorization flow for
  `POST /connections/{name}/oauth/authorize`. The first handler that returns a URL wins.

## Safety Rules

- Public metadata models must not expose raw secret or token values.
- Runtime resolution returns secrets only through `ISecretValueResolver` or
  `IConnectionCredentialSource`, wrapped in `SensitiveValue`.
- The workflow connection adapter registers injected token values with
  [`IRuntimeSecretMask`](../../Workflows/Runtime/Core/Contracts/IRuntimeSecretMask.cs) for activity fault
  handling. That mask is not a general logger, trace, console, output or persisted-state scrubber.
  Non-workflow consumers such as ETL must enforce their own diagnostic boundary; using Connections
  does not require coupling their runtime to workflow fault handling.
- `SensitiveValue` has no implicit string conversion; `ToString` returns `[redacted]`; JSON
  serialization throws.
