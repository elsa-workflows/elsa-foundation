# Managed authentication HTTP protocol

Optional `FoundationIdentityExternalAuthenticationApi` adds backend-owned normal authentication and
administrator-only preview endpoints. Canonical identity and provisioning semantics live in the
[managed feature](../Elsa.Foundation.Identity.ExternalAuthentication/README.md); this API does not import IdP roles.

Compose the managed connection feature, a protocol such as `FoundationIdentityExternalOidc`, a durable artifact
authority such as `FoundationIdentityExternalAuthenticationEntityFrameworkCore`, and an
`ICanonicalUserSignInService` authority. The concrete Foundation ASP.NET Core Identity substrate registers that
service and uses its ordinary framework principal factory, security stamp and `Elsa.Identity.Cookie` scheme.
The external adapter refuses an account requiring a local second-factor step that this protocol cannot complete;
it does not treat an upstream sign-in as completing that step.
The host retains its normal routing/authentication/authorization middleware, module migration lifecycle and
Data Protection key configuration. Programmatic registration is `AddFoundationExternalAuthenticationApi()`;
the web-shell feature maps endpoints through the ordinary endpoint authoring/security conventions.

Configure `ExternalAuthenticationOptions.PublicBaseUrl` to the trusted public backend URL, including path base
or shell prefix. Never derive it from a request's Host header. Routes below sit under `CallbackPath` (default
`/identity/external`) below that base. `ExternalAuthenticationApiOptions.LoginPath` defaults to
`/_elsa/identity/login`, the existing themed backend chooser. Separate Studio origins must be explicitly allowed
in the host's normal local-login and managed-flow return policies and credentialed CORS configuration.

| Method and suffix | Behavior |
|---|---|
| `GET /antiforgery` | Returns `requestToken` paired with an HttpOnly antiforgery cookie; no-store. |
| `GET /signin/{key}` | Opens the backend chooser. Preferred methods never bypass it. |
| `POST /signin/{key}` | Antiforgery-protected form start (`returnUrl`); creates one protected browser correlation and redirects upstream. |
| `GET /signin/{key}/callback` | Consumes protected durable state, validates provider evidence, admits the canonical user and issues the ordinary Elsa cookie. |
| `POST /preview/{key}` | Requires provider-management permission, the initiating canonical administrator's interactive cookie and antiforgery. Form adds `connectionRecordId` for the selected unarchived draft. |
| `GET /preview/{key}/callback` | Reauthenticates the existing administrator cookie and consumes preview state. It never issues or replaces a normal session. |
| `GET /preview/{key}/results/{handle}` | Requires management permission, the same administrator/browser and a protected result correlation; consumes the bounded safe result once. |

Purposes, tenant and browser proof come from the server route/configuration/protected cookie, never form fields.
Normal and preview purposes have separate paths, cookie protection purposes and durable artifacts. Each flow
gets a distinct cookie name based on its opaque state handle, so simultaneous tabs do not overwrite one another.
Correlation cookies are HttpOnly, SameSite=Lax, path-bound, expiring and Secure for an HTTPS public backend.
Loopback HTTP requires Development. All protocol responses are no-store and suppress Referer disclosure.
Duplicate callback state/code/issuer/error parameters, oversized form bodies and unapproved return destinations
are refused; raw upstream errors never enter responses.

Preview returns to the approved destination with `externalPreviewHandle` and `externalPreviewConnection`.
Those opaque values alone cannot retrieve a result: the same administrator cookie, permission and protected
browser correlation are required. Bearer-only administration cannot start this interactive flow. Result reads
report whether the observed connection material changed. They neither provision users/links nor mint normal
cookies or first-party tokens. This remains true for an enabled, already linked user and for a disabled,
non-effective stored draft.

The live discovery contribution uses stable IDs `external:{logical-key}`. Effective/enable/source changes are
visible without restart, while missing protocol/store/session support is not advertised as a usable method.
Capabilities reflect Elsa's configured IAM authority and effective ownership mode; upstream authentication does
not turn off Elsa-owned role management. Recurring group mapping remains undecided and is not advertised.
The existing login renderer includes local credentials and provider forms/links in its host theme/custom renderer,
escapes display names, and keeps local recovery available during an optional catalog outage. Disabling the
local password method hides/refuses that method while the same cookie substrate can still issue external sessions.

The normal Foundation token exchange now returns its existing refresh token alongside access token/expiry;
credential exchange/refresh responses are explicitly no-store/no-cache.
Logout uses the signed interactive subject, including after its managed connection is retired; a first-party
bearer can clear a matching canonical cookie but cannot select another user's cookie. Shared-scheme invalidators
rotate the ordinary security stamp. Durable selected-session records and access/refresh status enforcement for
user disable/delete remain T023/T024 work; cookie rejection alone is not that proof.

`ExternalAuthenticationHttpTests` drive actual owned Kestrel HTTP hosts and two actual HTTP OIDC fixtures with
native SQLite IAM/artifact/vendor-token stores and owned key files. They cover normal cookie/access/refresh/
logout, preview isolation, live discovery, ownership modes, CSRF/browser/purpose/replay, concurrent tabs and local
recovery. Browser rendering, external product interoperability, separate-process/two-node/server-database,
Studio and resulting-main qualification remain required by spec 1205. These tests are not substituted for them.
