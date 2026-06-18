# Implementation Plan: Elsa Foundation Auth System

**Branch**: `002-auth-system` | **Date**: 2026-06-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-auth-system/spec.md`

## Summary

Establish an extensible authentication and authorization story for
`elsa-foundation` (backend) and `elsa-foundation-studio` (React shell), grounded
in ASP.NET Core auth primitives and delivered as composable modules. The default
posture is external OIDC integration; the batteries-included reference stack is
ASP.NET Core Identity + OpenIddict, kept behind provider-agnostic foundation
contracts. IAM (users/roles/permissions/applications/credentials/tenancy) is
modeled as a plane independent of the auth protocol, with selectable ownership
modes (foundation-owned default, external-owned, hybrid). The decision and
rationale are recorded in [adr/0001-auth-architecture.md](./adr/0001-auth-architecture.md).

## Technical Context

**Language/Version**: C#/.NET 10 for foundation backend; TypeScript 5.6 / React 19 for Studio

**Primary Dependencies**: ASP.NET Core authentication/authorization; reference stack adds Microsoft.AspNetCore.Identity and OpenIddict; ASP.NET Core OpenID Connect / JWT bearer handlers for external IdP integration; existing Elsa tenant resolution

**Storage**: Pluggable stores behind foundation contracts; reference stack uses ASP.NET Core Identity stores (users/roles) and OpenIddict stores (applications/scopes/tokens); legacy Elsa Identity stores supported via compatibility adapter

**Testing**: dotnet test for contracts/providers/management APIs; Vitest for Studio auth SDK; integration tests per deployment profile

**Target Platform**: ASP.NET-hosted foundation backend + same-origin React Studio shell

**Project Type**: Modular backend + modular web client spanning two repositories (elsa-foundation, elsa-foundation-studio)

**Performance Goals**: Authentication/authorization checks must not be a per-request bottleneck; permission evaluation cached per request; token refresh transparent to module code

**Constraints**: Feature modules and Studio screens MUST NOT bind to a specific provider or ownership mode; backend is the authorization enforcement source of truth; no dev/default signing keys in production; secrets hashed at rest; tenant isolation throughout

**Scale/Scope**: Foundation contracts + reference-stack providers + management APIs + Studio auth SDK, validated across four deployment profiles (external IdP, OpenIddict self-hosted, external-owned, legacy compatibility)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

The current constitution (v1.0.0) is UI-system focused; auth-relevant principles
are applied where they map, and gaps are noted as future constitution work.

- **Modular UI contract**: PASS. Studio consumes auth only through the auth SDK; provider differences live in adapters, not the shell.
- **Workbench pattern fit**: PASS. Management surfaces (users/roles/applications/providers) reuse resource-index and master/detail archetypes.
- **Typography and token discipline**: PASS. Auth management screens use shared primitives and tokens; no auth-local styling.
- **Visual-language fidelity**: PASS. Management screens follow the Workbench direction.
- **Accessible interaction**: PASS. Login/challenge, management forms, and capability-disabled states expose keyboard/focus/labels/read-only semantics.
- **Real-screen proof**: PASS. Provider-agnostic sign-in and users/roles management are the first proof surfaces.
- **Auth-specific gate (proposed)**: Provider-agnostic seams and "backend is enforcement source of truth" are treated as binding constraints pending a constitution amendment for auth.

## Project Structure

### Documentation (this feature)

```text
specs/002-auth-system/
├── plan.md
├── spec.md
├── research.md
├── data-model.md
├── quickstart.md
├── adr/
│   └── 0001-auth-architecture.md
├── contracts/
│   ├── auth-module-contracts.md
│   ├── management-api.md
│   └── studio-auth-sdk.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (across repositories)

```text
# elsa-foundation (backend)
src/Elsa.Foundation.Identity.Abstractions/      # provider-agnostic contracts
├── Authentication/                              # IAuthenticationProviderModule, IPrincipalFactory, ITokenService
├── Iam/                                         # stores + managers (users/roles/apps/credentials)
├── Authorization/                               # IPermissionCatalog, IPermissionEvaluator, policies
└── Ownership/                                   # OwnershipMode, IOwnershipModeProvider, capabilities

src/Elsa.Foundation.Identity.OpenIddict/        # batteries-included reference provider
src/Elsa.Foundation.Identity.Oidc/              # external OIDC client provider (default posture)
src/Elsa.Foundation.Identity.AspNetCoreIdentity/# users/roles substrate for reference stack
src/Elsa.Foundation.Identity.Legacy/            # Elsa Identity compatibility adapter
src/Elsa.Foundation.Identity.Api/               # management API endpoints (/_elsa/identity/*)

# elsa-foundation-studio (this repo)
src/Elsa.Studio.Web/Client/src/auth/            # provider-agnostic auth SDK
├── AuthProvider.tsx
├── useAuthSession.ts
├── usePermissions.ts
├── useAuthCapabilities.ts
├── guards/                                      # AuthGuard, RequireAuth
├── adapters/                                    # oidc, openiddict-local, legacy
└── transport/                                   # http + signalr token injection

src/Elsa.Studio.IdentityManagement/Client/src/  # users/roles/apps/providers screens
```

**Structure Decision**: Keep all provider-specific code (OpenIddict, OIDC,
ASP.NET Core Identity, legacy) in dedicated modules behind the
`Identity.Abstractions` contracts. Studio depends only on the auth SDK and the
management API capability contract, never on a provider package.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Multiple provider modules + ownership modes | Enterprises differ on whether they own identity; one default cannot serve all | A single embedded IdP forces IdP operations on everyone; external-only harms local dev onboarding |
| Compatibility adapter for Elsa Identity | Existing installs need a migration window | Hard cutover would strand current Elsa Identity/Login users |

## Phase 0: Research

Research is captured in [research.md](./research.md). Key outcome: OpenIddict has
zero presence in elsa-core today (Elsa issues custom JWTs); external OIDC is the
right default; ASP.NET Core Identity + OpenIddict is the right reference stack
behind contracts.

## Phase 1: Design & Contracts

Design artifacts:

- [data-model.md](./data-model.md)
- [adr/0001-auth-architecture.md](./adr/0001-auth-architecture.md)
- [contracts/auth-module-contracts.md](./contracts/auth-module-contracts.md)
- [contracts/management-api.md](./contracts/management-api.md)
- [contracts/studio-auth-sdk.md](./contracts/studio-auth-sdk.md)
- [quickstart.md](./quickstart.md)

## Migration Strategy

- Treat `Elsa.Identity` / `Elsa.Studio.Login` as **Profile D (compatibility)**:
  supported behind the legacy adapter, not the new default.
- Provide store-level adapters so existing user/role/api-key data is readable
  through the new IAM contracts without an immediate data migration.
- Offer optional migration tooling to import legacy users/roles/api-keys and
  external identity links into the reference stores.
- Stage transitions A↔B↔C/D behind feature flags; document downtime expectations
  and rollback in profile migration playbooks.
- Do not carry the deprecated `Login` UI into new Studio templates; surface local
  credential login only via the OpenIddict/local or legacy adapter.

## Post-Design Constitution Check

- **Modular UI contract**: PASS. The Studio auth SDK contract forbids modules from reading tokens or branching on provider/ownership mode.
- **Workbench pattern fit**: PASS. Management API + SDK map cleanly onto resource-index and master/detail surfaces.
- **Typography and token discipline**: PASS. No auth-specific styling introduced.
- **Visual-language fidelity**: PASS. Capability-driven read-only states reuse shared primitives.
- **Accessible interaction**: PASS. Capability-disabled and read-only states are part of the SDK contract.
- **Real-screen proof**: PASS. Tasks require sign-in plus users/roles management as first proofs.
