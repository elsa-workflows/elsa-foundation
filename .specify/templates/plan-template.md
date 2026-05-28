# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION]  
**Primary Dependencies**: [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION]  
**Storage**: [if applicable, e.g., PostgreSQL, CoreData, files or N/A]  
**Testing**: [e.g., pytest, XCTest, cargo test or NEEDS CLARIFICATION]  
**Target Platform**: [e.g., Linux server, iOS 15+, WASM or NEEDS CLARIFICATION]
**Project Type**: [e.g., library/cli/web-service/mobile-app/compiler/desktop-app or NEEDS CLARIFICATION]  
**Performance Goals**: [domain-specific, e.g., 1000 req/s, 10k lines/sec, 60 fps or NEEDS CLARIFICATION]  
**Constraints**: [domain-specific, e.g., <200ms p95, <100MB memory, offline-capable or NEEDS CLARIFICATION]  
**Scale/Scope**: [domain-specific, e.g., 10k users, 1M LOC, 50 screens or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

This plan is governed by a **two-layer constitution**:

- **`.specify/memory/constitution.md`** — Elsa Workflow Engine Constitution (this repo's canonical constitution).
- **`.specify/memory/constitution-framework.md`** — Modular Software Design Framework Constitution (framework-neutral; cited from the Elsa constitution).

Every Elsa-code plan must satisfy the gates below. Each gate cites the rule it enforces. Mark a gate **PASS** when the plan demonstrably complies; **VIOLATION** triggers the *Complexity Tracking* table below with explicit justification (or the plan is reworked).

| # | Gate | Rule citation |
|---|---|---|
| G1 | Three-layer separation applied per feature; no global "Core" library. | framework §2.1 |
| G2 | Naming uses domain language only. No `Features.*`, `Modules.*`, `Implementations.*`, `Providers.*`, `Adapters.*`, `.Contracts`, `.Abstractions` segments anywhere in package/namespace names. | framework §2.2 |
| G3 | No heavy dependency in any `.Core` library. `.Core` allowed external NuGets: `Microsoft.Extensions.*Abstractions` + `Microsoft.Extensions.Primitives` only. | framework §2.1, §2.3 |
| G4 | No peer references between Layer-3 implementation libraries across unrelated sub-domains. Cross-feature coupling goes through inheritance (§2.5) or provider interfaces in a `.Core` library (§2.6). Impl-to-impl is permitted only within the same provider family for explicit specialization. | framework §2.1, §2.7.1 |
| G5 | Any new contract declares its kind: **replacement** or **contribution** (§2.6.1). Replacement-contract conflicts must be detected, not silently overwritten. | framework §2.6.1 |
| G6 | When composing with another feature, the §2.7.1 decision rule is applied: specialization → inheritance; heavy/external dependency → adapter; independent additive contribution → provider/handler/contributor. | framework §2.7.1 |
| G7 | No `DependsOn`-style static feature dependency declarations. Fail-fast at DI construction is the contract. | framework §2.11 |
| G8 | Persistence types: any generic constraint at the contract layer is `where TDbContext : DbContext` (Microsoft's base), never an Elsa-specific base or interface. `ElsaDbContextBase` is opt-in only (§E2.5). | framework §2.9, Elsa §E2.5 |
| G9 | Helper libraries are domain-owned, never referenced from a `.Core`, never activatable (no `IFeature`). | framework §2.4 |
| G10 | Refactor-cost test: any move that would change a NuGet identity is justified at the cost of every consumer's breaking change. Prefer the finer-grained split. | framework §2.16 |
| G11 | Duplication beats dependency: three-repetition rule applies to `<App>.Primitives` and broadly-shared utilities; inside a single domain or feature, duplication is a local tradeoff, not a violation. | framework §2.17 |
| G12 | Provider module decomposition: when a domain has only one provider, everything lives in `<App>.<Domain>.<Provider>` — no empty `<App>.<Domain>` umbrella. Replace meta NuGet packages with the specific provider sub-package. | framework §2.20 |
| G13 | Feature `name` is stable across refactors. Rename pattern is "create new feature + retire existing"; in-place rename is not supported. | framework §2.19 |
| G14 | SemVer for `.Core`: PATCH = no public/behavioural change; MINOR = compatible expansion (incl. default interface members); MAJOR = breakage (incl. feature-name change). | framework §4.2 |
| G15 | **Elsa-specific:** `Elsa.Workflows.Runtime.*` MUST NOT depend on `Elsa.Workflows.Design.*`. The dependency direction is enforced; the *seam mechanism* between the two sub-domains is deferred (see follow-up `2026-05-11_workflow_execution_seam.md`) — do not introduce specific seam types in plans until that follow-up closes. | Elsa §E2.2 |
| G16 | **Elsa-specific:** Elsa example boxes belong in the Elsa constitution; the framework constitution carries synthetic / `<App>` examples only. New worked examples for Elsa rules land in `constitution.md` §E3. | Elsa §E3 |
| G17 | Extension methods exceeding three lines have been review-walked against the §2.8 four-question framework. Bodies containing branching or business logic are promoted to interface methods or dedicated services rather than retained as extensions. | framework §2.8 |
| G18 | Persistence contract methods are split: commands mutate state without returning queryable views; queries return data without mutating. Combined command-query methods at the contract surface are a violation. | framework §2.10 |
| G19 | No module integrates the application with two external systems whose combined dependency is hidden inside an existing single-system module. Dual-integration responsibilities ship as a dedicated consumption-shape module whose package name signals the combined dependency envelope. | framework §2.14 |
| G20 | **Refactor work:** existing tests on the implementations being refactored continue to succeed across the reorganization. Test setup/dependencies/location may change; the *subject under test* and *objective* MUST be preserved. Test deletions require explicit recorded approval from at least one architect, captured in the PR description or Complexity Tracking. | framework §2.21.1 |
| G21 | **Domain events are the contribution mechanism.** Cross-feature contribution is dispatched through a domain event (declared in the domain's `.Core`, awaited end-to-end by the framework's pipeline). For sync access to contributions, the Registry + StartUp Task sub-pattern is used (event-driven population at startup, sync read afterwards). Provider/contributor interfaces (`IEnumerable<TProvider>`) are not introduced for new code; legacy uses are tracked as migration items. | framework §2.6, §2.6.1 |
| G22 | **No tight logic coupling between concrete implementations.** Cross-feature dependencies are expressed through one of the §2.6 mechanisms (domain events, replacement contracts) — not through reliance on side effects, observable behaviour, or implementation details of another concrete class. A test failure that exposes hidden side-effect coupling is the canonical signal of a violation (§2.23.4); resolution is to lift the dependency to a contract, not to reproduce the side effect in a stub. | framework §2.6 |
| G23 | **Generic dispatch is not a coupling mechanism.** `IMediator` / `IEventBus` / `INotificationSender` (and equivalents) are used only for fire-and-forget pub/sub. The moment a sender expects a specific handler to run, the contract is declared as a domain event (§2.6.1), not smuggled through a generic bus. | framework §2.6.3 |
| G24 | **Design-time vs runtime contract split.** When a contributor surface has both a design-time consumer (intellisense / validation / schema) and a runtime consumer (binding / execution), the surface splits into two contracts. They may share a `.Core` data shape; they bind to distinct consumers and are dispatched independently per §2.6.1. | framework §2.6.4 |
| G25 | **Provider-implementation dependencies.** Feature modules do not depend on concrete provider implementations unless the feature is itself provider-specific (its package name carries a provider suffix). Generic feature modules depend on the domain's `.Core` or, where persistence participation is part of the feature's role, on the provider-agnostic `.Persistence.Core`. | framework §2.20 Rule 3 |
| G26 | **Feature documentation.** Every feature ships with discoverable documentation listing at minimum (a) the domain event handlers it registers (and which events they handle), and (b) the tasks it registers (startup, recurring, scheduled). The form is application-defined; the content is not. | framework §2.22 |
| G27 | **Unit test discipline.** Every feature class has a registration test (asserts every registered service resolves). Every logic-bearing implementation class has unit tests covering all code branches. Visibility: feature classes `public` and NOT sealed (§2.5 inheritance); logic-bearing implementations `public sealed`. Skipping either test layer is forbidden. | framework §2.23 |
| G28 | **Persistence invariants are provider-agnostic.** Domain-model invariants on persisted data (immutability, audit timestamps, tenant scoping, etc.) are defined in `.Persistence.Core` (the provider-agnostic persistence surface); provider-specific enforcement mechanisms live in `.Persistence.<Provider>`. The same invariants apply across providers. | framework §2.9 |
| G29 | **Elsa-specific — runtime contract.** If an artifact is published as runnable, the runtime MUST be able to load and execute it; system-internal conditions (missing types, registry drift) may not break this contract. The Runtime sub-domain depends only on the runnable artifact + configured runtime features; design-side data is reachable via FK but not loaded for execution. Domain gates may deny execution; they may not destroy executability. | Elsa §E2.6 |
| G30 | **Elsa-specific — Elsa 3 backward compatibility is import-only.** Elsa 3 ↔ Elsa 4 interoperability flows through `Elsa3.<Domain>.Import` adapter modules: one-way one-time mapping. No dual-run, no ongoing viewmodel mapping for Elsa-3-shaped endpoints, no round-trip back to Elsa 3 shapes. | Elsa §E2.7 |

**Process.** Before Phase 0 research, the planner walks G1–G30, marks each PASS/VIOLATION/N/A, and records justifications for any VIOLATION in Complexity Tracking. After Phase 1 design, the planner re-walks G1–G30 against the concrete artefacts (data model, contracts, project tree). Most gates mark N/A for any given plan; the walk is fast — the value is in *not silently skipping* a rule.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
