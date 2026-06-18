# Research: Elsa Foundation Auth System

This document captures the current-state inventory and the key design decisions
behind the auth architecture. It satisfies the "inventory existing auth surfaces"
and "decision rationale" work in the plan.

## Current-State Inventory

### elsa-core: `Elsa.Identity` module

`Elsa.Identity` is a full, self-contained IAM + authentication module.

- **Domain & stores**: `User`, `Application`, `Role` entities with pluggable
  stores. Defaults to in-memory `MemoryUserStore` / `MemoryApplicationStore` /
  `MemoryRoleStore`. Providers are swappable: `StoreBasedUserProvider`,
  `ConfigurationBasedUserProvider`, `AdminUserProvider` (and role/application
  equivalents).
- **Contracts**: `IUserStore`, `IRoleStore`, `IApplicationStore`, `IUserProvider`,
  `IRoleProvider`, `IApplicationProvider`, `IUserManager`, `IRoleManager`,
  `IRoleAuthorizationService`, `IAccessTokenIssuer`, `IUserCredentialsValidator`,
  `IApplicationCredentialsValidator`, `IApiKeyGenerator`/`IApiKeyParser`,
  `ISecretHasher`.
- **Authentication**: `DefaultAuthenticationFeature` (a CShells `IShellFeature`)
  wires a multi-scheme policy (`Jwt-or-ApiKey`) using `AddJwtBearer` (access +
  refresh schemes) and `AspNetCore.Authentication.ApiKey`. It selects an API key
  provider (`DefaultApiKeyProvider` vs `AdminApiKeyProvider`), supports an admin
  API key, a development all-zero key (gated), and a `SecurityRoot` policy with an
  optional localhost grant.
- **Tokens**: `IdentityTokenOptions` (symmetric `SigningKey`, issuer, audience,
  access/refresh lifetimes, tenant-id claim type) and `DefaultAccessTokenIssuer`.
  Endpoints: `POST /identity/login` (anonymous, returns access + refresh) and
  `POST /identity/refresh-token` (refresh scheme).
- **Bootstrap & security**: `DefaultAdminUser` shell feature seeds an admin
  role/user idempotently. Production startup rejects missing/short/known-default
  signing keys (tolerated only in Development/Demo). Secrets hashed with
  PBKDF2-SHA256 (600k iterations), legacy SHA-256 upgraded opportunistically.
- **Multi-tenancy**: `ClaimsTenantResolver`, `CurrentUserTenantResolver`, tenant
  id claim type in token options.
- **Two configuration systems coexist**: a code-first `IdentityFeature`
  (`UseIdentity(...)` / `UseDefaultAuthentication(...)`) and a CShells shell-based
  `IdentityFeature` + `DefaultAuthenticationFeature` + `DefaultAdminUserFeature`.

**Observation**: `Elsa.Identity` already embodies the "two planes" idea (IAM vs
authentication) and is provider-swappable, but it is a bespoke implementation
(custom token issuer, custom API key path, custom user/role stores) rather than
built on ASP.NET Core Identity or a standards-based OAuth2/OIDC server.

### elsa-core: OpenIddict / OpenID Connect

- A repository search for `OpenIddict` in `elsa-core` returns **0 results**.
- A search for `OpenIdConnect`/`OIDC` server functionality in `elsa-core` modules
  returns **0 results**.

**Observation**: Elsa backend does **not** currently act as an OAuth2/OIDC
authorization server. It issues its own JWTs via a custom issuer. There is no
OpenIddict dependency to build on yet; adopting it is net-new.

### elsa-studio: authentication modules

Studio (the current Blazor product) has a layered set of auth modules:

- `Elsa.Studio.Authentication.Abstractions` — provider-agnostic contracts
  (`IAuthenticationProvider`, `IAuthenticationProviderManager`,
  `ICredentialsValidator`, `IAuthorizationService`, `IRefreshTokenService`,
  `IEndSessionService`, `IUnauthorizedComponentProvider`,
  `IHttpConnectionOptionsConfigurator`).
- `Elsa.Studio.Authentication.ElsaIdentity` (+ `.BlazorServer`, `.BlazorWasm`,
  `.UI`) — JWT-based auth against the Elsa Identity backend
  (`/identity/login`, `/identity/refresh`), local-storage token accessor,
  `AccessTokenAuthenticationStateProvider`, authenticating HTTP handler, SignalR
  configurator.
- `Elsa.Studio.Authentication.OpenIdConnect` (+ `.BlazorServer`, `.BlazorWasm`) —
  standards-based OIDC built on Microsoft's ASP.NET Core / Blazor auth stacks.
  Blazor Server uses Cookie + OpenIdConnect handlers with `SaveTokens`, automatic
  refresh via `AuthCookieEvents.OnValidatePrincipal`, separate
  `AuthenticationScopes` vs `BackendApiScopes`, PKCE, and an OIDC-authenticating
  HTTP handler / SignalR configurator. Host selects provider from
  `Authentication:Provider` configuration.
- `Elsa.Studio.Login` — explicitly `[Obsolete("Use ElsaIdentity instead.")]`. Still
  supports `UseElsaIdentity`, `UseOAuth2`, and a legacy `UseOpenIdConnect` (manual
  authorize/token/end-session endpoints). Superseded by the
  `Authentication.OpenIdConnect` modules that use authority metadata discovery.

**Observation**: Studio already has the right abstraction shape (a provider
manager with adapters for ElsaIdentity and OIDC). The deprecated `Login` module is
the pattern to avoid. The new foundation studio should generalize these
abstractions, not import the Blazor implementations.

### elsa-foundation-studio (this repo)

- No auth implementation exists yet (searches for auth/identity/openid in `src`
  return only unrelated matches). The shell is a React/Vite app hosted by ASP.NET
  Core with a CShells-based module model and a Studio module manifest endpoint.

**Observation**: This repo is a green field for the *Studio-facing* auth contract.
The backend contracts are designed here but land in `elsa-foundation`.

## Decision: Two planes — IAM domain vs authentication protocol

**Rationale**: `Elsa.Identity` and the Studio abstractions both already separate
"who the user is / what they can do" from "how they authenticate". Making this
split explicit and first-class lets providers change without reshaping the domain.

**Alternatives considered**:

- Couple identity to a single provider (e.g. only external OIDC): simplest, but
  blocks the batteries-included experience and self-hosted installs.
- Keep the bespoke `Elsa.Identity` as the only model: works today, but ties Elsa to
  custom security primitives that must be maintained and audited forever.

## Decision: External OIDC is the default posture; OpenIddict is an optional first-party provider

**Rationale**: Most enterprise targets already run an IdP (Entra, Auth0, Keycloak,
Okta). Defaulting to standards-based OIDC integration aligns with ASP.NET Core's
native model and avoids making Elsa responsible for being an IdP unless asked.
Shipping OpenIddict as an optional provider preserves a turnkey self-hosted story.

**Alternatives considered**:

- OpenIddict as the hard default: increases operational burden (key lifecycle,
  client/consent management, security cadence) for every install, including those
  that already have an IdP.
- External-OIDC only: blocks offline/self-hosted/dev-quickstart scenarios unless a
  local IdP profile is provided anyway.

## Decision: Batteries-included reference stack = ASP.NET Core Identity + OpenIddict

**Rationale**: ASP.NET Core Identity remains Microsoft's supported substrate for
local user/role management with swappable stores (EF Core, custom). OpenIddict is
the de-facto standards-based OAuth2/OIDC server for ASP.NET Core and integrates
cleanly with ASP.NET Core Identity. Together they replace the bespoke token issuer
and credential paths in `Elsa.Identity` for new installs, while staying behind
foundation contracts so module authors never bind to them directly.

**Alternatives considered**:

- Keep the custom Elsa Identity token issuer + API key path as the default:
  proven and lightweight, but non-standard, harder to audit, and missing full
  OAuth2/OIDC server semantics (consent, introspection, revocation, discovery).
- ASP.NET Core Identity for users/roles but a separate custom token issuer: viable
  middle ground; kept as an allowed configuration, but not the reference default
  because it forgoes standards-based protocol endpoints.
- Duende IdentityServer: feature-rich and well supported, but commercially
  licensed for most production use; unsuitable as an always-on default for an OSS
  foundation.

## Decision: Selectable ownership modes

**Rationale**: The product is modular and developers must choose who owns
identity. Entra-owned installs should let Entra own users; batteries-included
installs should let Elsa own users. Encoding foundation-owned / external-owned /
hybrid as a capability setting (not a fork) keeps one architecture.

**Alternatives considered**:

- Single fixed ownership model: simpler docs, but forces every install into one
  operational shape.

## Decision: Permission-based authorization shared by backend and Studio

**Rationale**: A shared permission catalog lets the same permission gate an API
endpoint and a Studio affordance, preventing drift. The backend stays the
enforcement source of truth; Studio uses permission claims only for presentation.

**Alternatives considered**:

- Role-only authorization: coarse and leaks role structure into UI logic.
- Independent UI authorization: drifts from backend enforcement and invites
  security-by-obscurity mistakes.

## Decision: Generalize the Studio provider-manager abstraction (do not import Blazor modules)

**Rationale**: The Blazor Studio `Authentication.Abstractions` shape (provider
manager + adapters + authenticating transport) is the right model, but the
implementations are Blazor-specific. The React foundation studio needs an
equivalent provider-agnostic auth SDK, with the deprecated `Login` pattern
explicitly excluded.

**Alternatives considered**:

- Port the Blazor modules: wrong runtime and coupling.
- Per-screen ad hoc auth: reproduces the drift the abstraction was built to avoid.

## Open Questions (to resolve during design slices)

- Effective-permission propagation: immediate vs token-refresh boundary, and
  whether to support short-lived access tokens with server-side permission
  evaluation.
- Account-linking policy specifics when multiple providers assert the same email.
- Whether legacy `Elsa.Identity` stores are bridged via an adapter or migrated via
  tooling (likely both, sequenced).
- Per-tenant provider configuration storage and resolution ordering.

## Stakeholder Notes

- The user explicitly approved: external OIDC default posture, ASP.NET Core
  Identity + OpenIddict as the batteries-included reference stack, foundation-owned
  IAM as the default ownership mode, and "developers can choose" as a first-class
  requirement.
