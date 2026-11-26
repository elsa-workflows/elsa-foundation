# Research: Activity Instance Metadata

## Decision 1: Store authored metadata in typed design metadata

**Decision**: Add a node-keyed `ActivityPresentationRecord` collection to the existing
draft/version design-metadata sibling and its full-state update/read contracts.

**Rationale**: Display Name and Description are authored per occurrence, need draft history and
promotion semantics, and are neither executable activity inputs nor canvas geometry. A typed
collection validates the contract and supports root activities even when geometry is absent.

**Alternatives considered**:

- Put fields on `ActivityNode`: rejected because it mixes presentation with authored execution
  state and risks accidental inclusion in executable compilation or hashing.
- Put fields in `DesignMetadataRecord.AdditionalProperties`: rejected because Foundation could not
  validate, document, or safely project an opaque Studio-owned shape.
- Create a wholly separate persistence aggregate: rejected because presentation shares the exact
  draft/version lifecycle and atomic full-state update boundary already owned by the design sibling.

## Decision 2: Freeze metadata on each Source Reference

**Decision**: Publish and Test Run paths project presentation into an optional Source Reference
sidecar keyed by flattened `ExecutableNodeId`.

**Rationale**: A single authored node may be placed multiple times through reusable activities.
Executable node IDs are the unambiguous join key on read-only flattened graphs, and Source
References already own per-publication layout and authored-input snapshots.

**Alternatives considered**:

- Resolve the mutable source definition during inspection: rejected because historical wording
  would drift and the source can be deleted or unavailable.
- Store presentation on the content-addressed executable: rejected because cosmetic edits must not
  change or duplicate behavioral artifacts.
- Key only by authored node ID: rejected because repeated reusable placements can produce
  collisions in a flattened executable graph.

## Decision 3: Keep presentation outside behavioral identity

**Decision**: Do not add presentation to Execution Material or `WorkflowExecutableHasher`.

**Rationale**: Display Name and Description explain behavior; they do not change it. This extends
the established layout-sidecar and purely behavioral hash decisions.

## Decision 4: Resolve labels once

**Decision**: Introduce a Studio resolver with precedence: authored Display Name, catalog display
name, technical activity type.

**Rationale**: Canvas, Inspector, navigation, runtime, and Weaver must not implement subtly
different fallback logic.

## Decision 5: Use one shared copy interaction

**Decision**: Add `CopyableIdentifier` to Studio's shared UI package. It owns display truncation,
tooltip/full-value access, clipboard fallback, local state, and accessible announcements; callers
provide the semantic label and exact value.

**Rationale**: Node ID, Activity Type, Activity Version ID, and existing identifier surfaces need
consistent behavior and styling.

## Decision 6: Hide versions only when unambiguous

**Decision**: Remove versions from all canvas nodes. In the palette, show a compact version only
when two selectable rows would otherwise represent the same activity; preserve exact hidden
versions in title/accessibility text.

**Rationale**: Canvas versions are always secondary inspection data, while palette versions can be
necessary to distinguish selectable choices.
