# Feature Specification: Typed Argument Model + Type Descriptor Registry (Backend)

**Feature Branch**: `081-typed-argument-model`

**Created**: 2026-06-30

**Status**: Draft

**Input**: Replace the brittle, inconsistent argument type model for workflow Variables, Inputs, and Outputs with one stable, uniform type reference, backed by a declarative module-contributed type-descriptor catalog. Backend (Phase 1) of a two-phase effort; the studio is Phase 2.

## Context & Problem *(informative)*

Workflow authors declare **Variables**, **Inputs**, and **Outputs** on a workflow. Each carries a *type* (e.g. "a string", "a list of strings"). Today this is modeled three incompatible ways and is brittle:

- The persisted type stores a fully-decomposed CLR identity (type name, namespace, assembly name, **assembly version**). A package bump, namespace move, or type rename can break resolution of already-saved workflows.
- Variables, Inputs, and Outputs use three different internal shapes; only Inputs/Outputs carry a collection flag, and **that flag is silently dropped** — the studio sends "is array" but the backend definition records have no field for it, so collection-ness does not round-trip for any of the three.
- There is no declarative way for a module to contribute the set of *selectable* types (with display metadata) shown to authors; the selectable set is assembled imperatively.

This feature replaces that with a single, rename-proof type reference and a declarative type catalog, so collection-ness round-trips uniformly and saved workflows survive refactors of the underlying types.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author a typed, collection-aware argument that round-trips (Priority: P1)

A workflow author declares a Variable, Input, or Output and chooses both an element type (e.g. "String") and a shape (e.g. "Array"). After the workflow is saved and reloaded, the same element type and shape come back, and at execution the argument is materialized as the correct concrete type (e.g. `string[]`).

**Why this priority**: This is the core fix. Without it, collection selection is a silent no-op and the three argument kinds stay inconsistent. It delivers a viable MVP on its own: uniform, round-tripping typed arguments.

**Independent Test**: Create a workflow with one Variable, one Input, and one Output, each set to a chosen element type and each of the four shapes (Single, Array, List, HashSet); save, reload, and confirm the type reference is unchanged; resolve each to a CLR type and confirm `Single→T`, `Array→T[]`, `List→List<T>`, `HashSet→HashSet<T>`.

**Acceptance Scenarios**:

1. **Given** a Variable with element type alias `String` and shape `Array`, **When** the definition is serialized and deserialized, **Then** the round-tripped type reference equals `{ alias: "String", collectionKind: "Array" }` and nothing else (no namespace/assembly/version).
2. **Given** an Input and an Output each set to shape `List` of `Int32`, **When** the workflow is saved and reloaded, **Then** both retain `collectionKind: "List"` — i.e. collection-ness round-trips for all three argument kinds, not only Variables.
3. **Given** any of the four `collectionKind` values for element alias `String`, **When** the argument is resolved to a runtime type, **Then** the resolved CLR type is `string`, `string[]`, `List<string>`, or `HashSet<string>` respectively.
4. **Given** an argument with `collectionKind: "Single"`, **When** resolved, **Then** the type is the bare element type with no collection wrapper.

---

### User Story 2 - A module contributes selectable types with display metadata (Priority: P2)

A module developer registers one or more types that workflow authors may select for arguments, each with a stable alias and presentation metadata (display name, category, and a hint for how its default value should be edited). The set of selectable types served to the designer reflects every module's contributions, grouped for display, and includes the hint needed to render a type-appropriate default-value editor.

**Why this priority**: This is the contract Phase 2 (studio) consumes. It unblocks the designer's type dropdown and type-aware default editor, but the system is still correct and usable (via P1) without it.

**Independent Test**: Register two descriptor providers contributing distinct aliases; query the descriptors surface and confirm the union of contributions is returned, each entry carrying `{ alias, displayName, category, defaultEditor }`, grouped by category.

**Acceptance Scenarios**:

1. **Given** the framework's built-in primitive descriptors and a module's contributed descriptors, **When** the descriptors surface is queried, **Then** the response is the union of both, keyed by alias, each with display name, category, and default-editor hint.
2. **Given** two providers, **When** their contributions are aggregated, **Then** entries can be grouped by `category` for presentation.
3. **Given** a contributed descriptor for alias `Elsa.Http.HttpRequest`, **When** the descriptors surface is queried, **Then** that alias is selectable and resolvable to its CLR type via the resolution authority.

---

### User Story 3 - Rename-proof type identity with fail-fast registration (Priority: P3)

A maintainer renames or moves the CLR type behind an alias. Workflows saved against that alias continue to load and resolve, because only the frozen alias was persisted. If two registrations ever claim the same alias, the application fails fast at startup rather than silently resolving to the wrong type. If a saved workflow references an alias no longer present (e.g. a module was removed), the workflow still loads and the unknown alias is surfaced rather than crashing the load.

**Why this priority**: Robustness and operability. The model works without these guarantees in the happy path, but they prevent silent corruption and hard failures over the software's lifetime.

**Independent Test**: (a) Register an alias, rename its CLR type, confirm previously-saved references still resolve. (b) Register the same alias twice and confirm startup throws. (c) Deserialize a definition referencing an unregistered alias and confirm it round-trips the alias string and is marked unresolved instead of throwing.

**Acceptance Scenarios**:

1. **Given** an alias whose CLR type is renamed, **When** a workflow saved against that alias is loaded, **Then** resolution still succeeds because the alias is unchanged.
2. **Given** two registrations for the same alias, **When** the application starts, **Then** startup fails with an error naming the conflicting alias.
3. **Given** a persisted argument referencing an unregistered alias, **When** the definition is loaded, **Then** the load succeeds, the alias string is preserved on save, and the argument is flagged as unresolved/unknown.
4. **Given** a module attempts to register a bare (non-dotted) alias that collides with a framework-reserved primitive alias, **When** the application starts, **Then** startup fails.

---

### Edge Cases

- **Unknown alias on load**: preserved and flagged unresolved; never throws; re-saving does not lose the original alias.
- **Collection of an unknown alias**: `collectionKind` is retained alongside the unresolved alias; resolution is deferred/blocked but the definition is intact.
- **Duplicate alias registration**: startup throws (fail fast), naming the alias and ideally both contributors.
- **Reserved-namespace collision**: a module registering a bare alias that collides with a framework primitive throws at startup.
- **Empty/missing collectionKind**: treated as `Single` (the default shape).
- **Empty/missing alias**: rejected as an invalid argument definition.
- **HashSet element semantics**: a `HashSet<T>` uses default element equality; custom comparers are out of scope.
- **Internal compiled-type path**: activity property signatures (arbitrary CLR types not authored by users) continue to serialize via the existing alias-or-assembly-qualified-name fallback; this is explicitly *not* changed to alias-only.

## Requirements *(mandatory)*

### Functional Requirements

#### Type reference shape

- **FR-001**: A workflow Variable, Input, and Output MUST persist its type as exactly two fields: a string `alias` (stable element-type identifier) and a `collectionKind` enumeration value. No namespace, assembly name, or assembly version may appear in the persisted argument definition.
- **FR-002**: `collectionKind` MUST support exactly these values: `Single`, `Array`, `List`, `HashSet`. A missing value MUST be treated as `Single`.
- **FR-003**: The decomposed type representation (type name + namespace + assembly name + assembly version) MUST be removed from the authored-definition serialization path for Variables, Inputs, and Outputs.
- **FR-004**: The decomposed/assembly-qualified representation MAY remain ONLY on the internal compiled-type serialization path used for activity property signatures (types not authored through the argument editor). This feature MUST NOT migrate that path to alias-only.

#### Uniform argument model

- **FR-005**: Variables, Inputs, and Outputs MUST converge on one uniform argument-descriptor shape so that element type and collection kind round-trip identically for all three.
- **FR-006**: Collection-ness MUST round-trip end to end for all three argument kinds (closing the current gap where the collection flag is silently dropped for Inputs/Outputs and absent for Variables).

#### Resolution

- **FR-007**: The system MUST resolve `(alias, collectionKind)` to a concrete runtime type as: `Single→T`, `Array→T[]`, `List→List<T>`, `HashSet→HashSet<T>`, where `T` is the CLR type the alias resolves to.
- **FR-008**: The system MUST support `HashSet` resolution and serialization (the current implementation handles array and list shapes but not hash set).
- **FR-009**: There MUST be a single runtime resolution authority mapping alias ↔ CLR type. The presentation catalog (FR-014…FR-017) MUST be a separate concern; runtime resolution MUST NOT depend on the designer-facing catalog.

#### Alias contract & registration

- **FR-010**: An alias MUST be a frozen contract: the CLR type behind an alias may be renamed or moved without breaking previously-persisted references; the alias itself MUST NOT be renamed as part of such a refactor.
- **FR-011**: Framework primitive types MUST use bare aliases (e.g. `String`, `Int32`, `Boolean`, `DateTime`, `Guid`, `Object`). The bare (non-dotted) alias namespace MUST be reserved for the framework.
- **FR-012**: Module-contributed types MUST use dotted/reverse-DNS aliases (e.g. `Elsa.Http.HttpRequest`).
- **FR-013**: Duplicate alias registration MUST fail fast at application startup with an error identifying the conflicting alias. A module registering a bare alias that collides with a reserved framework alias MUST also fail fast.

#### Type descriptor catalog (extension point)

- **FR-014**: The system MUST expose a declarative, module-contributed type-descriptor catalog as a DI collection of descriptor providers, distinct from the resolution authority but keyed by the same alias.
- **FR-015**: Each type descriptor MUST carry at least: `alias`, `displayName`, `category`, and `defaultEditor` (a hint identifying how the argument's default value should be edited).
- **FR-016**: The descriptors surface that the designer consumes MUST be enriched to return the aggregated catalog (union of all providers), grouped by `category`, so the designer can render a grouped type picker and a type-aware default-value editor.
- **FR-017**: The new descriptor-provider contribution surface MUST be recorded in the owning project's extension-point catalog (`EXTENSION_POINTS.md`).

#### Load-time robustness

- **FR-018**: Loading a definition that references an unregistered alias MUST NOT throw. The original alias string MUST be preserved on subsequent save, and the argument MUST be marked unresolved/unknown.

#### Wire contract (for Phase 2)

- **FR-019**: The feature MUST define and document the JSON wire contract that Phase 2 (studio) consumes, covering (a) the argument type reference shape `{ alias, collectionKind }` as persisted/transferred for Variables, Inputs, and Outputs, and (b) the descriptors-endpoint payload shape `{ alias, displayName, category, defaultEditor }`.

#### Serialization & quality gates

- **FR-020**: Argument-definition (de)serialization MUST go through the sanctioned serialization path (custom `JsonConverter`s participating in the configured payload serializer), consistent with the project serialization rule; raw ad-hoc JSON handling for these payloads is not permitted.
- **FR-021**: The work MUST include framework-required unit tests: registration/wiring tests for new services and the descriptor-provider aggregation, and implementation tests covering the resolution mapping (all four collection kinds), duplicate-alias fail-fast, reserved-namespace collision, and unknown-alias graceful handling.

### Key Entities

- **Argument type reference**: the persisted `{ alias, collectionKind }` pair. The sole way an authored argument's type is stored or transferred.
- **CollectionKind**: enumeration — `Single | Array | List | HashSet`.
- **Argument descriptor (unified)**: the shared shape backing Variables, Inputs, and Outputs (name, type reference, default, storage hint, plus per-kind fields), replacing the three divergent shapes.
- **Type resolution authority**: the alias ↔ CLR type registry; the single runtime source of truth for resolving an alias. Fail-fast on duplicate registration.
- **Type descriptor**: presentation-layer record keyed by alias — `{ alias, displayName, category, defaultEditor }`.
- **Type descriptor provider**: a module-contributed contributor to the descriptor catalog (the new extension point).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of authored Variables, Inputs, and Outputs persist their type using only `{ alias, collectionKind }` — zero occurrences of namespace/assembly/version in saved argument definitions.
- **SC-002**: All four collection kinds (Single, Array, List, HashSet) round-trip and resolve correctly for each of the three argument kinds — verified by tests covering all 12 combinations.
- **SC-003**: A type-rename refactor (renaming the CLR type behind an alias, alias unchanged) leaves previously-saved workflows fully resolvable — 0 resolution failures.
- **SC-004**: Duplicate alias registration is detected at startup in 100% of cases (the application refuses to start), and reserved-namespace collisions likewise fail fast.
- **SC-005**: Loading a definition that references an unknown alias never throws; the alias is preserved across a save/reload cycle in 100% of cases.
- **SC-006**: The descriptors surface returns the union of all registered providers' descriptors, grouped by category, with every entry exposing alias, display name, category, and default-editor hint — sufficient for Phase 2 to render the picker and default editor with no further backend changes.
- **SC-007**: The wire contract (type reference + descriptors payload) is documented such that Phase 2 can be implemented against it without reading backend source.

## Assumptions

- **Breaking changes are acceptable**: the software is unreleased; the database will be wiped. No data migration or backward-compatibility layer is required or in scope.
- **Default-editor vocabulary is extensible**: `defaultEditor` is an open string hint (e.g. `text`, `checkbox`, `number`, `date`); its exact value set is a Phase-2 presentation concern and may grow without changing this contract.
- **All collection kinds are allowed for all types**: there is no per-type restriction on which collection kinds are selectable.
- **Existing runtime collection handling is reused**: the runtime already special-cases array/collection conversion; this feature feeds it correctly-closed types rather than introducing new runtime conversion logic.
- **Reserved primitives are framework-owned**: the framework defines the bare-alias primitive set; modules never register bare aliases.

## Dependencies & Out of Scope

### Dependencies

- **Runtime persistence/resolution of workflow variables & inputs at execution time** is a separate, known gap. This spec covers the *authoring + serialization* model only. Correct end-to-end execution of authored variables/inputs depends on that separate work; success criteria here are validated against the resolution/serialization path, not full execution persistence.

### Out of Scope

- **Studio/frontend implementation (Phase 2)** — except for defining and documenting the wire contract Phase 2 consumes (FR-019).
- **Parameterized non-collection generics** (e.g. `Dictionary<K,V>`) and any generic arity beyond a single element type. If ever needed, a dedicated alias for the closed type is the escape hatch.
- **Per-type collection-kind restrictions** and custom equality comparers for `HashSet`.
- **Changing the internal compiled-type (activity property signature) serialization path** beyond adding `HashSet` support where shared.
