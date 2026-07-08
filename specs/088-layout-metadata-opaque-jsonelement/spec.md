# Feature Specification: Layout Metadata Bag Becomes Opaque JsonElement

**Feature Branch**: `feat/088-layout-metadata-opaque-jsonelement` (own branch — cross-cutting serialization shape)

**Created**: 2026-07-08

**Status**: Draft

**Input**: Convert the designer **layout** document's opaque Studio bag
(`DesignMetadataRecord.AdditionalProperties`) from `Dictionary<string, object?>?` to a verbatim
`JsonElement?`, kept opaque end-to-end. Completes
[ADR 0035](../../docs/adr/0035-serialization-unifies-on-the-alias-registry-and-retires-open-object-polymorphism.md)
D3 by extending it beyond `StateSource` to the last open-object-polymorphism holdout in serialized design
content.

**Program goal**: `none/free-flow`. Follows #570 (converter removal + StateSource bags → `JsonElement`).

## Context

ADR 0035 **D3** made the canonical `StateSource` designer bags
(`InputDefinition`/`OutputDefinition.PropertyInfo`, `UISpecifications`) opaque `JsonElement?`, and #570
(commit `bd92b408`) shipped it. D3 scoped itself to `StateSource`. The designer **layout** is a *separate*
document (`WorkflowDefinitionVersionLayout` / `WorkflowDefinitionDraftLayout`, carrying
`DesignMetadataRecord` rows keyed by `NodeId`), so its per-node opaque bag —
`DesignMetadataRecord.AdditionalProperties` (`src/Elsa/Workflows/Design/Persistence/Core/Entities/DesignMetadataRecord.cs:17`) —
survived as `Dictionary<string, object?>?`. It is the last open-object-polymorphism holdout in serialized
design content.

The bag is opaque, Studio-authored per-node layout metadata; the backend never indexes it. As a CLR
dictionary it is subject to key-sorting/canonicalization if it ever routes through the deterministic
serializer, which mutates the author's opaque bytes — against D3's principle (opaque JSON is stored as
`JsonElement` and **never** round-tripped through a `Dictionary<string, object>`). Not a determinism bug
today, but it becomes load-bearing the moment layout is hashed or diffed (e.g. ADR 0034 git export).

This unit mirrors #570's `StateSource` change on the layout document: `AdditionalProperties` becomes a
verbatim `JsonElement?` held opaque from the API view, through the read contract, to the persisted record.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — An author's layout bag is stored exactly as sent (Priority: P1)

Studio sends a per-node layout bag with keys in a specific (non-alphabetical) order; the backend stores and
returns it byte-for-byte, never reordered.

**Independent Test**: Serialize a `DesignMetadataRecord` whose `AdditionalProperties` has keys in `z, a, m`
order through the deterministic payload serializer; assert the emitted JSON preserves that order (NOT sorted
to `a, m, z`) and that a full serialize → deserialize cycle reproduces the raw bytes.

**Acceptance Scenarios**:

1. **Given** a layout bag with keys in author order, **When** the record is serialized, **Then** the bag
   re-emits verbatim in the author's key order (no canonicalization).
2. **Given** the same bag, **When** serialized twice, **Then** the output is byte-identical (deterministic).
3. **Given** an equivalent *CLR dictionary*, **When** serialized through the same serializer, **Then** it IS
   key-sorted — documenting why the opaque bag must be a `JsonElement`.

### User Story 2 — Studio wire compatibility is preserved (Priority: P1)

Studio (separate repo) sends the same JSON layout object it always has; the backend deserializes it to a
`JsonElement` instead of a `Dictionary` with no Studio change.

**Independent Test**: The `PUT design/workflows/definitions/{id}` layout round-trips through
`UpdateDefinitionCommandHandler` and `GetVersion`, preserving the bag.

### Edge Cases

- Absent bag (`null`) round-trips as absent (nullable `JsonElement?`, omitted on write via
  `WhenWritingNull`).
- Nested objects inside the bag are preserved verbatim, including their own inner key order.

## Requirements *(mandatory)*

- **FR-001**: `DesignMetadataRecord.AdditionalProperties` MUST be `JsonElement?` (was
  `Dictionary<string, object?>?`), and the `IDesignMetadataRecord` read contract, the
  `WorkflowDefinitionLayoutRecordView` API view, and the `UpdateDefinition` write path MUST all carry it as
  `JsonElement?`.
- **FR-002**: The bag MUST be kept as `JsonElement` **end-to-end** and MUST NOT be round-tripped through a
  `Dictionary<string, object>` at any layer (ADR 0035 D3 rider) — the `UpdateDefinitionCommandHandler`
  `ToRecord` mapping assigns the view's `JsonElement` verbatim (no `ToDictionary`).
- **FR-003**: The bag MUST serialize verbatim — kept in the author's key order, never key-sorted or otherwise
  canonicalized — and round-trip byte-identically.
- **FR-004**: Wire compatibility MUST hold: Studio sends the same JSON object; the backend deserializes to a
  `JsonElement`. No Studio change is required.
- **FR-005**: Tests MUST cover verbatim key-order preservation (asserting NOT reordered), byte-identical
  round-trip, and deterministic serialization.
- **FR-006**: Unreleased software — NO migration or back-compat shim for previously stored dictionary-shaped
  bags.

### Key Entities

- **`DesignMetadataRecord`** — the layout record whose `AdditionalProperties` becomes `JsonElement?`; the
  redundant explicit `IDesignMetadataRecord.AdditionalProperties` implementation is dropped (the positional
  property now satisfies the contract directly).
- **`IWorkflowDefinitionLayout` / `IDesignMetadataRecord`** — the Tier-1 read contract, updated to
  `JsonElement?`.
- **`WorkflowDefinitionLayoutRecordView`** — API view, updated to `JsonElement?`.
- **`UpdateDefinitionCommandHandler`** — write path, assigns the bag verbatim.

## Success Criteria *(mandatory)*

- **SC-001**: A layout bag with a specific key order is stored and returned byte-for-byte, never reordered.
- **SC-002**: No layer round-trips the bag through a CLR dictionary; the type is `JsonElement?` from view to
  persisted record.
- **SC-003**: Existing layout, handler, and Groundwork persistence tests pass; new tests cover FR-005.

## Out of Scope / Non-Goals

- Any change to `StateSource` bags (already opaque via #570 / ADR 0035 D3).
- Layout hashing / git export itself (ADR 0034) — this unit only makes the bytes safe to hash.
- Any Studio-side change (wire is unchanged).

Extends [ADR 0035](../../docs/adr/0035-serialization-unifies-on-the-alias-registry-and-retires-open-object-polymorphism.md)
D3 beyond `StateSource`.
