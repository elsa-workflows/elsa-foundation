# Implementation Plan: Domain-Owned Management APIs

**Branch**: `598-domain-owned-apis` | **Date**: 2026-07-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/092-domain-owned-apis/spec.md`

## Summary

Replace the host-owned `ElsaWorkflowManagementApi` with a supported management-client API composed from domain-owned FastEndpoints features. Correct executable retention first by treating retained workflow executions as artifact roots, then introduce Publishing-owned publication slots with failure-safe activation and publication-scoped trigger projections. Enrich the existing Workflow Design and Activity Design APIs, add Expressions and API Capabilities modules, move executable inspection into Runtime, migrate Elsa Studio to the canonical contracts, and remove the legacy facade without a compatibility adapter.

## Technical Context

**Language/Version**: C# 14 / .NET 10 for Foundation; TypeScript 5 + React for Elsa Studio

**Primary Dependencies**: ASP.NET Core, CShells/FastEndpoints, Elsa Modularity and Events, Elsa Mediator, Groundwork document persistence, React Query in Studio

**Storage**: Provider-neutral Runtime and Publishing store contracts with in-memory defaults; Groundwork document implementations and indexes for durable hosts

**Testing**: xUnit and ASP.NET Core TestHost for Foundation; Vitest/React Testing Library, TypeScript typecheck, and package/root builds for Studio

**Target Platform**: Cross-platform ASP.NET Core server/worker shells and browser-hosted Elsa Studio

**Project Type**: Multi-package modular web API plus a coordinated frontend repository

**Performance Goals**: One capability-bootstrap request; no per-definition N+1 queries; server-side distinct artifact-root lookup; no per-domain existence probing; publication preflight and activation bounded by the candidate plus conflicting trigger claims

**Constraints**: Preserve Design-only, Runtime-only, and combined deployment shapes; Runtime must not reference Design; old routes disappear immediately; failed publication cannot displace current authority; retained executions must never lose pinned artifacts; existing refactor tests remain behaviorally intact

**Scale/Scope**: Six API capability areas, two new Foundation projects, four enriched API projects, Runtime/Publishing persistence changes, trigger providers/projections, Elsa.Server composition, and the Workflows plus Weaver Studio modules

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

The Elsa constitution is draft (v3.3.0) and the framework constitution is draft (v3.1.0); the plan treats their current wording as the applicable quality gate and records clarifications rather than silently overriding them.

| Gate | Pre-design result | Post-design result |
|---|---|---|
| Framework §2.1/§2.2 domain layering and naming | PASS — every API slice remains in its model-owning domain | PASS — contracts allocate commands, queries, and projections without a global management implementation package |
| Elsa §E2.2 Design/Runtime split | PASS — Runtime consumes only executable/runtime persistence contracts | PASS — Publishing reads Design to compile; Runtime inspection never loads Design |
| Elsa §E2.2.3 deployment shapes | PASS — capabilities advertise only active modules | PASS — Design-only and Runtime-only hosts remain independently composable |
| Elsa §E2.6 artifact-only runtime | PASS — executions pin immutable executable identity | PASS — retention and inspection are artifact-based; source references are provenance only |
| Elsa §E2.8 activity catalog source of truth | PASS — canonical catalog projects persisted activity definitions/versions | PASS — no live-provider enumeration or descriptor fallback remains |
| Elsa §E2.9 state scope and architectural triplet | PASS — publication state is outside authored state | PASS — authored state, read projections, and executables stay separate |
| Framework §2.6 cross-feature contribution | PASS with terminology clarification — public API “capability” is not the retired synonym for a composition feature | PASS — static declarations are feature metadata; conditional contributions use a typed Source plus one aggregating handler/event |
| Framework §2.10 CQS persistence boundary | PASS — read models and mutation commands are distinct | PASS — slot activation is a command; provenance, slot, and retention-root queries do not mutate |
| Framework §2.11 dependency declaration | PASS — domain API features declare API Capabilities dependency | PASS — startup diagnostics reject duplicate incompatible declarations |
| Framework §2.16.1 small-project guidance | PASS — new projects are independently composable API units (exception class 6) | PASS — each ships a feature, tests, and documentation |
| Framework §2.21.1 refactor continuity | PASS — no test deletion is planned | PASS — test objectives move to domain tests; approved publication semantics replace append-only expectations |
| Framework §2.22/§2.23 docs and unit tests | PASS — feature docs and registration/behavior tests are in scope | PASS — extension catalogs and maps are refreshed with implementation |
| Elsa §E6 naming | PASS — planned names use sanctioned domain nouns and suffixes | PASS — no banned vague suffix or namespace repetition is introduced |

## Project Structure

### Documentation (this feature)

```text
specs/092-domain-owned-apis/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
└── tasks.md
```

### Source Code (repository root)

```text
src/
├── Apps/Elsa.Server/                         # reference composition only
└── Elsa/
    ├── Api/Capabilities/                     # new global shell-scoped discovery feature
    ├── Activities/Design/Api/                # canonical authoring catalog + availability
    ├── Expressions/Api/                      # new descriptor API
    └── Workflows/
        ├── Design/Api/                       # definitions, drafts, versions, analysis
        ├── Publishing/{Core,Api,...}/        # slots, policy, activation, reference mutation
        └── Runtime/{Core,Api,...}/           # artifacts, provenance, instances, retention

tests/Elsa/
├── Api/Capabilities/Tests/
├── Activities/Design/Api/Tests/
├── Expressions/Api/Tests/
└── Workflows/{Design,Publishing,Runtime}/.../Tests/

/Users/sipke/.codex/worktrees/091-domain-owned-apis/elsa-foundation-studio/
└── src/
    ├── Elsa.Studio.Workflows/Client/src/
    │   ├── api/                              # domain clients + capability cache
    │   └── workflow-editor/                  # publication and instance UX
    └── Elsa.Studio.Weaver.Workflows/Client/ # Runtime executable client
```

**Structure Decision**: Extend the existing domain API projects instead of creating a management facade. Create only the missing Expressions and API Capabilities feature projects. Publication-slot contracts live in Publishing Core; Runtime keeps artifact, execution, trigger-binding, and provenance read contracts. Durable Publishing storage uses a Publishing-owned Groundwork project if adding it to generic Runtime persistence would create a Publishing dependency. Studio changes in a fresh companion worktree from `origin/main` and is validated against this Foundation branch.

## Delivery Phases

1. Amend ADR 0040, add retained-executable root queries to in-memory and Groundwork execution stores, close GC races, and prove resume/inspection retention across statuses.
2. Add the publication-slot ADR and Publishing model, policy resolution, preflight, compare-and-swap activation, publication-scoped trigger/schedule projection, and reconciliation/outbox behavior.
3. Enrich Workflow Design and Activity Design canonical APIs, move executable inspection to Runtime, assign runtime diagnostics to Runtime, and add Expressions API.
4. Add global API Capabilities with explicit feature declarations, dynamic Source contributions, shell-scoped relative links, duplicate diagnostics, tests, and documentation.
5. Create the companion Studio worktree, split clients by domain, cache capabilities, update authoring/publishing/instance UX, and remove every fallback and legacy request.
6. Remove the facade and map call from Elsa.Server; update compositions, architecture guards, docs, maps, custom-host tests, and cross-repository release validation.

## Complexity Tracking

No constitution violations are planned. The client-facing term **API capability** is retained as an intentionally different concept from the framework's retired composition synonym “capability”; the glossary will make that distinction explicit in this work unit.
