# Elsa 3.8/3.9 parity: identity administration and scope

Status: accepted (owner decisions 2026-10-15). Sipke Schoorstra approved D1–D11 below exactly as recommended.

Program: [Elsa 3.8/3.9 parity #2771](https://github.com/elsa-workflows/elsa-foundation/issues/2771), goal bucket
[`elsa3-parity`](../program-goals/elsa3-parity.md). Planning record and full assessment:
[valence-works/strategy#8](https://github.com/valence-works/strategy/issues/8) (maintainers only). Constrained by
[ADR 0080](0080-elsa-4-simplification-decisions.md) (simplification decisions). In this ADR, D1–D11 are this ADR's
decisions; cite them as "ADR 0085 D7" to keep them apart from ADR 0080's numbering.

## Context

Elsa 4 is an unreleased preview. Sipke wants it to do what Elsa 3.8 and 3.9 do. Every change in the eight
elsa-core 3.8.x and 3.9.x releases was checked against Elsa 4 (main at `d2acadc`): of 99 changes, 33 are already
covered, 22 carry over as they are, 23 need adapting to Elsa 4's model and 21 can be dropped.

Elsa 4 is already level on diagnostics, the dashboard API, secrets, the outbox and commit atomicity, OpenTelemetry,
Weaver, BPMN, output conversion and most of the security hardening. The big gap is identity administration.
Elsa 4 has a sound identity design ([authentication architecture](../reference/authentication-architecture.md),
[identity configuration](../reference/identity-configuration.md)): an IAM plane apart from a protocol plane,
FoundationOwned / ExternalOwned / Hybrid ownership modes, one permission evaluator, OpenIddict-issued tokens, and
store contracts for users, roles, applications, credentials, external identity links, claim mapping and provider
configuration. What it lacks is everything on top: no API or Studio screen to manage users, roles, API keys or
SSO providers; interactive OIDC sign-in neither links nor creates an Elsa user; only one OIDC provider, from shell
settings; no request-level tenant resolution; and no way to bring Elsa 3 identities across.

Elsa 3 users will also arrive with 3.9 roles written in 3.9's `resource:verb` permission names, and with
integrations that authenticate by API key.

## Decisions

### D1: Built-in user store and external IdPs, both

Elsa 4 supports both, chosen per deployment through the existing ownership modes. The built-in store stays on by
default, for self-hosting and Elsa 3 parity; external OIDC is an option. Studio's admin screens follow the active
provider's capabilities and are hidden when users live in the IdP.

*Why:* the ownership modes already model this, and dropping either side breaks a large group of Elsa 3 users.

### D2: Elsa 3.9's permission vocabulary on Elsa 4's evaluator

Permissions are renamed to 3.9's `resource:verb` shape with trailing-`*` matching of child resources; `*` stays
superuser. Elsa 4 keeps its single evaluator. The implication catalog shrinks to one rule: `manage` implies `read`.
Unknown grants log a startup warning. A 3.9 → 4 mapping table is published.

*Why:* renames are cheap before GA and expensive after; 3.9 roles then migrate almost one to one, and the docs read
the same in both products. Porting 3.9's evaluator would add a second authority for no gain.

### D3: OIDC providers from settings, with optional database overrides

Shell settings stay the deployment baseline and are read-only in Studio. Database-backed providers are opt-in. A
database entry fully shadows the settings entry with the same key, with no field merging. Client secrets are always
Elsa Secrets references, never inline values.

*Why:* this is Elsa 3.8's model, which operators already understand, and it keeps configuration-as-code deployments
deterministic.

### D4: IdP groups and roles map to Elsa roles

Admins map IdP groups or roles to Elsa roles per provider. Provider claims never become permissions directly unless a
grant source is configured explicitly.

*Why:* Elsa stays the only authority that turns roles into permissions, as in Elsa 3. Elsa 4's claim mapping rules
already fit.

### D5: Unknown external users are rejected by default

By default, an external identity with no link is refused. Per provider, an admin can opt in to creating the user on
first sign-in (JIT) with static default roles. Elsa never links by email unless a verified-email matcher is enabled
explicitly. Links are keyed by (tenant, provider, issuer, subject).

*Why:* safe by default; email is not a stable or trustworthy key across IdPs.

### D6: Host-wide identity for v1, designed for per-tenant later

Providers and users are host-wide in v1; every record carries its tenant id, as today. The design must let
per-tenant providers and users follow. Tenant resolution is decided separately, token claim first, then host or
header.

*Why:* it matches Elsa 3.8 and the records already carry tenant ids. The labs plan runs one shell per tenant group,
which works with this.

### D7: The backend keeps owning the login page

Studio still never renders login. Elsa 3.8's login-method chooser and the Classic theme come to the backend login
page. Studio gets a sign-out button in its app bar.

*Why:* keeping credentials out of the Studio bundle is an existing locked decision, and nothing in Elsa 3's UI needs
Studio-rendered login.

### D8: Elsa 3 identities come across through a one-shot import

A one-shot import (CLI or admin endpoint, dry-run report first) brings users, roles, applications and OIDC
connections across. Elsa 3 password and API-key hashes are kept behind a legacy verifier that rehashes on the first
successful sign-in, so nobody has to reset. Roles are rewritten through the D2 mapping table.

*Why:* no forced resets and no lasting compatibility layer to maintain.

### D9: API keys carry over

API keys are built on Elsa 4's application and credential records: hashed at rest, shown once, rotatable and
revocable, authenticated into the normal principal pipeline. OAuth client credentials can follow later.

*Why:* many Elsa 3 integrations depend on API keys.

### D10: Non-identity scope

- **Webhooks and User Tasks: yes**, after identity; User Tasks needs participants.
- **StateMachine: deferred**; BPMN covers most state-driven flows. Revisit on demand.
- **MQTT:** belongs to Elsa Integrations, not this program.
- **LDAP: no.** Use an external IdP.

### D11: Tracked as the `elsa3-parity` program goal

Parity work is tracked as a new `elsa3-parity` program goal in this repository
([goal bucket](../program-goals/elsa3-parity.md), program issue #2771, label `elsa3-parity`). #1152 stays the record
of activity-surface gaps. This is a named owner exception to [ADR 0080 D0](0080-elsa-4-simplification-decisions.md#d0--focus)'s
parking and two-program cap, recorded there as the 15 October amendment.

## Consequences

- Work proceeds in phases. Phase 1, users and roles, is filed now: the permission vocabulary (#2773), user store
  queries (#2774), API keys (#2775), Studio sign-out (#2776), the users API (#2777), the roles and permissions API
  (#2778), escalation and last-admin guards (#2779), Studio Users (#2780) and Roles (#2781) pages, and Studio
  permission gating (#2782).
- Phase 2 (external OIDC: provisioning on sign-in, provider registry, group-to-role mapping, Studio SSO pages) is
  filed once Phase 1 is under way. Phases 3 (tenancy, Elsa 3 import), 4 (runtime operations) and 5 (Webhooks, User
  Tasks, secrets in expressions) follow.
- The D2 rename touches every permission check and seeded role. It lands first in Phase 1 so later issues use the new
  names.
- The rest of ADR 0080 applies to this program as to any other work.

## Considered options

The planning record lists the options per decision. The main ones not taken: external-IdP-only identity (D1),
keeping Elsa 4's dotted permission keys (D2), database-only provider storage (D3), claim → permission pass-through
(D4), auto-linking by email (D5), per-tenant identity in v1 (D6), Studio-rendered login (D7), docs-only migration or a
live compatibility layer (D8) and dropping API keys in favour of client credentials (D9).
