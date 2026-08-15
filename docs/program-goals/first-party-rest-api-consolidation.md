# First-party REST API Consolidation

Status: active.

Area: first-party REST endpoint authoring, shared authorization, dynamic route publication, and
FastEndpoints-to-Minimal-API migration.

Steward(s): Sipke plus active API/security architects and agents.

## Purpose

Execute the reviewed endpoint-framework and authorization decision from the
[2026-08 spike report](../reports/endpoint-framework-authorization-spike-2026-08.md): ASP.NET Core
Minimal APIs are the target authoring model for all first-party REST APIs, FastEndpoints coexists
only during bounded migration waves, Foundation Identity owns permission semantics, and CShells
publishes validated endpoint generations atomically.

The public delivery tracker is
[program issue #1342](https://github.com/elsa-workflows/elsa-foundation/issues/1342).
Program sequencing and status are coordinated on
[GitHub Project 45](https://github.com/orgs/elsa-workflows/projects/45), a private organization
project; public progress and validation evidence remain on the tracker and child issues.

## In Scope

- Explicit Minimal API module mapping seams for first-party REST APIs.
- Shared permission metadata, policy extensions, catalog contributions, and adapter contract tests.
- HTTP/OpenAPI compatibility snapshots and endpoint authorization/authoring guards.
- Studio Preferences, Secrets, and Structured Logs proof migrations.
- Endpoint ownership metadata, collision validation, atomic CShells publication, and exact-generation
  binding.
- Bounded remaining module waves and final FastEndpoints retirement.

## Out Of Scope

- Feature dependency/settings classification and CShells Appsettings Generator implementation;
  those remain under [Feature Composition Readiness](feature-composition-readiness.md).
- Secrets or diagnostics persistence/provider migration; those remain under
  [Zero-EF Persistence](zero-ef-persistence.md).
- Structured Logs/OpenTelemetry domain behavior, persistence, or Studio UI work; those remain under
  [Diagnostics Observability Readiness](diagnostics-observability-readiness.md).
- Workflow-authored HTTP execution semantics beyond shared endpoint disposition and ownership
  metadata.
- Replacing HTTP/JSON with another protocol or redesigning existing public routes and DTOs.
- Introducing or migrating MVC while no first-party MVC endpoint surface exists.
- Weakening the server-side host-management credential boundary established by
  [ADR 0037](../adr/0037-studio-management-bridge-keeps-host-management-key-server-side.md).

## Active Objectives

1. Ratify the Minimal API target and bounded shared endpoint conventions
   ([#1343](https://github.com/elsa-workflows/elsa-foundation/issues/1343)).
2. Route Minimal APIs and transitional FastEndpoints through one Foundation Identity permission
   contract ([#1344](https://github.com/elsa-workflows/elsa-foundation/issues/1344)).
3. Install HTTP/OpenAPI migration evidence and runtime endpoint disposition/authoring guards
   ([#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346)).
4. Prove the target with Studio Preferences as the canary
   ([#1347](https://github.com/elsa-workflows/elsa-foundation/issues/1347)), Secrets as the
   representative granular-permission API
   ([#1348](https://github.com/elsa-workflows/elsa-foundation/issues/1348)), and Structured Logs as
   the SSE proof ([#1349](https://github.com/elsa-workflows/elsa-foundation/issues/1349)).
5. Make CShells endpoint publication atomic, collision-safe, and generation-bound
   ([#1345](https://github.com/elsa-workflows/elsa-foundation/issues/1345)).
6. Convert the remaining inventory into bounded migration waves and retire FastEndpoints only after
   the proof gates pass ([#1350](https://github.com/elsa-workflows/elsa-foundation/issues/1350)).

## Delivery Order

The primary dependency chain is:

`#1343 -> #1344 -> #1346 -> #1347 -> (#1348 + #1349) -> #1350`

The dynamic-routing track can proceed in parallel after the ownership-metadata decision:

`#1343 -> #1345 -> #1350`

Do not begin broad migration waves before the canary, granular-permission, streaming, compatibility,
and routing gates are complete. Later module waves receive separate issues rather than expanding
#1350 into a repository-wide migration PR.

## Linked Surfaces

- [Program issue #1342](https://github.com/elsa-workflows/elsa-foundation/issues/1342)
- [Delivery board: GitHub Project 45](https://github.com/orgs/elsa-workflows/projects/45)
- [Spike issue #1329](https://github.com/elsa-workflows/elsa-foundation/issues/1329)
- [Spike report](../reports/endpoint-framework-authorization-spike-2026-08.md)
- [Foundation authorization contracts](../../src/Elsa/Foundation/Identity/Abstractions/Authorization/AuthorizationContracts.cs)
- [Current endpoint security guard](../../tests/Elsa/Architecture/EndpointSecurityTests.cs)
- [Feature Composition Readiness](feature-composition-readiness.md)
- [Diagnostics Observability Readiness](diagnostics-observability-readiness.md)
- [Zero-EF Persistence](zero-ef-persistence.md)

## Drift / Review Notes

- Keep the shared endpoint layer small; do not recreate FastEndpoints behind Elsa-owned abstractions.
- Preserve public HTTP and consumed OpenAPI behavior unless a separate contract change is approved.
- Keep endpoint migration separate from persistence, domain-feature, and Studio UI changes.
- Treat FastEndpoints coexistence as temporary compatibility, not the steady-state architecture.

## Removal or Completion Conditions

Complete this program when every first-party REST route uses an explicit Minimal API mapping seam or
has an approved dynamic/host-control disposition; shared permission/public/host metadata is complete;
HTTP, OpenAPI, security, coexistence, and collectible-context gates are green; CShells publication is
atomic and generation-bound; no permanent FastEndpoints registrations remain; and FastEndpoints
packages, endpoint bases, discovery/configuration, and transitional tests have been removed.
