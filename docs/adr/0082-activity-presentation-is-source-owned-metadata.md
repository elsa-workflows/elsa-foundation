# Activity Presentation Is Source-Owned Metadata

Status: accepted (2026-07-28)

## Context

Workflow authors need to name and describe individual activity occurrences. These values explain a
workflow but do not alter execution. Draft and version persistence already separates designer-owned
geometry from `WorkflowDefinitionState`, while published and Test Run Source References already
freeze source-owned layout and authored-input information.

A historical executable can outlive its definition, and a reusable authored activity can be placed
multiple times in a flattened executable graph. Resolving current draft wording or joining only by
authored node ID would therefore be unstable or ambiguous.

## Decision

Store optional Display Name and Description as a typed, node-keyed presentation collection in the
workflow draft/version design-metadata sibling. Promotion copies the collection with the authored
state and layout.

Publication and Test Run project the collection onto the Source Reference as an immutable
presentation sidecar keyed by flattened `ExecutableNodeId`. Reusable-activity compilation remaps
authored node IDs through the same placement context used for layout.

Presentation is not part of executable nodes, Execution Material, or `WorkflowExecutableHasher`.
Existing persisted documents and Source References without the collection remain valid and expose
an empty collection.

## Considered Options

- Put presentation on `ActivityNode` or executable nodes. Rejected because it couples documentation
  to runtime state and makes accidental behavioral hashing likely.
- Put presentation in `DesignMetadataRecord.AdditionalProperties`. Rejected because the server
  cannot validate or expose a durable typed contract.
- Resolve the current source definition during inspection. Rejected because historical wording
  would drift and the source may be unavailable.
- Store presentation on the content-addressed executable. Rejected because cosmetic changes would
  duplicate behavioral artifacts or violate content identity.

## Consequences

- Design update/read contracts and Groundwork documents gain an additive typed collection.
- Publish, reusable-activity publish, and Test Run paths must snapshot and remap presentation.
- Inspection views can render stable historical labels without resolving mutable definitions.
- Presentation-only republishes resolve to the same artifact hash while retaining distinct
  Source Reference wording.
- The Source Reference persistence envelope advances for the additive sidecar while old envelopes
  remain readable.

Related decisions: ADR 0038 (purely behavioral hash), ADR 0039 (layout on Source Reference), and ADR
0040 (Test Run Source References).
