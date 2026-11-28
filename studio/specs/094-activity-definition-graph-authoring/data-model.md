# Data Model: Activity Definition Graph Authoring

## Activity Definition Authoring Draft

Editable state:

- `contract`: public inputs, outputs, and outcomes
- `provider.providerKey`
- `provider.schemaVersion`
- `provider.payload`: exact provider manifest payload
- `layout`: resource-owned layout records
- `presentationLabel`

Server-managed state:

- definition and draft identity
- activity type key and tenant scope
- exact revision and source version
- created/updated timestamps
- validation and lifecycle state
- manifest fingerprint and migration state

Transitions:

1. `saved → pending → saving → saved` after a visual edit or valid JSON Apply.
2. `saved|pending → local-json-dirty` while JSON differs from the applied draft.
3. `local-json-dirty → local-json-invalid` when parsing or semantic validation fails.
4. `local-json-* → pending` after valid Apply.
5. `local-json-* → saved` after Reset when the applied server draft is saved.
6. `saving → conflict` on stale revision; workspace history resets when a server/recovery replacement is accepted.

## Activity Graph Provider Payload

- `rootActivity`: rooted `ActivityNode` tree; root is the graph scope owner, not a canvas node
- `variables`: graph-local variables
- `outputMappings`: required/optional public output expressions
- `outcomeMappings` (schema 2): graph root outcome to public outcome associations
- unknown provider fields: retained across adapter and JSON round trips

## Graph Authoring Document Adapter

Responsibilities:

- read and replace the root activity tree
- read and replace graph layout without dropping unknown layout data
- resolve the current scope from navigation frames
- apply graph edits against the latest controlled document
- identify public inputs and graph variables available to expressions
- supply resource kind and capability flags

Non-responsibilities:

- API loading or saving
- revision conflict policy
- publication or migration
- test-run lifecycle
- provider inference

## Public Interface Members

### Input

- stable `referenceKey`
- authored name/presentation
- type, nullability, requiredness, default
- storage/durability metadata
- available automatically to graph expressions

### Output

- stable `referenceKey`
- authored name/presentation
- type, nullability, requiredness
- zero or one boundary expression when optional
- exactly one valid boundary expression when required

### Outcome

- stable `referenceKey`
- name and optional description
- `isEmitted`
- new outcomes default to emitted
- only emitted outcomes require implementation/mapping

## Boundary Outcome Mapping

- `sourceOutcomeReferenceKey`: stable outcome from the graph root
- `boundaryOutcomeReferenceKey`: stable emitted public outcome

Invariants:

- source is unique across mappings
- target may be shared across mappings
- target must identify an emitted public outcome
- each emitted public outcome has at least one mapping before publication
- non-emitted or removed targets invalidate, never silently redirect, existing mappings

## Boundary Output Mapping

- `boundaryOutputReferenceKey`
- expression syntax and value

Invariants:

- at most one mapping per public output
- required outputs require one valid mapping
- optional outputs allow zero or one
- references resolve against public inputs, graph variables, and available node outputs

## Activity Category

- stored value: trimmed free-form string
- comparison key: locale-stable case-folded trimmed string
- suggestion source: authorized activity catalog
- display spelling: first deterministic authorized spelling for a comparison key
- lifecycle: none outside the containing Activity Definition

## Contribution Resource Scope

Resource kinds:

- `workflow-definition`
- `activity-definition-graph`

A graph-authoring contribution declares one or both. The active host filters host-level contributions before rendering. Shared node property and expression editors remain registered by their existing node-level contracts.

## Local History

- snapshots contain only the host's editable graph document projection
- a visual edit or JSON Apply creates one history entry
- undo/redo yields a new host edit and therefore a new persisted activity revision
- history resets on conflict replacement, provider migration, or accepted external/recovery replacement
