# Research: Activity Definition Graph Authoring

## Decision 1 — Extract a controlled workspace, not a WorkflowEditor mode

**Decision**: Introduce `GraphAuthoringWorkspace` as a controlled presentation and interaction boundary. Workflow Definition and Activity Definition hosts supply resource-specific document adapters, capabilities, persistence callbacks, lifecycle actions, diagnostics, and runtime surfaces.

**Rationale**: `WorkflowEditor` currently owns workflow loading, autosave choices, publishing, artifacts, test runs, import/export, Weaver, and definition metadata. A mode flag would couple Activity Definition behavior to that lifecycle. The underlying palette, canvas, graph model, breadcrumbs, layout interactions, and accessibility are reusable.

**Alternatives rejected**:

- Render `WorkflowEditor` with an activity mode: rejected because workflow-only behavior is spread throughout the component and hooks.
- Keep a second miniature graph editor: rejected because designer behavior, accessibility, and defect fixes would continue to diverge.

## Decision 2 — Use a graph document adapter

**Decision**: The workspace operates on a host document through explicit root, layout, scope, selection, and edit operations. An Activity Graph adapter projects `provider.payload.rootActivity`, graph variables, mappings, and `ActivityDefinitionLayoutRecord[]`; a Workflow adapter projects `WorkflowDraft.state.rootActivity` and workflow design metadata.

**Rationale**: Both resources use the same `ActivityNode` tree, but their envelopes, layout records, expression scope, and save semantics differ. The adapter makes that difference visible and testable.

**Alternatives rejected**:

- Cast the Activity Graph payload to `WorkflowDraft`: rejected because it fabricates workflow contract/lifecycle fields and risks losing provider fields.
- Genericize all workflow hooks at once: rejected because it produces an over-broad abstraction before the activity host proves the required seam.

## Decision 3 — Separate shared graph interactions from resource analysis

**Decision**: Reuse graph primitives and extract canvas mechanics around the `commitCanvas` boundary. Keep workflow-specific availability, reusable-version recommendations, scoped-variable analysis, and observability in the workflow host. Supply Activity Definition public inputs and graph variables through an activity-specific reference context.

**Rationale**: `useWorkflowCanvas` and `useWorkflowScope` contain valuable shared interactions but accept workflow-shaped state and call workflow-specific services. Expression references differ in contract shape and cannot safely be synthesized.

## Decision 4 — Keep exact-revision persistence in ActivityDefinitionDraftEditor

**Decision**: Activity Graph workspace edits call the existing provider contribution `onChange`; the Activity Definition draft host remains responsible for debounced replacement, recovery, conflict copies, validation, proposals, migration, publication, and test-run preparation.

**Rationale**: The host already has correct exact-revision sequencing and recovery semantics. The shared workspace must not know whether an edit is stored as a workflow or activity draft.

## Decision 5 — Choose latest authorable graph format automatically

**Decision**: Collapse authorable revisions by provider. When Activity Graph is the only meaningful provider, hide the implementation choice and choose the newest server-advertised authorable schema for which Studio has an exact contribution. Show implementation types only when providers represent meaningfully different authoring models.

**Rationale**: Schema 1 and 2 are revisions of `elsa.activity-graph`, not different engines. New schema 2 adds explicit outcome mappings. Schema selection is compatibility plumbing, not product intent.

**Compatibility**: Schema 1 remains openable and editable with its exact contribution. Migration to schema 2 uses the existing explicit server-advertised migration.

## Decision 6 — Derive categories from the authorized catalog

**Decision**: Build free-form combobox suggestions from the existing authorized activity authoring catalog. Trim, compare case-insensitively, retain one display spelling, sort predictably, and never introduce a category entity.

**Rationale**: Foundation already accepts category as a string and catalog items carry category. A new endpoint or taxonomy adds unnecessary lifecycle and authorization surface.

**Risk**: Add/retain an authorization regression test around the catalog before treating its categories as suggestions.

## Decision 7 — Model the Code tab as an authoring projection

**Decision**: Serialize editable contract, provider manifest payload, layout, and presentation label. Exclude server-managed definition/draft identity, activity type key, revision, timestamps, validation, lifecycle, and migration state. Applying valid JSON is one edit; invalid/unapplied JSON remains local.

**Rationale**: This matches what authors control while avoiding false promises that a raw database record is portable or replaceable.

## Decision 8 — Permit convergent boundary outcomes

**Decision**: Each graph source outcome maps at most once, while one emitted public outcome may be targeted by one or more graph outcomes. Every emitted public outcome requires at least one mapping.

**Rationale**: Elsa activities may produce multiple outcomes and several internal paths may intentionally converge on one public boundary. Stable source uniqueness prevents ambiguity; target uniqueness is unnecessarily restrictive.

**Foundation impact**: `GraphActivityProvider` and `GraphActivityDescriptor` currently reject repeated boundary targets/names and must be changed with focused tests. The runtime selection logic already resolves the matched source mapping.

## Decision 9 — Keep workflow and activity lifecycle UI distinct

**Decision**: Share graph authoring and runtime interaction components, but keep Activity Definition validation, migration, test, proposal, publication, and revision controls in its host. Do not add workflow version lists, deployment, trigger, scheduling, artifacts, or instance lifecycle screens.

## Existing support confirmed

- Foundation supports Activity Graph schemas 1 and 2 plus explicit 1→2 migration.
- Public contracts already carry arbitrary outcome arrays with `isEmitted`.
- Creation already accepts definition metadata, provider manifest, contract, and layout as one command.
- Draft replacement already uses optimistic exact revision and persists provider payload, contract, layout, and presentation label.
- Category is already a free string and authorized catalog items expose categories.
- Studio already has Activity Definition validation, proposal review, conflict recovery, provider migration, publication review, and activity test-run surfaces.
