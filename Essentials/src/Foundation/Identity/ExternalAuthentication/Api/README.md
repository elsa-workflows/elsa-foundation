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
rotate the ordinary security stamp. The [durable session authority](../Persistence/EntityFrameworkCore/README.md)
binds canonical cookies/access/refresh to one random session ID and live account/link state. Selected revocation
rejects that session's credentials without rotating the user's other sessions. Normal managed flow start/callback
requires the authority's durable-session capability. User disable/delete and account-version changes reject
already issued access/refresh as well as cookies; disable→enable cannot resurrect earlier sessions.
The administration operations below expose inspection, selected-session revocation and guarded link attachment/removal.
Link mutations coordinate current IAM permissions and recovery checks with the connection catalog when database management is enabled.
An explicit revision-authorized IAM login rebind rotates both affected account versions in the same transaction.
Their earlier cookies/access/refresh remain invalid even if the login later returns to its original owner.
Unrelated users retain their sessions; ordinary same-owner metadata updates preserve the account versions.

`ExternalAuthenticationHttpTests` drive actual owned Kestrel HTTP hosts and two actual HTTP OIDC fixtures with
native SQLite IAM/artifact/vendor-token stores and owned key files. They cover normal cookie/access/refresh/
logout, preview isolation, live discovery, ownership modes, CSRF/browser/purpose/replay, concurrent tabs and local
recovery. The separate [qualification runners](../../../../../../tools/identity/ExternalAuthentication.Probe/README.md)
exercise actual Keycloak, reference Studio/Workbench and separate backend processes on native SQLite and
PostgreSQL. Their [evidence and limits](../../../../../../specs/1212-external-authentication/evidence.md) are
recorded independently; unit/HTTP tests do not substitute for those product and deployment journeys.

## Connection management API

The same optional feature maps JSON operations below `/_elsa/identity/external-connections` (below the backend
path base). They use ordinary endpoint ownership/permission metadata. API endpoints [disable cookie redirects](https://learn.microsoft.com/en-us/dotnet/api/microsoft.aspnetcore.http.metadata.idisablecookieredirectmetadata?view=aspnetcore-10.0),
so denied API calls return 401/403. Handlers also recheck current canonical-user eligibility and saved permissions
before entering global catalog persistence; a stale cookie grant is insufficient. Custom session authorities
implement read-only `CanSignInAsync`, `SupportsDurableSessions`, live `ReadSessionAsync`/`ValidateSessionAsync`
and current-subject authorization explicitly; the defaults refuse unsupported capabilities.

| Method and suffix | Permission / result |
|---|---|
| GET root | `identity/providers:read`; catalog revision, source/effectiveness/shadowing, deployment policy, callbacks and current ownership capabilities |
| GET `/{id}` | Same read permission; source document plus local configuration readiness |
| POST `/assess` | `identity/providers:manage`; `ConnectionWrite`, prospective source/lifecycle effect, no catalog save |
| POST `/commands` | Same manage permission; the same command with an expected catalog revision, ordinary writer/CAS/last-path guards |
| POST `/test` | Same manage permission; saved connection record ID and expected revision, bounded discovery/JWKS only, observed time/material/staleness |
| GET `/lookups/users?skip=0&take=50&search=...` | Both provider manage and `identity/users:read`; bounded tenant-local identity labels/status, no credentials |
| GET `/users?skip=0&take=25&search=...` | `identity/users:read`; bounded tenant-local directory with ownership, independent current panel permissions, module availability and protocol-path metadata; provider access is not required |
| GET `/lookups/roles?skip=0&take=50` | Both provider manage and `identity/roles:read`; existing role IDs/names and current deployment/editor delegation eligibility |

All JSON posts require the paired antiforgery cookie and configured header (`RequestVerificationToken` by default), including technical
clients. Fetch it from `{protocolPath}/antiforgery`; the response supplies `requestToken`, `formFieldName` and `headerName`. The catalog returns the configured `protocolPath`, including
custom callback routing. Resolve this relative to the trusted backend base, retaining its path base. JSON bodies
are limited to 64 KiB; unknown members, inline credentials and recurring mapping fields are refused. Wire enums
use their numeric Core values. Lookup page size is 1–100; skip is nonnegative and an optional user search is at most
400 characters. Missing paging adapters return 503 rather than enumerating unbounded stores.

The feature contributes backend capability `elsa.api.identity.external-authentication` contract 1, with the
`external-connections` relation. The catalog also exposes current `canManageConnections` and installed unique
provisioning-policy names. The optional [Studio module](../../../../../../Extensions/Identity/studio/Elsa.Studio.Identity.ExternalAuthentication/README.md)
uses these metadata and permission boundaries rather than assuming that installation grants write access.

The catalog is bounded by the domain catalog limits. It projects only known OIDC settings and secret references;
unknown adapter/settings data and private recovery/credential bindings never enter responses. Deployment records
are read-only. `configurationSupported` distinguishes complete known configuration from a redacted unknown schema,
so a client cannot prepare an incomplete full override. `configurationEditable` describes a supported, unarchived database-owned configuration with database
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

Every managed catalog mutation captures the editor's current user and saved role definitions, the proposed
provisioning roles, and any selected recovery user's authority roots around the actual checks. Those opaque
revisions and the complete catalog CAS commit in one owned transaction. A changed authority snapshot returns
409 (`identity-authority-changed` before commit, or `revision-conflict` at commit); refresh and reassess rather
than replaying the old assessment. Coordination advances numeric root revisions while preserving account
security versions, role permissions and unrelated credentials.

The default EF coordinator requires IAM and provider configuration to use the same relational provider and
exact configured database target, with connections and transaction owned by the operation. Custom authorities
can implement the Core guard/capture/coordination contracts. Unsupported storage returns 503
`identity-coordination-unavailable`; the catalog reports `canManageConnections:false` and
`managementUnavailableReason:identity-coordination-unavailable`, and Studio explains the unavailable host
management. Deployment-only sign-in remains available without enabling database connection management.

A test targets a saved unarchived record, including disabled/shadowed drafts. It performs no admission, code exchange,
user/link/session/token write or recovery-proof recording. Its response contains only closed failure categories,
observed time/material and a stale flag if the catalog or record changed during the test. It has an overall
thirty-second cancellation bound in addition to protocol backchannel bounds. A test observation is returned to the
caller, not retained as scheduled health history. Interactive preview continues through the separate protected
administrator/browser-bound routes above; successful metadata testing alone never counts as recovery.

For an interactive browser form, request `{protocolPath}/antiforgery?purpose=preview`. This authenticates the
administrator's cookie explicitly and mints the token for that cookie identity; a bearer-minted token may not
validate when the browser posts its form without a bearer header. An authenticated transport subject must match
the cookie's complete typed subject. Different identities refuse even when both are administrators. Use the returned
`formFieldName` in the form. A consumed protected preview may redirect a closed `externalPreviewFailure` category
to its currently allowed return URL; unbound/invalid state and revoked return policy never provide a redirect.
Successful preview still returns the one-time `externalPreviewHandle` and `externalPreviewConnection`.

Owned real HTTP/native SQLite tests cover the complete draft-assess-save-test-enable-retire and explicit
override/archive/restore paths, current permission/delegation changes, source/revision/CSRF/body/role/unknown-field
refusals, safe projections and bounded lookups. Actual Studio journeys also exercise creation, test, preview,
enablement, full override promotion, disable, archive, restore and removal. The qualification evidence above
records browser/ownership/permission profiles separately from process/store modes.

## Link and session administration

These operations share the management base path and its JSON/antiforgery/body/paging protections. Each requires
`identity/users:read` in addition to the dedicated permission below, with current saved grants checked again by
the handler. Provider management and non-interactive credential management do not imply these grants.

| Method and suffix | Dedicated permission / behavior |
|---|---|
| GET `/users/{userId}/sessions?skip=0&take=50` | `identity/sessions:read`; bounded tenant/user metadata, active/inactive/expired/revoked state, current-session flag, revision and current revocation availability |
| POST `/users/{userId}/sessions/{sessionId}/revoke` | `identity/sessions:manage`; `expectedRevision`, durable CAS revocation of that session only, current ownership capability required |
| GET `/users/{userId}/links?skip=0&take=50` | `identity/links:read`; provider, exact issuer/subject, timestamps, revisions, `canRemove` and permitted attachment choices |
| POST `/users/{userId}/links/attach` | `identity/links:manage`; existing user only, explicit connection record and opaque subject with expected catalog/user revisions |
| POST `/users/{userId}/links/remove` | `identity/links:manage`; exact qualified provider/issuer/subject with expected link/user/catalog revisions; preserves a proved remaining user path and administrative recovery |

Session responses omit account security versions and upstream subjects/tokens. Revocation reports 409 for a stale
revision, 404 for another user or a missing session, and 503 with `revoked:false` for a storage failure. A confirmed
already-revoked session is idempotent. Retrying a failed write remains possible; unrelated sessions stay live.
Inspection refuses an unavailable durable authority or a replacement returning records outside the requested scope.

Attachment obtains the issuer from bounded server-side protocol validation, never a caller-supplied issuer. It
rechecks source material, current permissions, user revision, membership and delegation after metadata work,
then uses atomic IAM admission without creating a user or rebinding another owner. All existing user/membership
roles and direct permissions must remain within the editor's authority and configured delegation boundary.

Attachment captures the editor/target user and every referenced saved role definition around those checks through
`IExternalLinkAuthorizer`. `IExternalIdentityAdministrationStore` compares and advances the guarded IAM roots and
attaches the unchanged existing user in one owned serializable transaction. In database-managed mode,
`IConnectionCatalogStore.PrepareProviderWrite` contributes the unchanged full-catalog CAS to that same commit,
so a source edit cannot pass between checking the connection and storing the link. A stale snapshot/catalog
returns 409. No roles or direct permissions are assigned by administrative attachment.

Deployment-only hosts use the tenant IAM transaction and do not require provider-configuration persistence.
Database-managed hosts require a coordinated provider write from their catalog and the same owned database target.
Missing capabilities are read-only in inspection and return 503 on commands; there is no unguarded fallback.
The optional coordinator is owned by the selected IAM backend, so a later unowned replacement fails startup
validation. Custom authorities can supply the Core coordinator and a matching catalog bridge. Borrowed transactions,
pending caller changes, split targets and cross-tenant guards are refused.

A storage exception returns a redacted 503 `link-attachment-unavailable` envelope and a failure notification with
`storage-outcome-unavailable`; it does not return `attached:false` for an uncertain commit. Refresh current state
before retrying. Native commit-acknowledgement loss retains the typed unknown outcome and commits all participating
rows together. Existing saved roles are preserved; attachment does not authenticate the linked user or issue a session.

Unlink accepts `provider`, `issuer`, `subject`, `expectedLinkRevision`, `expectedUserRevision` and nullable
`expectedCatalogRevision`. Issuer and subject remain exact; issuer-less legacy links are not a fallback. It requires
the same current permission/ownership/delegation boundary and an unchanged guarded target. `canRemove` describes
that action availability, not a promise that the prospective recovery check will pass.

`IExternalLinkRecovery` proves a remaining path for the target and a global administrative path, excluding the removed
qualified key. Eligible local passwords count. External paths require retained, protected normal-session evidence
for the selected source/material, opaque credential binding, callback, account and exact link instance, plus current
protocol/readiness/admission checks. Global recovery additionally requires current `identity/providers:manage`.
A configured link or preview alone cannot count. The scan is bounded by `RecoveryScanLimit` users and at most
`min(100, RecoveryScanLimit)` recent retained sessions per user, under a thirty-second deadline. Evidence cannot be
older than `RecoveryEvidenceLifetime`; expired rows may already have been cleaned up, so fresh normal sign-in may
be required. Old records without provenance never acquire invented evidence through an upcaster.

An ended individual session may supply bounded historical credential evidence, while its actual cookie/access/refresh
remain ended. Removal preserves saved roles, account epochs, other links and unrelated sessions. The user, editor,
role and selected recovery guards commit with removal and, in database-managed mode, the unchanged catalog CAS.
Deployment-only mode uses IAM without provider persistence. Competing independent nodes cannot both remove the
remaining links. Unusable recovery returns 409 `final-login-path`; stale state returns 409; wrong owner/key returns 404.
A storage exception returns a redacted 503 `link-removal-unavailable` envelope without a false `removed:false` claim.
Refresh current links before retrying an uncertain operation.

Provider changes, admission/sign-in/preview outcomes, attachment/removal and session revocation emit `IAuthEventSink`
notifications when sinks are installed. Notifications use closed action/result categories, canonical actor/target
identifiers and an optional opaque Elsa session ID. They omit upstream subjects, display/profile fields, secrets,
tokens, protocol payloads and raw exceptions. A sink failure is logged by exception type and cannot undo a
committed action or turn it into a failed mutation. This adds no audit database; the host owns notification sinks.
