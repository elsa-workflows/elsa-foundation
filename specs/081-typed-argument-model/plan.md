# Implementation Plan: Typed Argument Model + Type Descriptor Registry (Backend)

**Branch**: `081-typed-argument-model` | **Date**: 2026-06-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/081-typed-argument-model/spec.md`

## Summary

Replace the decomposed `TypeInformation` carried by workflow `VariableDefinition`, `InputDefinition`, and `OutputDefinition` with a single rename-proof **`TypeReference { Alias, CollectionKind }`** value object. Resolution stays in the runtime authority (`IWellKnownTypeRegistry`, hardened to fail-fast on duplicate/reserved-namespace registration); a **design-time type-descriptor catalog** (a DI collection of providers, mirroring the existing `IExpressionDescriptorProvider` → `ExpressionDescriptorRegistry` pattern) supplies the picker metadata and feeds a new `descriptors/variables` endpoint. The two sides are split per **§2.6.4 / pattern 6 (design-time vs runtime contract split, sharing a shape record)**: providers contribute a shared `TypeDescriptor` shape; a startup bridge seeds the resolution registry from the same providers (alias→CLR type only), so the two services share data without the runtime depending on the picker.

The change is scoped to the **type reference**, not the record shapes — `InputDefinition`/`OutputDefinition` stay standalone per the prior FR-030 decision; only their type members change.

## Technical Context

**Language/Version**: C# / .NET 8 (assembly identities resolve to 8.0.0.0).

**Primary Dependencies**: `System.Text.Json` (custom `JsonConverter`s via `IPayloadSerializer`), `Microsoft.Extensions.DependencyInjection`, FastEndpoints (`ElsaEndpointWithoutRequest`) for the descriptors endpoint.

**Storage**: `WorkflowDefinitionState` persisted as `StateSource` shadow JSON on `WorkflowDefinitionVersion`/`Draft` (EF Core). DB is wiped — no migration.

**Testing**: xUnit with built-in assertions (these test projects do **not** use FluentAssertions). Pattern: `{Subject}Tests`, methods `{Method}_{Scenario}_{Expected}`, direct instantiation with stubs.

**Target Platform**: Modular-monolith backend (`elsa-foundation`), consumed by the Studio (Phase 2, separate repo).

**Project Type**: Backend domain libraries + API endpoint.

**Performance Goals**: N/A (authoring/serialization path; not a hot loop). Registry/catalog are startup-snapshot singletons.

**Constraints**: Breaking changes allowed; no backward compatibility. Authored-definition JSON MUST carry only `{ alias, collectionKind }` for argument types (and a bare alias for storage driver) — zero namespace/assembly/version.

**Scale/Scope**: ~9 source files changed + ~6 new types + 1 endpoint + 2 test projects + extension-point/glossary/map follow-through.

## Constitution Check

*GATE: must pass before Phase 0 and re-checked after Phase 1.*

| Gate | Applies how | Status |
|---|---|---|
| **§2.6.4 / pattern 6** — design-time vs runtime contract split | Resolution registry (runtime) and descriptor catalog (design-time) are two contracts sharing the `TypeDescriptor` shape record. | ✅ Designed to it |
| **§2.6.1 / pattern 3a** — Registry + aggregating providers | `IVariableTypeDescriptorProvider` collection aggregated by a singleton catalog ctor, mirroring `ExpressionDescriptorRegistry`. | ✅ |
| **§2.6.2 / pattern 5** — replacement contract | `IWellKnownTypeRegistry` is the single resolution authority. | ✅ existing |
| **§2.3 / §E2.3** — Primitives charter (zero-dep, domainless) | `TypeReference` + `CollectionKind` placed beside `TypeInformation` in `Elsa.Primitives` (domainless, no new deps). | ✅ (see research D2) |
| **§2.5.1** — scoped-by-default; singletons for registries/immutable lookups | Catalog + registry are singletons (startup snapshots); providers singletons. | ✅ |
| **§2.23.1** — feature registration test | New services registered in `ExpressionsFeature`/`SerializationFeature`/API feature get registration tests. | ⬜ planned |
| **§2.23.2** — branch-covered implementation tests | Mapper (4 kinds × resolve/decompose), registry (duplicate throw, reserved-namespace, unknown), catalog aggregation, converter parity. | ⬜ planned |
| **§2.23.5** — infra exceptions wrapped at boundary | Duplicate/reserved registration raise **domain** exceptions (`DuplicateTypeAliasException`, `ReservedAliasNamespaceException`) in `.Core`; `JsonException` from argument (de)serialization wrapped. | ⬜ planned |
| **Serialization rule** (`docs/serialization.md`) | `TypeReference` is plain data (camelCase via `IPayloadSerializer`); existing custom converters are the sanctioned exception; no ad-hoc `JsonSerializer`. | ✅ |
| **§E2.9.1** — `WorkflowDefinitionState` scope | Variables/Inputs/Outputs are in-scope authored content; type-ref change is legitimate. | ✅ |
| **§E2.9.7** — draft mutation diff by `ReferenceKey` | `ReferenceKey` preserved; a type/kind change is a per-variable diff emitting the existing mutation event. | ✅ preserve |
| **Extension-point catalog** (§2.22) | New `IVariableTypeDescriptorProvider` surface cataloged in owning `EXTENSION_POINTS.md`. | ⬜ follow-through |

No violations requiring Complexity Tracking. One deliberate shared-shape-record choice (providers feed both catalog and registry seed) is explicitly sanctioned by §2.6.4 and recorded in research D5.

## Project Structure

### Documentation (this feature)

```text
specs/081-typed-argument-model/
├── plan.md              # This file
├── research.md          # Phase 0 — decisions (placement, seeding, scope, registry hardening)
├── data-model.md        # Phase 1 — entities, fields, validation, exceptions
├── contracts/
│   └── wire-contract.md # Phase 1 — JSON shapes Phase 2 consumes (type ref + descriptors payload)
├── quickstart.md        # Phase 1 — how to verify end to end
└── checklists/requirements.md
```

### Source Code (repository root, `elsa-foundation`)

```text
src/Elsa/Primitives/Primitives/Models/
├── TypeReference.cs            # NEW value object { Alias, CollectionKind }
├── CollectionKind.cs           # NEW enum Single|Array|List|HashSet
└── TypeInformation.cs          # KEPT — used only by the compiled-Type path now

src/Elsa/Expressions/Core/Models/
├── VariableDefinition.cs       # CHANGE: TypeInformation → TypeReference; StorageDriverType → string? alias
├── VariableDescriptor.cs       # EXTEND/relate to TypeDescriptor (shared shape)
└── TypeDescriptor.cs           # NEW shared shape record { Alias, ClrType, DisplayName, Category, DefaultEditor }

src/Elsa/Expressions/Core/Contracts/
├── IVariableTypeDescriptorProvider.cs   # NEW design-time contract (Source pattern)
└── IVariableTypeDescriptorCatalog.cs     # NEW aggregating registry contract

src/Elsa/Expressions/Services/
├── VariableTypeDescriptorCatalog.cs      # NEW singleton aggregator (mirrors ExpressionDescriptorRegistry)
├── DefaultVariableTypeDescriptorProvider.cs  # NEW framework primitives provider
└── VariableMapper.cs                      # CHANGE: compose/decompose (alias, kind) ↔ closed CLR type

src/Elsa/Activities/Design/Core/Models/
├── InputDefinition.cs          # CHANGE: TypeInformation Type → TypeReference Type; StorageDriverType → string?
└── OutputDefinition.cs         # CHANGE: same

src/Elsa/Serialization/Core/
├── IWellKnownTypeRegistry.cs   # CHANGE: document fail-fast contract; add seeding/contribution hook
├── DuplicateTypeAliasException.cs        # NEW domain exception
└── ReservedAliasNamespaceException.cs    # NEW domain exception

src/Elsa/Serialization/SystemText/
├── Services/WellKnownTypeRegistry.cs     # CHANGE: throw on duplicate; reserved-namespace guard
├── JsonConverters/TypeJsonConverter.cs   # CHANGE: add HashSet<> read/write parity (FR-008)
├── Startup/SeedWellKnownTypesStartupTask.cs  # NEW bridge: seed registry from descriptor providers
└── SerializationFeature.cs               # CHANGE: register seeding bridge

src/Elsa/Activities/Design/Api/Endpoints/Descriptors/   (or workflow-management api host)
└── Variables.cs                # NEW descriptors/variables endpoint (mirrors Secrets/Descriptors)

tests/Elsa/.../Tests/Unit/
├── VariableMapperTests.cs               # NEW (12 combinations + unknown alias)
├── WellKnownTypeRegistryTests.cs        # NEW (duplicate throw, reserved namespace, resolve)
├── VariableTypeDescriptorCatalogTests.cs # NEW (aggregation, grouping)
├── TypeJsonConverterTests.cs            # NEW/extend (HashSet parity)
└── *FeatureRegistrationTests.cs         # NEW/extend (new services resolve)
```

**Structure Decision**: Single backend repo; changes land in the existing `Elsa.Primitives`, `Elsa.Expressions(.Core)`, `Elsa.Activities.Design(.Core/.Api)`, and `Elsa.Serialization(.Core/.SystemText)` packages. Exact endpoint host (workflow-management API app vs `Activities.Design.Api`) confirmed in research D6.

## Complexity Tracking

> No constitution violations require justification. The one notable choice — a shared `TypeDescriptor` provider feeding both the design-time catalog and the runtime registry seed — is sanctioned by §2.6.4 ("two consumers, two contracts, may share a shape record") and documented in research.md (D5). It is recorded here only for visibility, not as a violation.

| Decision | Why | Rejected alternative |
|---|---|---|
| Providers feed both catalog (design) and registry seed (runtime) via shared `TypeDescriptor` | Single source of truth for alias↔type↔presentation; prevents drift | Fully separate seed list + descriptor list → two lists drift out of sync |
