# Data Model: Typed Argument Model + Type Descriptor Registry

Phase 1 output. Entities, fields, validation, and exception surfaces. Names are proposals; final names confirmed in `/speckit.tasks`.

## Value objects

### `CollectionKind` (enum) — `Elsa.Primitives`

| Value | Meaning | Closes to |
|---|---|---|
| `Single` | scalar (default) | `T` |
| `Array` | array | `T[]` |
| `List` | generic list | `List<T>` |
| `HashSet` | generic set (default equality) | `HashSet<T>` |

- Default (missing/unset) → `Single` (FR-002).

### `TypeReference` (sealed record) — `Elsa.Primitives`

| Field | Type | Notes |
|---|---|---|
| `Alias` | `string` | stable element-type identifier; non-empty (FR-001) |
| `CollectionKind` | `CollectionKind` | defaults to `Single` |

- Sole persisted representation of an authored argument's type. No namespace/assembly/version.
- Serializes natively (camelCase) via `IPayloadSerializer` — no custom converter (FR-020).
- Validation: empty/whitespace `Alias` → invalid argument definition (FR rejected at validation gate / mapper guards).

## Authored definition records (changed)

### `VariableDefinition` — `Elsa.Expressions.Core`

| Field | Before | After |
|---|---|---|
| `ReferenceKey` | `string` | unchanged (diff key, §E2.9.7) |
| `Name` | `string` | unchanged |
| type member | `TypeInformation TypeInformation` | **`TypeReference Type`** |
| `StorageDriverType` | `TypeInformation?` | **`string?`** (bare alias) |
| `Default` | `ArgumentValue?` | unchanged |

### `InputDefinition` / `OutputDefinition` — `Elsa.Activities.Design.Core`

Standalone records preserved (FR-030). Only:

| Field | Before | After |
|---|---|---|
| `Type` | `TypeInformation` | **`TypeReference`** |
| `StorageDriverType` | `TypeInformation?` | **`string?`** (bare alias) |

All other fields (`DisplayName`, `Category`, `IsBrowsable`, `IsSerializable`, `Description`, `Order`, `UiHint`, `PropertyInfo`, `UISpecifications`, `IsRequired`) unchanged.

## Descriptor / catalog (new — design-time side)

### `TypeDescriptor` (sealed record) — `Elsa.Expressions.Core`

Shared shape contributed by providers; read by both the catalog (all fields) and the registry seed (Alias + ClrType only).

| Field | Type | Notes |
|---|---|---|
| `Alias` | `string` | stable id; bare for primitives, dotted for module types |
| `ClrType` | `Type` | resolution datum (consumed by the registry seed) |
| `DisplayName` | `string` | picker label |
| `Category` | `string` | grouping (e.g. "Primitives", "Http") |
| `DefaultEditor` | `string` | open hint, e.g. `text`/`checkbox`/`number`/`date` (FR-015) |

### `IVariableTypeDescriptorProvider` (contract) — `Elsa.Expressions.Core`

- **Kind**: Source (pull). `IEnumerable<TypeDescriptor> GetDescriptors();`
- **Register**: `services.AddSingleton<IVariableTypeDescriptorProvider, MyProvider>()`.
- **Known impls (shipped)**: `DefaultVariableTypeDescriptorProvider` (framework primitives, intra-domain). Module providers (e.g. `Elsa.Http`) contribute dotted-alias descriptors cross-domain.

### `IVariableTypeDescriptorCatalog` + `VariableTypeDescriptorCatalog` (singleton) — `Elsa.Expressions`

- Constructor injects `IEnumerable<IVariableTypeDescriptorProvider>`, aggregates once at DI build (mirrors `ExpressionDescriptorRegistry`).
- Exposes the union keyed by `Alias`, and a grouped-by-`Category` view for the endpoint (FR-016).
- Duplicate alias **across providers** → `DuplicateTypeAliasException` (consistency with the registry).

## Resolution authority (changed — runtime side)

### `IWellKnownTypeRegistry` / `WellKnownTypeRegistry` — `Elsa.Serialization(.Core/.SystemText)`

- `RegisterType(Type, alias)`: now **throws** `DuplicateTypeAliasException` on a repeat alias or repeat type (was silent last-writer-wins).
- Bare (non-dotted) alias not in the reserved framework set → `ReservedAliasNamespaceException` (FR-011/FR-013).
- Resolution methods (`TryGetType`, `GetTypeOrDefault`, `GetAliasOrDefault`, …) unchanged in signature.
- Seeded once at startup by `SeedWellKnownTypesStartupTask` from the descriptor providers' `(Alias, ClrType)` (D5).

### `SeedWellKnownTypesStartupTask` — `Elsa.Serialization.SystemText`

- Reads `IVariableTypeDescriptorCatalog`/providers, registers each `(Alias, ClrType)` into the registry (single registration site for primitives).

## Mapper (changed)

### `VariableMapper` — `Elsa.Expressions`

- `Map(VariableDefinition)`: `Type.Alias` → element `Type` (registry); close by `CollectionKind`; `Variable<closed>`; resolve `StorageDriverType` alias via registry. Unknown alias → `object` + warning; alias string already preserved on the definition.
- `Map(IVariable)`: inspect value type → `(alias, CollectionKind)`; `Array`/`List<>`/`HashSet<>`/scalar.

## Exceptions (new, domain-scoped — §2.23.5)

| Exception | Package | Carries | Raised when |
|---|---|---|---|
| `DuplicateTypeAliasException` | `Elsa.Serialization.Core` | the conflicting `Alias` (+ both contributors where available) | alias/type registered twice |
| `ReservedAliasNamespaceException` | `Elsa.Serialization.Core` | the offending `Alias` | module registers a bare/reserved alias |

`JsonException` from argument (de)serialization is wrapped into a domain exception at the serialization boundary per §2.23.5.

## Compiled-Type path (parity only)

`TypeJsonConverter` (`JsonConverter<Type>`): add `HashSet<>` read (`"HashSet<elem>" → HashSet<T>`) and write (`HashSet<T> → "HashSet<elem>"`), matching the existing `[]` and `List<>` handling (FR-008). No other change; AQN fallback retained (FR-004).

## Relationships

```text
IVariableTypeDescriptorProvider*  ──aggregated by──▶  VariableTypeDescriptorCatalog ──serves──▶ descriptors/variables endpoint
        │ (shared TypeDescriptor shape)
        └──seeded (Alias,ClrType) by SeedWellKnownTypesStartupTask──▶ IWellKnownTypeRegistry ──resolves──▶ VariableMapper ──▶ Variable<T>

VariableDefinition / InputDefinition / OutputDefinition ──carry──▶ TypeReference { Alias, CollectionKind }
```
