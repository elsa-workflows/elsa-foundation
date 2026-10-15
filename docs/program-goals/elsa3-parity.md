# Elsa 3.8/3.9 Parity

## Status

Active. Created on 15 October 2026 as a named owner exception to
[ADR 0080 D0](../adr/0080-elsa-4-simplification-decisions.md#d0--focus). Program issue #2771, label `elsa3-parity`.

## Area

Identity administration first (users, roles, permissions, API keys, external OIDC sign-in), then tenancy, the Elsa 3
identity import, runtime operator controls and the Webhooks and User Tasks modules.

## Steward(s)

Sipke plus Elsa 4 Lead and its worker agents.

## Purpose

Make Elsa 4 do what Elsa 3.8 and 3.9 do, built the Elsa 4 way, so Elsa 3 users can move across without losing
features. Decisions: [ADR 0085](../adr/0085-elsa-3-parity-identity-administration-and-scope.md).

## In scope

- The identity management API and Studio Security area on the existing IAM contracts.
- External OIDC provisioning, several providers, group-to-role mapping.
- Request tenant resolution and the Elsa 3 identity import.
- Operator controls and the modules ADR 0085 D10 accepts.

## Out of scope

- Activity-surface gaps; #1152 keeps that record.
- StateMachine (deferred), MQTT (Elsa Integrations), LDAP (use an external IdP).
- Studio-rendered login (ADR 0085 D7).

## Active objectives

Phase 1, users and roles:

- #2773 permission vocabulary alignment (first).
- #2774 user store list, page, delete.
- #2775 API keys and applications.
- #2776 Studio sign-out.
- #2777 users API.
- #2778 roles and permissions API.
- #2779 privilege escalation and last-admin guards.
- #2780 Studio Users pages.
- #2781 Studio Roles pages.
- #2782 Studio permission gating.

## Linked surfaces

- [ADR 0085](../adr/0085-elsa-3-parity-identity-administration-and-scope.md).
- Planning record and the 99-row assessment: [valence-works/strategy#8](https://github.com/valence-works/strategy/issues/8)
  (maintainers only).
- Related issues: #792 tenant-scoped tagging, #1653 Secrets HTTP journey, #2665 console panel 401 after sign-in.

## Current roadmap notes

Phase 2 (external OIDC) is filed once Phase 1 is under way. Phases 3–5 follow.

## Removal or completion conditions

Complete when the identity phases (1–3) have shipped and the remaining accepted rows are either built or explicitly
dropped.
