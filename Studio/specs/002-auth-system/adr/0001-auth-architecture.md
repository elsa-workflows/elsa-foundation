# ADR 0001: Elsa Foundation Authentication & Authorization Architecture

**Status**: Accepted

**Date**: 2026-06-18

**Deciders**: Elsa Foundation maintainers

**Context source**: [spec.md](../spec.md), [research.md](../research.md)

## Context

Elsa has accumulated several overlapping auth stories:

- `Elsa.Identity` (elsa-core): a bespoke IAM + JWT issuer + API-key module with
  swappable user/role/application stores and CShells shell features.
- Elsa Studio (Blazor): `Authentication.ElsaIdentity` (JWT against Elsa Identity),
  `Authentication.OpenIdConnect` (standards-based OIDC), and a deprecated
  `Elsa.Studio.Login` module.
- No OpenIddict or OAuth2/OIDC *server* exists in elsa-core today; Elsa issues its
  own JWTs.

`elsa-foundation` + `elsa-foundation-studio` need **one** extensible auth
architecture grounded in ASP.NET Core auth primitives, deliverable as modules,
that serves enterprise (external IdP), self-hosted (turnkey), and developer
(quickstart) deployments without forking.

The question that triggered this work: should OpenIddict be the default instead of
the custom Elsa Identity module?

## Decision

We adopt a **two-plane, module-composed** architecture with **selectable ownership
modes**, and the following posture:

1. **Separate the IAM domain plane from the authentication protocol plane.**
   Users, roles, permissions, applications, credentials, and tenant membership are
   modeled independently of how principals authenticate.

2. **External OIDC integration is the default posture**, implemented with ASP.NET
   Core authentication handlers (OIDC/JWT-bearer/cookie). Enterprises plug in their
   existing IdP.

3. **OpenIddict is shipped as an optional first-party provider**, not a mandatory
   default. It powers self-hosted installs that want Elsa to issue tokens.

4. **The batteries-included reference stack is ASP.NET Core Identity + OpenIddict**,
   kept behind provider-agnostic foundation contracts. ASP.NET Core Identity
   provides local user/role management with swappable stores; OpenIddict provides
   standards-based OAuth2/OIDC token issuance, introspection, and revocation.

5. **Authorization is permission-based** over a shared permission catalog, enforced
   by the backend and reflected (not solely gated) by Studio.

6. **Ownership is selectable** — foundation-owned IAM (default), external-IdP-owned
   IAM, and hybrid — expressed as a capability setting, not separate architectures.

7. **Elsa Identity becomes a compatibility/bootstrap path**, not the long-term
   universal default. It remains available behind the same contracts during a
   documented migration window.

8. **Studio consumes one provider-agnostic auth SDK** (generalized from the Blazor
   `Authentication.Abstractions` shape). The deprecated `Elsa.Studio.Login` pattern
   is explicitly not carried forward.

## Why not "OpenIddict as the hard default"?

- It makes every install — including those that already own an IdP — responsible
  for operating an authorization server (signing-key lifecycle, client/consent
  management, security patch cadence).
- The default *posture* most enterprise adopters want is "use our existing IdP",
  which is external-OIDC integration, not Elsa-as-IdP.
- Making OpenIddict optional preserves the turnkey self-hosted story without
  imposing IdP operations on everyone.

## Why not "keep the bespoke Elsa Identity issuer as the default"?

- It is non-standard: no discovery, introspection, revocation, or consent
  semantics; a custom token issuer and API-key path that must be independently
  audited and maintained.
- ASP.NET Core Identity + OpenIddict deliver the same capabilities on supported,
  standards-based building blocks, while staying swappable.
- Elsa Identity still has value for migration and minimal installs, so it is
  retained as a compatibility profile rather than removed.

## Consequences

### Positive

- One architecture spans enterprise, self-hosted, and dev deployments.
- Standards-based protocol surface (OIDC/OAuth2) improves interoperability and
  auditability.
- Provider, store, and ownership choices are configuration/module decisions, not
  code forks.
- Studio gets a single auth contract; feature modules never bind to a provider.

### Negative / Costs

- Net-new OpenIddict integration in the foundation (no existing base in elsa-core).
- Ownership-mode and provider-capability matrix adds documentation and UX surface;
  must be made explicit in Studio to avoid operator confusion.
- A migration window with a compatibility profile must be built and supported.

### Neutral

- ASP.NET Core Identity and OpenIddict are reference defaults but are hidden behind
  foundation contracts, so alternative stacks (including the legacy Elsa Identity
  issuer) remain valid configurations.

## Deployment Profiles (resulting)

- **Profile A — External IdP + Foundation IAM (default)**: external OIDC
  authenticates; foundation owns users/roles/apps/permissions and links external
  identities.
- **Profile B — OpenIddict + Foundation IAM**: foundation is the IdP via OpenIddict
  with ASP.NET Core Identity user/role management.
- **Profile C — External-IdP-owned IAM**: external provider owns users/groups/apps;
  foundation maps claims to permissions.
- **Profile D — Compatibility (legacy Elsa Identity)**: existing installs keep
  working during staged migration.

## Follow-ups

- Resolve open questions in research.md (permission propagation boundary, account
  linking, store bridging vs migration, per-tenant provider config).
- Validate the reference stack against multi-tenancy and SignalR auth in a spike.
