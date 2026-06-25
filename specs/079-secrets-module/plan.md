# Implementation Plan: Secrets Module

**Branch**: `079-secrets-module` | **Date**: 2026-06-24 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/079-secrets-module/spec.md`

## Summary

Add a first-class Secrets module across Foundation and Foundation Studio. The backend introduces provider-neutral secret contracts, lifecycle management, safe metadata APIs, expression/runtime resolution, and Groundwork-backed durable storage. Studio adds a React module with a security feature area, secrets management pages, and a reusable secret-picker property editor so sensitive workflow inputs can store references instead of raw values.

The first implementation ports the intended behavior from upstream `elsa-core`/`elsa-studio`, but adapts it to Foundation architecture: CShells shell features, mediator-backed FastEndpoints, Groundwork document persistence, React/Vite Studio modules, and Foundation's expression extension points.

## Technical Context

**Language/Version**: C# / .NET `net10.0`; TypeScript / React 19 in Foundation Studio modules

**Primary Dependencies**: CShells `IShellFeature`, Elsa FastEndpoints wrappers, Elsa mediator handlers, Elsa expression descriptors/handlers, Groundwork document store, Microsoft configuration/options, React/Vite/Vitest, Studio SDK contribution registries

**Storage**: In-memory repository for tests/development; Groundwork document store for durable secret aggregates; application configuration for deployment-owned configuration-backed values. No EF Core provider package is planned for this slice.

**Testing**: xUnit backend unit/API/persistence/registration tests; Vitest + jsdom Studio module tests; quickstart validation against local server/studio composition when implementation is complete

**Target Platform**: Elsa Server combined development host and `elsa-foundation-studio` modular React shell

**Project Type**: Cross-repository backend domain/API/persistence module plus frontend Studio module

**Performance Goals**: Metadata list/picker queries should remain responsive for at least 10,000 secrets when backed by declared indexes. Runtime resolution should be one indexed aggregate lookup plus store read under normal local provider conditions.

**Constraints**: Secret values must not appear in metadata responses, picker responses, saved workflow definitions, logs, audit records, or Studio state beyond create/rotate form input. Runtime resolution must not depend on workflow design data. Configuration-backed secrets write lookup metadata only. External vault providers and full encrypted value export/import are deferred.

**Scale/Scope**: One backend Secrets domain with Core, implementation, API, and Groundwork persistence projects; one backend test project; one Studio Secrets module with client tests and .NET manifest registration tests.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Status | Note |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Contracts/models live in `Elsa.Secrets.Core`; implementation, API, and persistence live in separate packages. |
| Framework §2.2 domain naming | PASS | Uses `Elsa.Secrets.*`, not layer-marker namespaces. |
| Framework §2.20 provider decomposition | PASS | Groundwork persistence is an optional provider/bridge package; core contracts do not depend on Groundwork. |
| Framework §2.23 unit tests | PASS with work required | Feature registration, implementation branch behavior, API mapping, persistence, and Studio registration tests are planned. |
| Elsa §E2.2 Workflows Design/Runtime split | PASS | Secrets integrates through expressions and runtime input materialization; runtime does not reference design packages. Studio joins authoring UX with backend metadata at application layer. |
| Elsa §E2.6 artifact-only runtime | PASS | Workflow definitions store secret references; runtime resolves through Secrets services at point of use without reading design source. |
| Source-of-truth layering | PASS | Durable work is captured in `specs/079-secrets-module`; no glossary or constitution changes are required for the first implementation. |

Initial gate status: **PASS**. No violations requiring Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/079-secrets-module/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── backend-api.md
│   ├── runtime-contract.md
│   └── studio-contract.md
└── checklists/
    └── requirements.md
```

### Source Code (repository roots)

```text
elsa-foundation/
├── src/Elsa/Secrets/Core/
│   ├── Contracts/
│   ├── Models/
│   ├── Events/
│   └── Elsa.Secrets.Core.csproj
├── src/Elsa/Secrets/
│   ├── Extensions/
│   ├── Expressions/
│   ├── Services/
│   ├── Stores/
│   ├── Types/
│   ├── Features/
│   └── Elsa.Secrets.csproj
├── src/Elsa/Secrets/Api/
│   ├── Constants/
│   ├── Endpoints/Secrets/
│   ├── Requests/
│   ├── Features/
│   └── Elsa.Secrets.Api.csproj
├── src/Elsa/Secrets/Persistence/Groundwork/
│   ├── DependencyInjection/
│   ├── Stores/
│   ├── SecretsStorageManifest.cs
│   ├── SecretsGroundworkPersistenceFeature.cs
│   └── Elsa.Secrets.Persistence.Groundwork.csproj
└── tests/Elsa/Secrets/Tests/
    └── Elsa.Secrets.Tests.csproj

elsa-foundation-studio/
├── src/Elsa.Studio.Secrets/
│   ├── Client/
│   │   ├── package.json
│   │   ├── src/module.tsx
│   │   ├── src/secretsApi.ts
│   │   ├── src/secretTypes.ts
│   │   ├── src/styles.css
│   │   ├── src/__tests__/
│   │   └── vite.config.ts
│   ├── Handlers/ContributeSecretsStudioModule.cs
│   ├── SecretsStudioFeature.cs
│   ├── SecretsStudioServiceCollectionExtensions.cs
│   └── Elsa.Studio.Secrets.csproj
└── tests/Elsa.Studio.Tests/
```

**Structure Decision**: Keep all retrievable-secret domain contracts in `Elsa.Secrets.Core`, not Identity. Identity credential hashes remain separate because they are intentionally non-retrievable. Use Groundwork as the first durable provider because Secrets are low-risk document aggregates with simple declared indexes and because this repo already uses Groundwork for provider-neutral storage validation. Deliver Studio in the sibling React module system rather than porting the upstream Blazor UI literally.

## Phase 0 Output

See [research.md](research.md).

Resolved decisions:

- Use a new `Elsa.Secrets` domain instead of extending Identity.
- Use immutable normalized technical names as the reference key.
- Resolve references to latest active versions only.
- Include no cleartext reveal path after create/rotate submission.
- Use an Elsa-managed encrypted store and a configuration-backed lookup store in the first slice.
- Use Groundwork documents for durable persistence.
- Represent permission and audit contracts now, with host authorization enforcement allowed to remain permissive in unsecured local development.

## Phase 1 Output

- [data-model.md](data-model.md)
- [contracts/runtime-contract.md](contracts/runtime-contract.md)
- [contracts/backend-api.md](contracts/backend-api.md)
- [contracts/studio-contract.md](contracts/studio-contract.md)
- [quickstart.md](quickstart.md)

## Post-Design Constitution Re-Check

| Gate | Status | Post-design evidence |
|---|---|---|
| Framework §2.1 three-layer separation | PASS | Data model and contracts split Core, implementation, API, persistence, and Studio concerns. |
| Framework §2.20 provider decomposition | PASS | Groundwork persistence implements `ISecretRepository`; Core remains provider-neutral. |
| Framework §2.23 unit tests | PASS with work required | Tasks must include direct service tests, feature registration tests, endpoint tests, persistence tests, and Studio tests. |
| Elsa §E2.2 Workflows Design/Runtime split | PASS | Studio authoring uses picker references; runtime expression handler uses `ISecretResolver` only. |
| Elsa §E2.6 artifact-only runtime | PASS | Workflow artifacts store `SecretReference` values; runtime resolution uses configured Secrets features. |

Post-design gate status: **PASS**. No justified violations.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| None | N/A | N/A |
