# Feature Specification: Deterministic Payload Serialization

**Feature Branch**: shipped via PR #549 (own branch — cross-cutting shared-serializer change)

**Created**: 2026-07-07

**Status**: Implemented (merged to main, PR #549) — this spec documents the shipped behavior. The
determinism mechanism as shipped is `DeterministicOrderTypeInfoModifier.SortObjectMembers` (object
member order) + `DeterministicDictionaryConverterFactory` (dictionary key order), wired in
`JsonPayloadSerializer.BuildOptions()`.

**Input**: Make the shared `IPayloadSerializer` (System.Text.Json) **deterministic** so equal object
graphs always produce byte-identical JSON, regardless of dictionary insertion order or reflection
ordering. Prerequisite for content-hashing serialized workflow state
([ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md) D3/D8) and
independently valuable anywhere Elsa hashes serialized state.

**Program goal**: `none/free-flow` (foundation serialization quality). Unblocks
[`specs/085`](../085-workflow-definition-gitops/spec.md).

## Context

`JsonPayloadSerializer.BuildOptions()`
(`src/Elsa/Serialization/SystemText/Services/JsonPayloadSerializer.cs`) builds
`JsonSerializerOptions` with CamelCase + ignore-null but **no ordering guarantee**. Two sources of
non-determinism make the same logical state serialize to different bytes across runs/hosts:

1. **Dictionary key order** — System.Text.Json emits `IDictionary` entries in enumeration order.
   Workflow state is dictionary-heavy (variables, inputs, properties).
2. **Object member order** — `System.Text.Json` emits reflection-ordered members, which is not
   guaranteed stable across runs/hosts; member order must be fixed. (The former open-object
   polymorphism converters — `PolymorphicObjectConverter` / `…Factory` — were retired by ADR 0035
   D2/D5, PR #570; type identity is now a registry alias and there is no injected discriminator to
   place.)

ADR 0034 (D3/D8) makes content identity a hash over the canonical serialization and requires
`StateSource` to *be* that canonical form; without determinism, the git hash tripwire (D7) throws on
nothing but key-order noise. Activity reconciliation already relies on a content hash, so determinism
pays off system-wide. **This is unreleased software — no migration / backward-compat is required**
([memory: unreleased-no-backcompat]); existing frozen `StateSource` is never re-serialized.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Equal state serializes to identical bytes (Priority: P1)

Serializing two equal object graphs (built with dictionaries populated in different insertion orders)
produces byte-identical JSON.

**Independent Test**: Build the same `WorkflowDefinitionState` twice with dictionaries filled in
different orders; assert `Serialize(a) == Serialize(b)` byte-for-byte, and that both round-trip
(`Deserialize(Serialize(x))` equals `x`).

**Acceptance Scenarios**:

1. **Given** two equal graphs with differently-ordered dictionaries, **Then** their serialized bytes
   are identical.
2. **Given** an object with reflection-ordered members, **Then** the members serialize in a fixed
   order every run.
3. **Given** any serialized payload, **Then** it round-trips to an equal object (no semantic loss
   from ordering normalization).

### User Story 2 — Determinism holds across processes/hosts (Priority: P2)

The same input hashes identically in two separate processes (no dependence on runtime reflection
order or hash-seed-randomized dictionary iteration).

**Independent Test**: Serialize + SHA-256 the same fixture in two process invocations; assert equal
digests.

### Edge Cases

- Nested dictionaries, dictionaries with non-string keys, collections of polymorphic items.
- `null` values (must stay stable under `WhenWritingNull`).
- Performance: `JsonPayloadSerializer` caches options per converter-registry revision (hot path —
  execution-log payloads); the determinism hook must not defeat that cache or add per-call cost.

## Requirements *(mandatory)*

- **FR-001**: `IPayloadSerializer.Serialize` MUST emit dictionary entries in a **stable key order**
  (ordinal by serialized key) for all `IDictionary`/`IReadOnlyDictionary` shapes in scope.
- **FR-002**: Object member order MUST be deterministic — a fixed policy applied via a `JsonTypeInfo`
  contract modifier (shipped as `DeterministicOrderTypeInfoModifier.SortObjectMembers`).
- **FR-003**: Determinism MUST hold **across processes** (independent of reflection order and
  hash-randomized dictionary iteration).
- **FR-004**: Serialization MUST remain **round-trip lossless** — normalization changes byte order
  only, never semantics.
- **FR-005**: The change MUST preserve the existing options-caching behavior in `JsonPayloadSerializer`
  (no measurable per-call regression on the payload hot path).
- **FR-006**: A **determinism test** MUST assert byte-identity for equal-but-differently-ordered
  inputs and equal digests across two process runs (guards against silent regression).
- **FR-007**: No migration / backward-compat shim is required; the serializer simply becomes
  deterministic (unreleased software).

### Key Entities

- **Canonical serialization** — the deterministic output; the content-hash preimage (ADR 0034 D3).
- **Ordering hook** — the `JsonTypeInfo` contract modifier (`DeterministicOrderTypeInfoModifier.SortObjectMembers`)
  plus the dictionary-normalizing converter (`DeterministicDictionaryConverterFactory`), both applied in
  `JsonPayloadSerializer.BuildOptions()`.

## Success Criteria *(mandatory)*

- **SC-001**: 100% of a randomized-order fixture corpus serializes byte-identically to its
  canonical-order twin.
- **SC-002**: Cross-process digest equality holds for the fixture corpus.
- **SC-003**: The full serialization + design/runtime persistence test suites pass unchanged.
- **SC-004**: No regression on the payload-serialization hot-path benchmark (options cache intact).

## Out of Scope / Non-Goals

- Changing the wire schema semantics or the polymorphic model itself.
- Migrating existing persisted `StateSource` (unreleased; frozen versions are never re-serialized).
- The git hashing/consumer logic (that lives in `specs/085`).

Prerequisite for [`specs/085`](../085-workflow-definition-gitops/spec.md) (ADR 0034 D3/D8).
