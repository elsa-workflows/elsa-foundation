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
- [Route ownership metadata](../glossary/elsa.md), collision validation, atomic CShells publication,
  and exact-generation binding.
- Bounded remaining module waves and final FastEndpoints retirement.

## Out Of Scope

- Feature dependency/settings classification and CShells Appsettings Generator implementation;
  those remain under [Feature Composition Readiness](feature-composition-readiness.md).
- Secrets or diagnostics persistence/provider migration; those remain under
  [Zero-EF Persistence](zero-ef-persistence.md).
- Structured Logs/OpenTelemetry domain behavior, persistence, or Studio UI work; those remain under
  [Diagnostics Observability Readiness](diagnostics-observability-readiness.md).
- Workflow-authored HTTP execution semantics beyond shared
  [endpoint security disposition](../glossary/elsa.md) and
  [route ownership metadata](../glossary/elsa.md).
- Replacing HTTP/JSON with another protocol or redesigning existing public routes and DTOs.
- Introducing or migrating MVC while no first-party MVC endpoint surface exists.
- Weakening the server-side host-management credential boundary established by
  [ADR 0037](../adr/0037-studio-management-bridge-keeps-host-management-key-server-side.md).

## Active Objectives

1. Apply the accepted
   [Minimal API target and bounded shared endpoint conventions](../adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md)
   across the program's implementation slices.
2. Route Minimal APIs and transitional FastEndpoints through one Foundation Identity permission
   contract ([#1344](https://github.com/elsa-workflows/elsa-foundation/issues/1344)). This work unit
   establishes canonical policies, normalized-principal trust, deterministic replacement contracts,
   and the transitional FastEndpoints adapter used by the remaining migration slices.
3. Route transport-adjacent activity-design and runtime-inspection authorization contexts through
   the same evaluator after the endpoint bridge lands
   ([#1356](https://github.com/elsa-workflows/elsa-foundation/issues/1356)).
4. Install HTTP/OpenAPI migration evidence and runtime
   [endpoint security disposition](../glossary/elsa.md)/authoring guards
   ([#1346](https://github.com/elsa-workflows/elsa-foundation/issues/1346)).
5. Prove the target with Studio Preferences as the canary
   ([#1347](https://github.com/elsa-workflows/elsa-foundation/issues/1347)), Secrets as the
   representative granular-permission API
   ([#1348](https://github.com/elsa-workflows/elsa-foundation/issues/1348)), and Structured Logs as
   the SSE proof ([#1349](https://github.com/elsa-workflows/elsa-foundation/issues/1349)).
6. Make CShells endpoint publication atomic, collision-safe, and generation-bound
   ([#1345](https://github.com/elsa-workflows/elsa-foundation/issues/1345)).
7. Convert the remaining inventory into bounded migration waves and retire FastEndpoints only after
   the proof gates pass ([#1350](https://github.com/elsa-workflows/elsa-foundation/issues/1350)).

## Current Delivery State

The architecture, shared authorization, compatibility harness, proof migrations, CShells atomic
publication, and transport-adjacent authorization follow-up are complete through #1356. The current
source audit identifies 164 concrete FastEndpoints registrations across 18 owners, while the
transition scanner records only 91 because it does not resolve indirect endpoint inheritance.

The inventory evidence and grouping rationale are recorded in the
[first-party REST API migration-wave report](../reports/first-party-rest-api-migration-waves-2026-08.md).
The canonical active backlog is the wave registry below and its self-contained GitHub issues.

## Remaining Wave Registry

Wave 0 is a hard prerequisite for every numbered FastEndpoints migration because the current source
scanner is incomplete. Retained-host track H and workflow-dynamic track D may start alongside it.
After Wave 0 lands, the program may run two or three non-overlapping migration sessions. The numeric
order is the preferred risk-controlled landing order; only entries in the **Hard blockers** column
are technical or program gates.

| Wave | Work unit | Owners / registrations | Hard blockers | Unloadability exit |
|---:|---|---|---|---|
| 0 | [Make the first-party endpoint inventory retirement-grade](https://github.com/elsa-workflows/elsa-foundation/issues/1364) | compatibility/architecture tooling | none | Not applicable: tooling only |
| H | [Complete ownership and security metadata for retained host routes](https://github.com/elsa-workflows/elsa-foundation/issues/1365) | Extension Builder 42; Workbench management 9, root/health 3, console-log 3; Foundation Host management 2 and health 2; CShells management 6 | none | Not applicable: root-hosted surfaces |
| D | [Complete workflow-authored dynamic HTTP publication metadata](https://github.com/elsa-workflows/elsa-foundation/issues/1366) | `Elsa.Http` / unbounded runtime-authored routes | none | Required for the exact owning shell generation |
| 1 | [Migrate small and read-oriented APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1367) | Capabilities, Attention, Expressions, JS Rendering, Runtime JS, Dashboard / 8 | Wave 0 | Required for every listed owner |
| 2 | [Migrate bounded CRUD, interchange, and import APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1368) | BPMN, Modularity, Execution Evidence, Elsa 3 import / 13 | Wave 0 | Required for every listed owner |
| 3 | [Migrate Foundation Identity protocol APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1369) | Foundation Identity + ASP.NET Core Identity / 9 | Wave 0 | Required for both listed owners, including auth-scheme/provider retention |
| 4 | [Migrate Agent REST and SSE APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1370) | Agent / 11 | Wave 0 | Required for Agent, including SSE cleanup |
| 5 | [Migrate OpenTelemetry query, streaming, and OTLP APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1371) | Diagnostics OpenTelemetry / 11 | Wave 0 | Required, including SSE/OTLP route, serializer, and auth retention |
| 6 | [Migrate Workflows Design APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1372) | Workflows Design / 27 | Wave 0 | Required for Workflows Design |
| 7 | [Migrate Activities Design APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1373) | Activities Design / 38 | Waves 0 and 6 | Required for Activities Design |
| 8 | [Migrate Publishing APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1374) | Publishing / 23 | Waves 0, 6, and 7 | Required for Publishing |
| 9 | [Migrate Runtime APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1375) | Runtime / 24 | Wave 0 | Required for Runtime |
| Final | [Retire FastEndpoints from first-party REST APIs](https://github.com/elsa-workflows/elsa-foundation/issues/1376) | shared framework and all owners | Waves 0-9, H, and D | Reconcile all owner-specific evidence and approved exceptions |

Every migration issue must be self-contained and materialize checklists for exact owners and counts,
immutable FastEndpoints-before HTTP/OpenAPI fixtures, ownership/security metadata, Foundation
Identity permission and catalog coverage, owner-specific unloadability evidence, coexistence and
relevant E2E gates, hard blockers, a reviewable rollback point, exact baseline reconciliation, exit
criteria, and post-merge evidence. Final retirement requires zero first-party FastEndpoints
registrations, not merely zero unapproved exceptions.

Wave 1 must preserve `ApiCapabilitiesRead`; it is not a public route. Wave 2 must replace Execution
Evidence's wildcard-only authorization with catalog-owned read and delete/manage permissions, with
explicit implication behavior, unless a separate security review approves and records an exception.
No migration issue may silently convert an existing permission-protected route to public access.

## Delivery Order

The primary dependency chain is:

`#1343 -> #1344 -> #1346 -> #1347 -> (#1348 + #1349) -> #1350`

The dynamic-routing track can proceed in parallel after the ownership-metadata decision:

`#1343 -> #1345 -> #1350`

The transport-adjacent authorization follow-up can proceed after the shared endpoint bridge and
must complete before final retirement planning closes:

`#1344 -> #1356 -> #1350`

Do not begin broad migration waves before the canary, granular-permission, streaming, compatibility,
and routing gates are complete. Later module waves receive separate issues rather than expanding
#1350 into a repository-wide migration PR.

## Linked Surfaces

- [Program issue #1342](https://github.com/elsa-workflows/elsa-foundation/issues/1342)
- [Delivery board: GitHub Project 45](https://github.com/orgs/elsa-workflows/projects/45)
- [ADR 0068: First-party REST APIs use ASP.NET Core Minimal APIs](../adr/0068-first-party-rest-apis-use-aspnet-core-minimal-apis.md)
- [Spike issue #1329](https://github.com/elsa-workflows/elsa-foundation/issues/1329)
- [Spike report](../reports/endpoint-framework-authorization-spike-2026-08.md)
- [Remaining migration waves](../reports/first-party-rest-api-migration-waves-2026-08.md)
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
