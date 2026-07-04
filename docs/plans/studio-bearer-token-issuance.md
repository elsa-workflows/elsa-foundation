# Plan: Real bearer-token issuance for Studio (ASP.NET Core Identity + OpenIddict)

**Status:** Proposed
**Owner:** Identity workstream (was flagged "W18")
**Consumers:** `elsa-foundation-studio` shell (issue #208 — the client seam is already wired; see §7)

## 1. Problem & goal

The Studio shell (`elsa-foundation-studio`) mounts a user-auth subsystem that, when enabled, exchanges the
browser session cookie for a **bearer token** and attaches it to every backend HTTP/SignalR call, with a
401→refresh→retry loop. The client seam is complete and shipped: it issues

```
GET  {tokenEndpoint}            (credentials: include, cache: no-store)
→ 200 { "accessToken": "<token>" }   when the caller has a live session cookie
→ 401                                 when anonymous (client treats as "no token")
```

with `tokenEndpoint` defaulting to **`/_elsa/identity/token`**.

On the backend, the Elsa Foundation identity layer is ~80% built:

- **`Foundation/Identity/Abstractions`** — full contracts (`ITokenService`, `IAuthSessionService`,
  `IAuthenticationProviderResolver`, permission/ownership/security seams) with default implementations.
- **`Foundation/Identity/Api`** — 5 of 6 endpoints implemented: `bootstrap`, `capabilities`, `session`,
  `refresh`, `challenge`, `logout` (all under `/_elsa/identity/*`).
- **`Foundation/Identity/OpenIddict`** — `OpenIddictTokenService` implements `ITokenService` (issue / refresh
  / validate / revoke) over an **in-memory, opaque-random-token** store.
- **`Foundation/Identity/Oidc`** — external-OIDC provider module + JwtBearer/OpenIdConnect handler config.
- **`Foundation/Identity/AspNetCoreIdentity`** — **scaffold only**: `UserManager`/`RoleManager` wrappers,
  an `InMemoryIdentityStore` sketch, and a principal factory, none registered by default.

### The gaps

1. **No `GET /_elsa/identity/token` endpoint.** `ITokenService.IssueAsync` exists but nothing exposes it,
   so the client's `getAccessToken` always 401s → no real bearer token. **(critical, smallest unit)**
2. **No bearer *validation* for issued tokens.** `OpenIddictTokenService` mints opaque random strings with an
   in-memory store; there is no authentication handler that accepts those tokens on subsequent API calls, so a
   token, once minted, authenticates nothing.
3. **No credential substrate / sign-in.** ASP.NET Core Identity is sketched but not wired: no persistent user
   store, no `SignInManager`, no login endpoint. Without a session cookie there is nothing to exchange.
4. **Features are discoverable but disabled.** `FoundationIdentityApi`, `FoundationIdentityOpenIddict`, and
   `FoundationIdentityAspNetCoreIdentity` are intentionally off in `shells.json` (a comment notes enabling the
   token endpoints without a token service would fault endpoint registration).

This plan closes gaps 1–4 as two cohesive modules — **ASP.NET Core Identity** (who the user is) and
**OpenIddict** (standards-compliant token issuance & validation) — plus the glue endpoint and enablement.

## 2. Target architecture

```
Browser (Studio SPA, same origin as the server)
   │  1. GET /_elsa/identity/challenge/{provider}   → login UI / external OIDC
   │  2. cookie session established (ASP.NET Core Identity SignInManager OR external OIDC)
   │  3. GET /_elsa/identity/token   (cookie) ─────────────► issues bearer for the cookie principal
   │  4. GET /api/...  Authorization: Bearer <token> ─────► validated by OpenIddict validation handler
   ▼
ASP.NET Core Identity module        OpenIddict module
  • EF Core user/role store            • OpenIddict server (token + introspection/JWKS)
  • password hashing, lockout          • JWT (or reference) access tokens
  • SignInManager cookie sign-in       • ASP.NET Core validation handler (accepts bearer on APIs)
  • login/logout endpoints             • claims: sub, tenant, roles, permissions/scopes
```

Decision points to lock before coding:

- **Token format:** prefer **OpenIddict-issued JWT access tokens** (self-validating, no shared in-memory
  store, works across scaled-out instances) over the current opaque in-memory tokens. Keep the existing
  `OpenIddictTokenService` only as a dev/test reference behind an option.
- **Cookie↔token bridge:** `GET /_elsa/identity/token` runs under the *cookie* auth scheme and returns a
  bearer for the already-authenticated principal (an authorization-code-less, first-party exchange). This is
  simpler than a full `/connect/token` password grant and matches the client's GET-returns-`{accessToken}`
  contract exactly.
- **Same-origin hosting:** the client requests `{shell-origin}/_elsa/identity/token` with `credentials:
  include`. Host the Studio SPA from the Foundation server (same origin) so the session cookie flows. The
  studio config supports overriding `tokenEndpoint` to an absolute backend URL for cross-origin setups, but
  that then requires CORS + `SameSite=None; Secure` cookies — avoid unless necessary.

## 3. Workstream A — ASP.NET Core Identity module

Goal: a real, persistent identity substrate with cookie sign-in.

**A1. Persistence.** Replace the `InMemoryIdentityStore` sketch with an EF Core store (new project
`Elsa.Foundation.Identity.AspNetCoreIdentity.EntityFrameworkCore`, or fold into the existing project behind an
option). Entities: `AspNetCoreIdentityUser : IdentityUser`, roles, claims. Provide a `DbContext` and a
design-time factory; wire migrations. Keep the in-memory store as the default only for `IsDevelopmentOrDemo`.

**A2. Registration.** In `AddFoundationAspNetCoreIdentity(...)`:
- `services.AddIdentityCore<AspNetCoreIdentityUser>()` + `.AddRoles<IdentityRole>()`
  `.AddEntityFrameworkStores<...>()` `.AddSignInManager()` `.AddDefaultTokenProviders()`.
- Register the existing `AspNetCoreIdentityPrincipalFactory` as `IUserClaimsPrincipalFactory<>` so
  tenant/permission claims are projected (feeds `IClaimsNormalizer`).
- Add a cookie authentication scheme for first-party sign-in (or reuse the app's cookie scheme).

**A3. Sign-in surface.** Add first-party endpoints under `/_elsa/identity/*` (FastEndpoints, same pattern as
existing endpoints — `AllowAnonymous`, validate via `SignInManager.CheckPasswordSignInAsync`):
- `POST /_elsa/identity/login` `{ username, password, tenantId? }` → issues the cookie, returns the session.
- (`logout` already exists; ensure it clears the Identity cookie scheme.)
- Provider bootstrap: register an `IAuthenticationProviderModule` for the local Identity provider
  (`id: "aspnetcore-identity"`, `kind: "openiddict"` or `"password"`) with a redirect/none challenge so it
  shows up in `bootstrap`/`capabilities`. Mirror `OpenIddictAuthenticationProviderModule`.

**A4. Seeding.** Behind `IsDevelopmentOrDemo`, seed an admin user + roles so a fresh checkout can log in.

**A5. Tests.** Store round-trip, password validation, principal-factory claim projection, login endpoint
(happy path + bad credentials → 400/401), seeded-admin sign-in.

## 4. Workstream B — OpenIddict module (issuance + validation)

Goal: standards-compliant, self-validating tokens; replace the in-memory reference service for production.

**B1. OpenIddict server.** In `AddFoundationIdentityOpenIddict(...)` add the OpenIddict core + server +
ASP.NET Core integration (packages already pinned: `OpenIddict.AspNetCore` 7.5.0):
- `AddOpenIddict().AddCore(...)` with an EF Core store (can share the Identity `DbContext`).
- `.AddServer(o => …)`: enable the token endpoint, set signing/encryption credentials (dev ephemeral key when
  `IsDevelopmentOrDemo`, else `FoundationIdentityOptions.SigningKey`/cert), set access-token lifetime from
  `OpenIddictIdentityOptions.AccessTokenLifetime`, register scopes, and **prefer JWT access tokens**
  (`o.UseAspNetCore().EnableTokenEndpointPassthrough()` if we hand-roll the exchange in FastEndpoints, or use
  OpenIddict's own `/connect/token` — see B3).
- `.AddValidation(...)`: local validation of the issued tokens.

**B2. Bearer validation handler.** Register the OpenIddict validation handler (or JwtBearer against the local
issuer) as an authentication scheme and make it the default for the `/api` surface, so
`Authorization: Bearer <token>` authenticates. This is the piece that makes an issued token actually *do*
something. (If we keep reference tokens instead of JWT, wire introspection to `ITokenService.ValidateAsync`.)

**B3. Reconcile `ITokenService`.** Two options:
- **(Preferred)** Make `OpenIddictTokenService.IssueAsync` mint a real OpenIddict token from the supplied
  `ClaimsPrincipal` (sub/tenant/scopes) via the OpenIddict token manager, returning the JWT + expiry +
  refresh token. `ValidateAsync`/`RevokeAsync` delegate to OpenIddict. Keeps the `ITokenService` seam and the
  existing `Refresh` endpoint working unchanged.
- **(Fallback)** Keep the in-memory opaque implementation for dev/test, guarded by option; ship JWT for prod.

**B4. Tests.** Issue→validate→expire→revoke over the real pipeline; JWT contains `sub`, tenant, roles,
permission/scope claims; refresh rotation; validation handler accepts a freshly issued token on a protected
endpoint and rejects a tampered/expired one.

## 5. Workstream C — The token endpoint (the critical glue)

**C1. `GET /_elsa/identity/token`** — new FastEndpoints endpoint in `Foundation/Identity/Api/Endpoints/Token.cs`.

```csharp
// Runs under the cookie scheme; anonymous callers get 401 (client treats 401 as "no token").
internal sealed class Token(ITokenService tokens, IOptions<FoundationIdentityOptions> identity)
    : ElsaEndpointWithoutRequest<AccessTokenResponse>
{
    public override void Configure()
    {
        Get(IdentityRouteConstants.GetRoute("token"));
        // Require an authenticated *session* (cookie / external OIDC), NOT a bearer.
        // e.g. Policies/AuthSchemes: the Identity cookie + external OIDC schemes.
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            await Send.UnauthorizedAsync(ct);   // → client sees 401 → stays anonymous
            return;
        }

        var subject = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var tenantId = User.FindFirstValue(IdentityClaimTypes.TenantId) ?? "default";
        var scopes = User.FindAll(IdentityClaimTypes.Permission).Select(c => c.Value).ToArray();

        var result = await tokens.IssueAsync(new TokenIssueRequest(subject!, tenantId, scopes), ct);
        await Send.OkAsync(new AccessTokenResponse(result.AccessToken, result.ExpiresAt), ct);
    }
}

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);
```

Notes:
- Response shape: the client only reads `accessToken`; `expiresAt` is additive and harmless.
- **Do not** `AllowAnonymous`; instead require the cookie/external session schemes so anonymous → 401. Confirm
  the endpoint is *not* gated by the bearer scheme (that would be circular).
- The **cookie-flow refresh** the client uses does **not** post to `/refresh`; it re-probes `GET
  /_elsa/identity/session`, and on a live cookie re-fetches `GET /token`. So the existing `Refresh` endpoint
  (token-based) is orthogonal to the Studio cookie flow and needs no change for #208. (Its request/response
  contract differs from the redirect adapter's `refreshEndpoint` shape — leave it for API/first-party token
  clients, or align later; out of scope here.)

**C2. Endpoint tests.** Anonymous → 401; authenticated cookie principal → 200 `{ accessToken }` with the
subject/tenant/scope claims flowing into `IssueAsync`; issued token validates on a protected API call (ties A+B+C).

## 6. Workstream D — Enablement & app wiring (`src/Apps/Elsa.Server`)

**D1. Enable features** in `shells.json` for the default shell:
`FoundationIdentityApi`, `FoundationIdentityOpenIddict`, `FoundationIdentityAspNetCoreIdentity` (in addition to
the already-on `FoundationIdentityAbstractions`, `FoundationIdentityOidc`). Remove/replace the "W18" guard
comment in `Program.cs` once a token service is present (its stated fault condition — token endpoints without a
service — is now resolved by B).

**D2. Middleware order.** Ensure `UseAuthentication`/`UseAuthorization` sit after routing and that the schemes
compose: cookie (+ external OIDC) for the identity/session surface, OpenIddict validation for the API surface.

**D3. Config.** Document the new `FoundationIdentityOptions`/`OpenIddictIdentityOptions`/EF connection settings
and dev seeding switch in `appsettings`/`shells.json`.

## 7. Consumer already wired (elsa-foundation-studio, issue #208)

No further Studio work is required to consume this; for reference, the shell now:

- `StudioAuthRuntimeConfig` gained `tokenEndpoint?` / `refreshEndpoint?`; the host emits an `auth` object from
  `Studio:Auth:*` on `/studio-runtime.js`.
- `createBackendAuthProviderManager` forwards `tokenEndpoint` (default `/_elsa/identity/token`) + refresh into
  the redirect adapter, so `getAccessToken` mints a real bearer and the HTTP client does 401-refresh-retry.
- The authenticated endpoint context exposes an `accessTokenFactory`; the ConsoleStream SignalR hub uses it
  (`withAuthenticatedSignalROptions` primitive) so hub connections carry the same bearer.

To integrate: host the SPA same-origin, enable the features (§6), and set `Studio:Auth:Enabled=true`. The
client will then drive `challenge → session → token` against these endpoints.

## 8. Sequencing & acceptance

1. **A (Identity substrate + login)** — unblocks having a session to exchange.
2. **B (OpenIddict issuance + validation)** — real tokens that authenticate API calls.
3. **C (`GET /token`)** — the exchange; smallest change, but depends on A (session) + B (issuer/validator).
4. **D (enable + wire)** — turn it on in `Elsa.Server`.

**Definition of done:** with features enabled and the SPA same-origin, a user logs in (cookie), the shell's
`GET /_elsa/identity/token` returns a JWT, backend API + ConsoleStream SignalR calls carry
`Authorization: Bearer …` and succeed, a 401 triggers one cookie re-probe + retry, and anonymous users still
boot when `Studio:Auth:Enabled=false`. Covered by the new backend tests (A5/B4/C2) and the existing Studio
client tests (`auth-token-endpoint`, `app-auth`, ConsoleStream `module`).

## 9. Out of scope / follow-ups

- Aligning the token-based `POST /_elsa/identity/refresh` contract with the redirect adapter's client-held
  refresh-token shape (the Studio cookie flow doesn't use it).
- Full OAuth2 authorization-code / client-credentials grants for non-Studio API clients.
- Multi-tenant provider resolution and per-tenant signing keys.
- Token revocation UX / session management surface.
