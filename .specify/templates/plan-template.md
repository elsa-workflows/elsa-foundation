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
| G15 | **Elsa-specific:** `Elsa.Workflows.Runtime.*` MUST NOT depend on `Elsa.Workflows.Design.*`. `WorkflowExecutable` is the seam. | Elsa §E2.2 |
| G16 | **Elsa-specific:** Elsa example boxes belong in the Elsa constitution; the framework constitution carries synthetic / `<App>` examples only. New worked examples for Elsa rules land in `constitution.md` §E3. | Elsa §E3 |
| G17 | Extension methods exceeding three lines have been review-walked against the §2.8 four-question framework. Bodies containing branching or business logic are promoted to interface methods or dedicated services rather than retained as extensions. | framework §2.8 |
| G18 | Persistence contract methods are split: commands mutate state without returning queryable views; queries return data without mutating. Combined command-query methods at the contract surface are a violation. | framework §2.10 |
| G19 | No module integrates the application with two external systems whose combined dependency is hidden inside an existing single-system module. Dual-integration responsibilities ship as a dedicated consumption-shape module whose package name signals the combined dependency envelope. | framework §2.14 |
| G20 | **Refactor work:** existing tests on the implementations being refactored continue to succeed across the reorganization. Test setup/dependencies/location may change; the *subject under test* and *objective* MUST be preserved. Test deletions require explicit recorded approval from at least one architect, captured in the PR description or Complexity Tracking. | framework §2.21.1 |

**Process.** Before Phase 0 research, the planner walks G1–G20, marks each PASS/VIOLATION/N/A, and records justifications for any VIOLATION in Complexity Tracking. After Phase 1 design, the planner re-walks G1–G20 against the concrete artefacts (data model, contracts, project tree). Most gates mark N/A for any given plan; the walk is fast — the value is in *not silently skipping* a rule.

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
