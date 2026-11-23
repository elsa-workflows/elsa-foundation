# Managed OpenID Connect

Optional `FoundationIdentityExternalOidc` protocol contribution for the
[managed connection feature](../Elsa.Foundation.Identity.ExternalAuthentication/README.md).
It does not modify the existing direct-bearer Identity.Oidc feature or register per-connection ASP.NET schemes.
The broker/session/API/Studio composition and deployment qualification remain in
[spec 1212](../../../../../../specs/1212-external-authentication/spec.md); installing this protocol alone does not
expose a sign-in endpoint or prove a complete deployment.

## Connection settings

`authority` is the exact configured issuer URL, including its trailing-slash choice; use a fixed tenant issuer,
not a multi-tenant issuer template. `clientId` is the registered client ID. Optional `scopes` defaults to
`openid profile email` and must contain `openid`. `clientAuthentication` supports `client_secret_basic`,
`client_secret_post`, or an explicitly public `none` client. Confidential modes use the `clientSecret` key in
`ConnectionRecord.Secrets`, containing an Elsa Secrets text reference, never an inline value. Without an explicit
method, a secret reference selects basic authentication; otherwise the public method is selected.
`signingAlgorithm` defaults to `RS256`; configured alternatives are the supported RS/PS/ES 256/384/512 algorithms.
Symmetric signatures and unsigned tokens are refused. Unknown settings and secret slots are invalid.

Metadata must advertise authorization code flow, the chosen signing/client-authentication mode, and S256 when
it advertises PKCE methods. An omitted client-authentication list means basic, as in OIDC discovery. The module
uses HTTPS with normal certificate validation. Discovered authorization/token/JWKS endpoints must share the
configured authority's origin; redirects are not followed. Authorization/token endpoints cannot add query
parameters that could conflict with broker-owned protocol fields. JWKS query parameters are permitted.
These constraints intentionally do not offer arbitrary endpoint overrides or relaxed issuer validation.

`ManagedOidcOptions.AllowLoopbackHttpForDevelopment` permits loopback HTTP only with a positively identified
Development host. Both startup/shell activation and point-of-use checks refuse it in Production or an unknown
environment. `BackchannelTimeout` defaults to ten seconds, is positive and at most thirty seconds, and bounds
the entire operation. Each JSON response is bounded to 128 KiB and depth 32. Named-client headers are redacted;
no upstream error description, authorization code, client secret or token is returned in diagnostics.

## Protocol boundary

The named `IExternalAuthenticationProtocol` supplies local checks, bounded metadata/key tests, challenge
creation and code redemption. Inputs are server-only: a broker must generate high-entropy state/nonce/verifier,
use a distinct trusted callback per connection/purpose, persist protected browser/purpose/material-bound state,
and atomically consume that state before redemption. A protocol contribution is not a flow/session store.

Challenges use authorization code with S256 PKCE and a nonce. Redemption re-resolves the secret reference and
compares a server-only generation/value fingerprint before sending the code. Only that fingerprint belongs in
protected flow state, never the resolved value. Resolution uses an owned host-tenant scope and does not rebind
the caller. The configured issuer and any authorization-response issuer are checked before code exchange;
advertised response-issuer support makes its response parameter required.

ID tokens require a valid public-key signature, exact issuer/client audience, lifetime, nonce, subject and
issued-at evidence. Additional untrusted audiences, conflicting authorized-party values, access-token types,
duplicate JWT members, critical extensions and malformed claims refuse. IdentityModel's OIDC validator also
checks a supplied access-token hash. An unknown signing key triggers one fresh read of the same trusted JWKS
endpoint without a second code exchange. Validated identity/claims are returned only to server admission;
upstream access, refresh and encoded ID tokens are discarded.

Validation follows [OIDC Core ID-token validation](https://openid.net/specs/openid-connect-core-1_0.html#IDTokenValidation),
[OAuth security guidance on issuer mix-up](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.4.2),
and [PKCE](https://www.rfc-editor.org/rfc/rfc7636.html#section-4).

The owned HTTP fixture verifies real client authentication, single-use PKCE codes and cryptographically signed
tokens. It is component protocol evidence; it does not substitute for Elsa callback/cookie/browser, restart,
two-node or external-product acceptance witnesses.
