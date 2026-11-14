# External authentication protocol and connection API

This optional package connects the managed flow service to the backend-owned login chooser and canonical
ASP.NET Core Identity cookie/session authority. Register `AddFoundationExternalAuthenticationApi` and map
`MapExternalAuthenticationApi`, or enable `FoundationIdentityExternalAuthenticationApi` in the composed host.
Adding the package or a solution entry does not enable it.

Compose the managed connection module, a selected protocol adapter, a durable artifact/session store and the
canonical Identity adapter. The backend's trusted `PublicBaseUrl`, including its path base, determines callback
addresses; incoming Host headers do not choose them. Share the protected-key authority across participating nodes.

## Routes and browser binding

Routes below use `ExternalAuthenticationOptions.CallbackPath`, `/identity/external` by default, below the trusted
backend base. Each operation declares its API owner and public-protocol or permission disposition.

| Method and suffix | Result |
|---|---|
| `GET /antiforgery` | Supplies a paired browser token; preview additionally requires the administrator cookie. |
| `GET /signin/{key}` | Opens the normal backend chooser without starting provider authentication. |
| `POST /signin/{key}` | Validates antiforgery, creates protected one-time flow state and redirects to the provider. |
| `GET /signin/{key}/callback` | Consumes bound state, verifies provider evidence, admits the saved Elsa user and issues the canonical session. |
| `POST /preview/{key}` | Starts assessment for the existing administrator; requires provider-management permission. |
| `GET /preview/{key}/callback` | Validates that administrator's cookie and correlation; creates no new authenticated user or normal session. |
| `GET /preview/{key}/results/{handle}` | Consumes the protected preview result for that browser and administrator, with provider-management permission. |

The chooser uses the ordinary Identity login page, `/_elsa/identity/login` by default. It offers enabled effective
external connections alongside the permitted local-password method. Preferred providers do not bypass choice.
Only an antiforgery-protected POST starts authentication. A failed optional catalog read retains local recovery.

Correlation cookies are protected, HttpOnly, SameSite Lax and scoped to the backend callback path. Production
uses HTTPS; development loopback transport requires the separately installed adapter's explicit development
policy. Callback and return destinations are checked again at completion. Wrong browser, purpose, administrator,
source/material, issuer or credential generation cannot supply an admitted identity. Bound state is single-use.
Responses suppress caching and sensitive upstream/sink errors; successful preview is not recovery evidence.

## Canonical authority

External names, groups and role claims grant no Elsa permissions. Normal admission uses an explicit integrator
provisioning policy and existing saved Elsa roles. Returning linked users retain their saved memberships.
The canonical adapter rechecks the user, membership, framework eligibility and exact qualified link before
creating its ordinary cookie and protected durable-session record. The issued session's identifier is a
server-owned binding, excluded from client JSON models.

Local password sign-in uses the same durable authority when that optional store is installed. Cookie, access
and refresh validation re-read current session/account/link authority. Revoked, expired, missing, rebound or
stale bindings are refused; an older readable payload cannot manufacture a link revision. Access/refresh
lifetimes are capped by the absolute session expiry. Removing the authority does not convert a session-bound
credential into a legacy credential. Base token-only compositions keep their existing scoped-key behavior.

The generic Foundation Identity token/refresh/logout endpoints preserve persisted-subject isolation and user
eligibility checks. Logout revokes before invalidating the cookie authority; a revocation failure preserves a
retryable session and returns a failure. A retired managed provider's own signed session can still sign out.

## Connection administration

Capability `elsa.api.identity.external-authentication` version1 advertises `external-connections` at
`/_elsa/identity/external-connections`, below the backend base. Its declaration and routes are installed together.

| Method and suffix | Required permission and result |
|---|---|
| `GET /` and `GET /{id}` | Provider read; source-aware list/detail with supported settings and secret references. |
| `POST /assess` | Provider manage and antiforgery; evaluate a proposed command without saving. |
| `POST /commands` | Provider manage and antiforgery; apply the guarded, revision-bound lifecycle command. |
| `POST /test` | Provider manage and antiforgery; bounded saved-configuration protocol test without authenticating a user. |
| `GET /lookups/users` | User read plus provider manage; bounded tenant user lookup. |
| `GET /lookups/roles` | Role read plus provider manage; bounded saved-role lookup with current delegation eligibility. |

Endpoint metadata supplies API denials rather than cookie redirects. Handlers independently re-read current
canonical eligibility and saved permissions. Mutation requests carry the current catalog revision and respect
source ownership, deployment policy and the existing role-permission delegation boundary. Enable/restore use
the saved policy, so omitting its roles from the submitted body cannot bypass delegation checks.

The catalog reports read-only status when the selected store cannot coordinate IAM revision guards. Assessment
and save use the same guarded writer. Retiring an effective path rechecks protected administrative recovery or
eligible local recovery, including current account, membership, role, exact link and issued-session authority;
configuration validity and successful preview are insufficient. Stale or uncoordinated writes do not save a
partial change. Ordinary same-key drafts do not shadow deployment; only an explicitly permitted full override
does. Disabled overrides shadow, archive/remove reveal the baseline, and restore reinstates the saved document.

Test results contain readiness/failure and stale-material status. They do not serialize private issuer/credential
proof, resolve secret values into responses or establish recovery evidence. Unknown adapters/settings remain
visible as source metadata without an arbitrary settings/secret dump. Deployment-only catalog reads continue
to avoid the optional provider-configuration database.

## Source-preparation boundary

This prefix contains protocol/chooser/preview, canonical session integration and connection administration.
User, link and selected-session administration routes follow in their owning later slice. It does
not enable Workbench or provide a Studio administration module.

Controlled-provider HTTP regression source covers the composed contracts, including prior protected sessions
with missing/stale link revisions. No build, test, provider, browser or database execution has qualified this
prepared prefix. Current-source real-provider/deployment, package, composed Studio and independent visual
acceptance remain required by [spec1212](../../../../../../specs/1212-external-authentication/spec.md).
