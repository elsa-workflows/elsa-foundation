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
not turn off Elsa-owned role management. Recurring external group-to-role mapping is excluded; provisioning policies assign saved existing Elsa roles and later sign-ins use saved memberships.
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

## Connection management API

The same optional feature maps JSON operations below `/_elsa/identity/external-connections` (below the backend
path base). They use ordinary endpoint ownership/permission metadata. API endpoints [disable cookie redirects](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.metadata.idisablecookieredirectmetadata?view=aspnetcore-10.0),
so denied API calls return 401/403. Handlers also recheck current canonical-user eligibility and saved permissions
before entering global catalog persistence; a stale cookie grant is insufficient. Custom session authorities
implement read-only `CanSignInAsync` and current-subject authorization explicitly.

| Method and suffix | Permission / result |
|---|---|
| GET root | `identity/providers:read`; catalog revision, source/effectiveness/shadowing, deployment policy, callbacks and current ownership capabilities |
| GET `/{id}` | Same read permission; source document plus local configuration readiness |
| POST `/assess` | `identity/providers:manage`; `ConnectionWrite`, prospective source/lifecycle effect, no catalog save |
| POST `/commands` | Same manage permission; the same command with an expected catalog revision, ordinary writer/CAS/last-path guards |
| POST `/test` | Same manage permission; saved connection record ID and expected revision, bounded discovery/JWKS only, observed time/material/staleness |
| GET `/lookups/users?skip=0&take=50&search=...` | Both provider manage and `identity/users:read`; bounded tenant-local identity labels/status, no credentials |
| GET `/lookups/roles?skip=0&take=50` | Both provider manage and `identity/roles:read`; existing role IDs/names and current deployment/editor delegation eligibility |

All JSON posts require the paired antiforgery cookie and `RequestVerificationToken` header, including technical
clients. Fetch it from `{protocolPath}/antiforgery`; the catalog returns the configured `protocolPath`, including
custom callback routing. Resolve this relative to the trusted backend base, retaining its path base. JSON bodies
are limited to 64 KiB; unknown members, inline credentials and recurring mapping fields are refused. Wire enums
use their numeric Core values. Lookup page size is 1–100; skip is nonnegative and an optional user search is at most
400 characters. Missing paging adapters return 503 rather than enumerating unbounded stores.

The catalog is bounded by the domain catalog limits. It projects only known OIDC settings and secret references;
unknown adapter/settings data and private recovery/credential bindings never enter responses. Deployment records
are read-only. `configurationEditable` describes a supported, unarchived database-owned configuration with database
management enabled; the caller still needs manage permission. Archived/unsupported records retain source metadata
and explicit lifecycle operations. Database-disabled hosts refuse mutation. Existing Elsa roles are selected through
the provisioning policy; no group-mapping editor or synchronization capability is advertised. Ownership capabilities
remain those of Elsa's configured IAM authority.

Create saves a disabled draft. A same-key document does not shadow deployment automatically; explicit permitted
override/promotion is a separate command. `assess` reports the resulting effectiveness for confirmation. `commands`
reassesses and saves only against the supplied revision, so assessment is not a reservation. Enable/restore load the
saved admission policy and recheck its roles against current saved administrator grants; omitting/changing roles in
the command body cannot bypass delegation. Catalog conflicts and final-path refusal return 409; denied source/role
policy returns 403; invalid commands return 400 and missing database records return 404.

A test targets a saved unarchived record, including disabled/shadowed drafts. It performs no admission, code exchange,
user/link/session/token write or recovery-proof recording. Its response contains only closed failure categories,
observed time/material and a stale flag if the catalog or record changed during the test. It has an overall
thirty-second cancellation bound in addition to protocol backchannel bounds. A test observation is returned to the
caller, not retained as scheduled health history. Interactive preview continues through the separate protected
administrator/browser-bound routes above; successful metadata testing alone never counts as recovery.

Owned real HTTP/native SQLite tests cover the complete draft-assess-save-test-enable-retire and explicit
override/archive/restore paths, current permission/delegation changes, source/revision/CSRF/body/role/unknown-field
refusals, safe projections and bounded lookups. Studio/browser and deployment qualification remain in spec 1205.
