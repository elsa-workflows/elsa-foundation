# Research & Decisions: Typed Argument Model + Type Descriptor Registry

Phase 0 output. Each decision resolves a design choice grounded in the current codebase.

## D1 — Type reference shape

**Decision**: Introduce `TypeReference { string Alias, CollectionKind CollectionKind }` as a plain `sealed record`. It replaces `TypeInformation` as the **element type carrier** on authored definitions. Default `CollectionKind` is `Single`.

**Rationale**: The alias is a stable string; the kind is an enum. Both serialize natively (camelCase) through `IPayloadSerializer` — no custom converter needed for the reference itself, so the serialization rule is satisfied by plain data. `TypeInformation` is flat (`TypeName`/`Namespace`/`AssemblyName`/`AssemblyVersion`) and cannot represent a generic element type, which is exactly why a separate `CollectionKind` field (not a composed type string) is the right model.

**Alternatives rejected**: (a) Keep `TypeInformation` and encode collections in `TypeName` → flat record can't hold generic args; (b) one combined type-alias string like `"List<String>"` → pushes parsing into every consumer and couples element + kind. The existing `TypeJsonConverter` does string-encode `[]`/`List<>` for the **compiled-Type path**; we deliberately do **not** reuse that for authored definitions (see D8).

## D2 — Placement of `TypeReference` and `CollectionKind`

**Decision**: Place both in `Elsa.Primitives` (`src/Elsa/Primitives/Primitives/Models/`), beside `TypeInformation`.

**Rationale**: They are domainless value types with zero external dependencies, consumed by both `Elsa.Expressions.Core` (VariableDefinition) and `Elsa.Activities.Design.Core` (Input/OutputDefinition). `TypeInformation` already sets the precedent for "type-model primitive lives in `Elsa.Primitives`" (§E2.3 charter: domainless building blocks, zero deps). A lower common package avoids a cross-domain reference.

**Alternative considered**: `Elsa.Serialization.Core` (where `IWellKnownTypeRegistry` lives), since "alias" is a resolution concept. Rejected because the value object has no dependency on the registry, and Activities.Design.Core would then need a Serialization.Core reference it may not otherwise want. Flag for architect confirmation if Primitives admission is contested.

## D3 — Scope of "unification": the type reference, not the records

**Decision**: Unify only the **type member**. Replace:
- `VariableDefinition.TypeInformation` → `TypeReference Type`
- `InputDefinition.Type` (`TypeInformation`) → `TypeReference Type`
- `OutputDefinition.Type` (`TypeInformation`) → `TypeReference Type`

Leave `InputDefinition`/`OutputDefinition` as standalone records with their existing fields (DisplayName, Category, IsBrowsable, …).

**Rationale**: A prior decision (FR-030, recorded in the records' XML docs) **deliberately** made `InputDefinition`/`OutputDefinition` standalone sealed records duplicating `ArgumentDefinition` "rather than inheriting, keeping the input signature clear and decoupled." Merging the three records would reverse that ratified choice. The spec's "uniform argument-descriptor shape" is satisfied at the level that matters for this feature — the **type reference is identical across all three** so collection-ness round-trips uniformly. The studio-side three-way inconsistency (the original brainstorm motivation) is a **Phase 2** concern.

## D4 — `StorageDriverType` must also drop `TypeInformation`

**Decision**: Convert `StorageDriverType` on all three records from `TypeInformation?` to `string?` (a bare alias; always `Single`, no collection kind).

**Rationale**: FR-003 requires removing the decomposed representation from the **authored-definition path**. `StorageDriverType` is a `TypeInformation` living on those very records, so leaving it would violate FR-003. Storage drivers are also registry-resolvable types selected from the existing `descriptors/storage-drivers` list, so an alias is the natural representation. `VariableMapper` already resolves storage driver via `Type.GetType(fullName)`; it switches to registry alias resolution.

## D5 — Registry vs catalog split, single-sourced (pattern 6)

**Decision**: Two contracts sharing one shape record.
- **Runtime**: `IWellKnownTypeRegistry` stays the alias↔CLR-type authority.
- **Design-time**: `IVariableTypeDescriptorProvider` (`IEnumerable<TypeDescriptor> GetDescriptors()`) contributed via DI; aggregated by a singleton `VariableTypeDescriptorCatalog` whose constructor injects `IEnumerable<IVariableTypeDescriptorProvider>` — mirroring `ExpressionDescriptorRegistry(IEnumerable<IExpressionDescriptorProvider>)`.
- **Bridge**: a startup task seeds the resolution registry from the same providers, reading only `(Alias, ClrType)` from each `TypeDescriptor`, calling `RegisterType` (which now throws on duplicate).

**Rationale**: This is the textbook §2.6.4 / pattern-6 case: a design-time consumer (picker) and a runtime consumer (binding) of the same concept, "two contracts, may share a shape record." Sharing the provider output as the single source of truth prevents the resolution map and the picker list from drifting. The runtime registry depends on the shared shape record's data, not on picker logic.

**Trade-off recorded in plan Complexity Tracking**: the alternative (separate hardcoded seed list + separate descriptor list) duplicates the canonical primitive set and invites drift; rejected.

## D6 — Descriptors endpoint

**Decision**: Add a `descriptors/variables` endpoint returning the aggregated catalog grouped by `category`, each entry `{ alias, displayName, category, defaultEditor }`. Model it on `Secrets/Descriptors.cs` (`ElsaEndpointWithoutRequest`) and register it alongside the existing `descriptors/activities` mapping under the workflow-management API.

**Rationale**: The grounding pass confirmed **no `descriptors/variables` endpoint exists today** — the studio falls back to a hardcoded well-known list. Creating it lights up the real, module-contributed dropdown and the type-aware default editor. The existing `Secrets` descriptors endpoint (registry → response) is the precedent.

**Open item for `/speckit.tasks`**: confirm the exact host project/route group (`Activities.Design.Api` vs the `ElsaWorkflowManagementApi` group that maps `descriptors/activities`). The studio expects `/_elsa/workflow-management/descriptors/variables`.

## D7 — Registry hardening (fail-fast + reserved namespace + graceful unknown)

**Decision**:
- `WellKnownTypeRegistry.RegisterType` **throws `DuplicateTypeAliasException`** when an alias (or the same CLR type) is already registered, instead of silently overwriting. The existing nullable auto-registration only adds the `?` alias if absent (no self-collision).
- Registering a **bare** (non-dotted) alias not on the framework-reserved primitive set throws `ReservedAliasNamespaceException`. Framework primitives register through the trusted seed path; module providers must use dotted aliases.
- **Graceful unknown**: an unresolvable alias is preserved verbatim in the persisted definition (it is just a string — automatic). `VariableMapper` keeps resolving unknown aliases to `typeof(object)` with a warning for runtime materialization, but the **definition round-trips the original alias unchanged** (FR-018 / SC-005). "Disabled/unknown entry" surfacing in the picker is a Phase-2 concern.

**Rationale**: `RegisterType` currently does `_typeAliasDictionary[type] = alias; _aliasTypeDictionary[alias] = type;` (last-writer-wins) — the silent-overwrite this feature forbids. Throwing turns a latent wrong-type-resolution bug into a startup failure (SC-004). Exceptions live in `Elsa.Serialization.Core` as domain exceptions (§2.23.5).

**Note**: making `RegisterType` throw means the seeding bridge must register each alias exactly once; the seed is the single registration site for primitives, so existing scattered `RegisterType` calls (if any) are audited during `/speckit.tasks`.

## D8 — Compiled-Type path keeps alias-or-AQN; add `HashSet`

**Decision**: Leave `TypeJsonConverter` (the `JsonConverter<Type>` for activity property signatures) on its alias-or-assembly-qualified-name behavior, and **add `HashSet<>` read/write** to it for parity with `[]` and `List<>` (FR-008).

**Rationale**: That converter serializes arbitrary compiled CLR types that no human curated; it legitimately needs the AQN fallback (FR-004). It is a separate path from the authored `TypeReference` and must not be migrated to alias-only. The missing `HashSet` case is the only functional gap there.

## D9 — `VariableMapper` compose/decompose

**Decision**:
- `Map(VariableDefinition)`: resolve `Type.Alias` via the registry → element `Type`; close by `CollectionKind` (`Single→T`, `Array→T.MakeArrayType()`, `List→List<T>`, `HashSet→HashSet<T>`); build `Variable<closed>`.
- `Map(IVariable)`: inspect the variable's value type — if `IsArray` → `(elementAlias, Array)`; if closed `List<>`/`HashSet<>` → `(elementAlias, List|HashSet)`; else `(alias, Single)`. Alias via `GetAliasOrDefault`.

**Rationale**: Centralizes the (alias, kind) ↔ closed-type composition in the one mapper, which already owns `VariableDefinition ↔ Variable<T>`. The runtime `ObjectConverter` already special-cases array/collection conversion, so no new runtime conversion logic is needed (spec assumption).

## D10 — `TypeDescriptor` vs existing `VariableDescriptor`

**Decision**: Introduce `TypeDescriptor { string Alias, Type ClrType, string DisplayName, string Category, string DefaultEditor }` as the shared provider shape. Relate the existing `VariableDescriptor(Type, Category, Description)` to it (either fold its use sites onto `TypeDescriptor` or keep `VariableDescriptor` as a derived projection). Final reconciliation decided in `/speckit.tasks` after auditing `VariableDescriptor` use sites.

**Rationale**: The existing `VariableDescriptor` carries `Type` + `Category` but lacks `Alias`, `DisplayName`, and `DefaultEditor`. The new descriptor must be alias-keyed and presentation-complete to satisfy FR-015/FR-016. Avoid a premature destructive rename until use sites are catalogued.

## Resolved unknowns

All Technical Context items are resolved; no `NEEDS CLARIFICATION` remain. The two items deferred to `/speckit.tasks` (endpoint host in D6; `VariableDescriptor` reconciliation in D10) are implementation-sequencing details, not design unknowns.
