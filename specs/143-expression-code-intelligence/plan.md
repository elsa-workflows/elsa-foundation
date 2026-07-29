# Implementation Plan: Expression Code Intelligence Foundation

**Branch**: `143-expression-code-intelligence` | **Date**: 2026-07-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/143-expression-code-intelligence/spec.md`

## Summary

Create an additive, versioned authoring-tooling seam. `Elsa.Workflows.Design` builds a permission- and host-policy-filtered, language-neutral context for one expression location. `Elsa.Expressions.Core` owns the per-expression-type provider and common symbol/diagnostic contracts. JavaScript and Liquid project that safe context into their own language semantics. `Elsa.Workflows.Design.Api` owns and advertises the capability-discoverable, cancellable, no-store endpoints only when the complete transport path is composed. The existing draft validation gate supplies consequential-operation validation: draft editing remains permissive, test run rejects known errors (with an explicit unavailable-validator override), and publication/promotion fail closed.

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: Existing Elsa Expressions, Workflows.Design, Workflows.Publishing, FastEndpoints, mediator, inline-event validation, and capability-discovery abstractions

**Storage**: Existing draft/design state only; no new durable tooling, cache, diagnostic, or runtime-value store

**Testing**: xUnit with `dotnet test`; existing architecture, API endpoint, design validation, expression, publishing, and integration/conformance suites

**Target Platform**: Cross-platform .NET host API consumed by Studio and other authorized clients

**Project Type**: Multi-project modular libraries plus host APIs

**Performance Goals**: Warm bounded context/validation p95 at or below 250 ms for 500 visible symbols; cancellation stops downstream work; no all-symbol materialization for normal requests

**Constraints**: Design-time metadata only; no evaluation, live runtime values, service access, source in telemetry, Studio source dependency, new persisted store, or Runtime → Design dependency; existing descriptor clients remain compatible

**Scale/Scope**: Shared contracts, Design context assembly and gate integration, additive APIs/capabilities, JavaScript/Liquid providers, and focused conformance/operation-gate tests

## Constitution Check

*GATE: Passed before Phase 0 research and re-checked after Phase 1 design.*

- **Status warning**: The Elsa and framework constitutions remain draft/provisional. This work applies their current gates without treating them as ratified doctrine.
- **Design/runtime separation (Elsa §E2.2)**: PASS. All context and semantic work stays in Design/Expressions/API. Runtime receives neither authoring context nor tooling contracts.
- **Artifact-only execution (Elsa §E2.6)**: PASS. Tooling cannot execute expressions or supply runtime facts; publication compiles its existing artifact only after validation.
- **Core seam ownership (framework §2)**: PASS. `Elsa.Expressions.Core` exposes the provider contract; `Elsa.Workflows.Design` owns workflow-specific context assembly; API projects these seams without reversing dependencies.
- **Capability and endpoint authority (Elsa glossary / existing capability pattern)**: PASS. Capability discovery is caller-permission-neutral; endpoint authorization and host-policy filtering remain authoritative.
- **Validation and resiliency**: PASS. The current `DraftValidationGate` keeps shielded read behavior; consequential write paths use the strict full-draft gate.
- **Testing and refactor preservation (Elsa §E1 / framework §2.21.1, §2.23)**: PASS. Existing descriptor and validation behavior remains covered; new provider/API/gate conformance tests are specified before implementation.
- **Post-design re-check**: PASS. No new persistence, runtime contract, cross-repository dependency, or constitutional exception is introduced.

## Project Structure

### Documentation (this feature)

```text
specs/143-expression-code-intelligence/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── expression-tooling-core.md
│   └── expression-tooling-http.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Elsa/Expressions/
├── Core/
│   ├── Contracts/
│   └── Models/
├── Api/
│   ├── Endpoints/
│   ├── Models/
│   └── Requests/
├── JavaScript/
│   ├── Services/
│   └── Models/
└── Liquid/
    ├── Services/
    └── Models/

src/Elsa/Workflows/Design/
├── Core/
│   ├── Contracts/
│   └── Services/
├── Validations/
│   ├── Core/
│   └── Validators/
└── Api/
    ├── Endpoints/Authoring/
    ├── Handlers/
    ├── Models/
    └── Requests/

src/Elsa/Workflows/Publishing/Api/
├── Handlers/
├── Requests/
└── Endpoints/

tests/Elsa/
├── Expressions/{Tests,Api/Tests,JavaScript/Tests,Liquid/Tests}/
├── Workflows/Design/{Tests,Api/Tests}/
└── Workflows/Publishing/Api/Tests/
```

**Structure Decision**: Extend existing module, registry, capability, validation, and publishing seams in place. `Elsa.Expressions.Core` carries only the reusable expression tooling contract. Workflow graph facts and policy filtering stay owned by `Elsa.Workflows.Design`; JavaScript and Liquid remain provider modules. No Studio project or package is referenced.

## Complexity Tracking

No constitution violations require tracking.
