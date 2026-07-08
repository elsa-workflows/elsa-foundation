# Feature Specification: Layout Metadata Designer Bag Becomes Opaque JsonElement (ADR 0035 D3 extension)

**Feature Branch**: `TBD` (own branch — small, contained design-model change)

**Created**: 2026-07-08

**Status**: Draft

**Input**: Finish retiring open-object polymorphism from design content by making the layout
designer bag `DesignMetadataRecord.AdditionalProperties` an **opaque `JsonElement?`** (kept verbatim),
mirroring what ADR 0035 D3 / #570 already did for `StateSource`'s `PropertyInfo`/`UISpecifications`.

**Program goal**: `none/free-flow`. Consistency follow-up to ADR 0035 D3.

## Context

ADR 0035 D3 (landed in #570, `bd92b408`) converted the opaque Studio-authored UI bags in `StateSource`
— `InputDefinition`/`OutputDefinition.PropertyInfo` and `UISpecifications` — from
`IDictionary<string, object>?` to `JsonElement?`, so open-object polymorphism is gone from
`StateSource` and those bags are stored **verbatim** (D3 rider: *"opaque JSON is stored as
`JsonElement` and never rewritten… never round-tripped through a `Dictionary<string, object>`"*).

D3 scoped itself explicitly to `StateSource`. **Layout is a separate document**
(`WorkflowDefinitionVersionLayout` / `WorkflowDefinitionDraftLayout`), so
`DesignMetadataRecord.AdditionalProperties` — the catch-all bag of Studio-authored per-node layout
metadata (extra props beyond `X/Y/Width/Height`) — survived as `Dictionary<string, object?>?`. It is
**the last open-object-polymorphism holdout in serialized design content**, and it is the *same class*
of opaque Studio bag D3 targets.

It is **deterministic today** (spec 086 / #570's `DeterministicDictionaryConverterFactory` now claims
`Dictionary<string, object?>` and sorts its keys, and its `object?` values deserialize to verbatim
`JsonElement`), so this is **not a hash-correctness bug**. But an *opaque* bag is being **canonicalized
(key-sorted)** rather than kept verbatim — exactly the treatment D3 argues opaque author data must not
get (*"mutating the author's bytes… asserting a semantic equality Elsa does not own"*). Benign while
layout is not content-hashed (ADR 0034 hashes only `versions/*.json` state, not layout), but it becomes
load-bearing the moment anything hashes or diffs layout.

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Layout designer metadata is preserved verbatim (Priority: P1)

Studio authors per-node layout metadata (arbitrary keys under `AdditionalProperties`); it round-trips
through the backend byte-for-byte, without key reordering or value canonicalization.

**Independent Test**: Persist a layout record whose `AdditionalProperties` JSON has keys in a specific
order; reload and re-serialize; assert the bag is byte-identical to what Studio sent (order preserved).

**Acceptance Scenarios**:

1. **Given** a layout record with `AdditionalProperties` = `{"z":5,"color":"red"}` (author order),
   **When** it is persisted and reloaded, **Then** the stored/re-emitted bytes preserve `{"z":5,"color":"red"}`
   verbatim (not reordered to `{"color":"red","z":5}`).
2. **Given** the same logical metadata sent twice, **Then** serialization is deterministic (unchanged
   from today).

### Edge Cases

- `AdditionalProperties` is null/absent — unchanged (`JsonElement?` null).
- Nested objects/arrays inside the bag — preserved verbatim (they are opaque).
- A consumer that previously indexed the dictionary (`AdditionalProperties["key"]`) — must read via
  `JsonElement` API instead (see FR-002).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `DesignMetadataRecord.AdditionalProperties` MUST change from `Dictionary<string, object?>?`
  to `JsonElement?`, and the interface/view mirrors MUST follow:
  `IDesignMetadataRecord.AdditionalProperties`, `IWorkflowDefinitionLayout.AdditionalProperties`
  (`src/Elsa/Workflows/Design/Core/Contracts/IWorkflowDefinitionLayout.cs:27`), and
  `WorkflowDefinitionLayoutRecordView.AdditionalProperties`
  (`src/Elsa/Workflows/Design/Api/Models/WorkflowDefinitionLayoutRecordView.cs`).
- **FR-002**: Update the mapping/consumer sites — at minimum
  `UpdateDefinitionCommandHandler.cs:48` (`view.AdditionalProperties?.ToDictionary(...)` → assign the
  `JsonElement?` verbatim) — and any code that reads `AdditionalProperties` as a dictionary must move to
  the `JsonElement` API. (Grep confirms the surface is small: the 4 declarations above + this one
  mapping site.)
- **FR-003**: The bag MUST be kept as `JsonElement` **end-to-end** and never round-tripped through a
  `Dictionary<string, object>` (ADR 0035 D3 rider) — Studio's authored bytes preserved verbatim.
- **FR-004**: No migration / backward-compat is required (unreleased software).
- **FR-005**: Wire-compatible with Studio (separate repo): Studio still sends the same JSON object;
  the backend deserializes it into a `JsonElement` instead of a `Dictionary`. Verify the
  Studio↔backend layout wire contract still round-trips; **no Studio change should be required**.
- **FR-006**: Record the scope extension: add a one-line note to ADR 0035 D3 that layout
  `AdditionalProperties` is now also opaque `JsonElement` (D3 extended beyond `StateSource` to the
  layout document), so open-object polymorphism is fully retired from design content.
- **FR-007**: Tests MUST assert verbatim round-trip (FR-003) and that a layout with `AdditionalProperties`
  serializes deterministically without reordering the bag's contents.

### Key Entities

- **`DesignMetadataRecord`** — layout record (`NodeId, X, Y, Width, Height, AdditionalProperties`);
  `AdditionalProperties` becomes `JsonElement?`.
- **`IDesignMetadataRecord` / `IWorkflowDefinitionLayout` / `WorkflowDefinitionLayoutRecordView`** —
  mirror the type change.

## Success Criteria *(mandatory)*

- **SC-001**: No `Dictionary<string, object>` / `IReadOnlyDictionary<string, object>` remains as an
  opaque designer bag in serialized design content (StateSource *or* layout) — the open-object holdout
  is removed (verifiable by grep + the arch/serialization tests).
- **SC-002**: A layout `AdditionalProperties` bag round-trips byte-identically (author order preserved).
- **SC-003**: Existing Design/serialization test suites pass; the Studio↔backend layout wire round-trips
  unchanged.

## Out of Scope / Non-Goals

- `StateSource` fields (`PropertyInfo`, `UISpecifications`) — already converted in #570.
- Any behavior change to layout semantics beyond the bag's storage type.
- Content-hashing layout (ADR 0034 hashes only version state; unchanged here).

Consistency follow-up to [ADR 0035](../../docs/adr/0035-serialization-unifies-on-the-alias-registry-and-retires-open-object-polymorphism.md)
D3; reference implementation is #570 (`bd92b408`) on the `StateSource` bags.
