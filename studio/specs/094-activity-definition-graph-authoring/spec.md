# Feature Specification: Activity Definition Graph Authoring

**Feature Branch**: `codex/094-activity-definition-graph-authoring`

**Created**: 2026-07-28

**Status**: Draft

**Input**: User description: "Make a graph-based activity definition feel like editing a workflow graph while retaining activity-definition identity, contract, testing, migration, and publication semantics."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Create a graph activity without provider jargon (Priority: P1)

As an activity author, I can create a graph activity by choosing its composition model and entering ordinary activity metadata, without having to understand implementation-provider schema revisions.

**Why this priority**: Authors cannot reach the improved editor unless creation is understandable and produces the correct initial graph draft.

**Independent Test**: Open Create Activity Definition with only the Activity Graph provider available, choose each composition model in turn, reuse or enter a category, and verify the created draft has the expected initial root and provider format without displaying a schema chooser.

**Acceptance Scenarios**:

1. **Given** Activity Graph is the only user-meaningful implementation type, **When** the creation dialog opens, **Then** it does not ask the author to select an implementation provider or schema revision.
2. **Given** the author is creating a graph activity, **When** they choose Flowchart, Sequence, or BPMN and submit valid metadata, **Then** identity and the initial rooted graph draft are created atomically in the latest supported Activity Graph format.
3. **Given** authorized activity definitions already use categories, **When** the author edits Category, **Then** a combobox suggests distinct matching values and also accepts a new free-form value.
4. **Given** category values differ only by case or surrounding whitespace, **When** suggestions are built, **Then** duplicates are collapsed without exposing categories the author is not authorized to discover.
5. **Given** more than one genuinely different implementation type becomes available, **When** the dialog opens, **Then** it presents those choices as implementation types and keeps technical schema revisions out of the primary labels.

---

### User Story 2 - Edit the activity graph in the shared designer (Priority: P1)

As an activity author, I can compose and configure an activity definition's graph with the same canvas, palette, inspector, nested-scope navigation, history, layout, and accessibility conventions used by workflow authoring.

**Why this priority**: The existing root-activity dropdown and miniature editor are the central usability problem.

**Independent Test**: Open a graph activity draft, add and configure nodes, edit a nested container, move nodes, use undo/redo, reload the exact revision, and verify the graph remains equivalent while activity-specific controls remain visible.

**Acceptance Scenarios**:

1. **Given** a graph activity draft, **When** its editor opens, **Then** the center workspace provides the shared designer canvas, palette, inspector, breadcrumbs, layout interactions, and keyboard behavior rather than a root-activity selector.
2. **Given** the author is at the graph root, **When** no child node is selected, **Then** the inspector identifies the activity graph as the scope owner and exposes graph variables and composition settings without rendering the root as a canvas node.
3. **Given** the author changes graph structure, properties, layout, or variables, **When** autosave succeeds, **Then** the activity-definition draft receives a new exact revision without importing workflow-definition lifecycle controls.
4. **Given** the author invokes undo or redo after a saved edit, **When** the action completes, **Then** the resulting state is persisted as a new activity draft revision.
5. **Given** a save conflict, provider migration, or externally replaced draft, **When** the replacement is accepted, **Then** incompatible local history is reset and the author receives an explicit explanation.
6. **Given** host or module contributions extend graph authoring, **When** the activity graph workspace loads, **Then** only contributions declaring support for `activity-definition-graph` participate, while shared node property and expression editors remain available automatically.

---

### User Story 3 - Define and map a multi-outcome public interface (Priority: P1)

As an activity author, I can define public inputs, outputs, and multiple outcomes, then explicitly connect the graph boundary to that contract.

**Why this priority**: A reusable activity is useful only when its public interface accurately represents its graph behavior.

**Independent Test**: Define several public outcomes and outputs, map converging root outcomes and output expressions, use public inputs in node expressions, and verify validation distinguishes complete, optional, and incompatible mappings.

**Acceptance Scenarios**:

1. **Given** any activity definition provider, **When** the author edits its public contract, **Then** it may expose zero, one, or multiple outcomes.
2. **Given** a new public outcome, **When** it is added, **Then** it is emitted by default; historical non-emitted outcomes remain representable for compatibility.
3. **Given** a graph activity exposes root outcomes, **When** boundary mappings are edited, **Then** each root outcome maps to at most one emitted public outcome and multiple root outcomes may converge on the same public outcome.
4. **Given** emitted public outcomes exist, **When** one has no implementation or graph boundary mapping, **Then** validation blocks publication and identifies the unresolved outcome.
5. **Given** a public output is required, **When** no boundary output expression is configured, **Then** validation requires exactly one mapping; optional outputs allow zero or one mapping.
6. **Given** a public input exists, **When** the author edits a node expression, **Then** the input is available by its stable reference key without duplicating it as a graph variable.
7. **Given** provider inference proposes contract changes, **When** proposals are shown, **Then** each change is contextual, reviewable, and applied only after explicit selection.

---

### User Story 4 - Edit the complete authoring draft as JSON (Priority: P2)

As an advanced activity author, I can edit the complete activity-definition authoring draft as JSON and safely return to visual editing.

**Why this priority**: JSON is a precise and efficient escape hatch for advanced edits, source review, and bulk changes, but must not expose storage internals.

**Independent Test**: Edit valid and invalid draft JSON, switch between visual and code views, add/remove/move graph nodes, and verify apply/reset, layout preservation, navigation blocking, validation, and revision behavior.

**Acceptance Scenarios**:

1. **Given** an activity draft, **When** Code is selected, **Then** JSON includes the editable public contract, provider payload, layout, and presentation metadata, but excludes server-managed database, identity, revision, timestamp, validation, and lifecycle state.
2. **Given** the JSON buffer is invalid, **When** autosave would otherwise run, **Then** the invalid buffer remains local and validation, testing, migration, publication, and unconfirmed navigation are blocked.
3. **Given** valid changed JSON, **When** Apply is selected, **Then** it becomes one undoable authoring edit and is persisted through the normal exact-revision activity draft save.
4. **Given** visual and JSON graph nodes retain their identifiers, **When** JSON is applied, **Then** existing positions are preserved; new nodes receive deterministic non-overlapping placement.
5. **Given** unapplied JSON changes, **When** the author selects Reset, **Then** the buffer returns to the last applied draft without creating a revision.

---

### User Story 5 - Validate and test the activity as an activity (Priority: P2)

As an activity author, I can understand graph and contract problems and run the activity definition in an activity-specific testing surface.

**Why this priority**: Shared graph composition must still lead to a reliable reusable activity, not a workflow-definition lifecycle experience.

**Independent Test**: Create structural, contract, and mapping errors, focus each diagnostic, then run a valid draft with test inputs and inspect outputs, outcomes, logs, incidents, and execution details.

**Acceptance Scenarios**:

1. **Given** the draft contains graph, contract, mapping, or provider errors, **When** validation completes, **Then** a collapsible diagnostics panel groups the issues and can focus the corresponding canvas node or editor field.
2. **Given** a valid activity draft, **When** the author starts a test run, **Then** the activity-specific runtime view accepts public inputs and shows public outputs, emitted outcomes, incidents, logs, and execution details using shared runtime interactions.
3. **Given** the draft is invalid, conflicted, migrating, or has unapplied JSON, **When** the author attempts a test or publication action, **Then** the action is disabled with a specific reason.
4. **Given** a legacy Activity Graph schema 1 draft, **When** it opens, **Then** it remains editable in its existing format and offers an explicit reviewed migration to the latest format; opening or saving alone never migrates it.

### Edge Cases

- No category suggestions are available, or the category catalog fails to load; free-form entry remains usable.
- A category consists only of whitespace; it is treated as empty rather than stored as a distinct value.
- A legacy schema is supported for reading but cannot perform a newer authoring operation; the editor explains that migration is required.
- The graph has no child nodes, incomplete required container slots, nested scopes, or disconnected nodes; it remains saveable as a draft and diagnostics explain what blocks testing or publication.
- An activity or provider advertises no stable outcome reference keys; mapping is unavailable with an actionable compatibility diagnostic rather than name-based guessing.
- A public outcome is changed from emitted to non-emitted while mappings reference it; affected mappings become invalid and are never silently redirected.
- A required public output's expression references a removed input, variable, node output, or unavailable activity; the mapping remains visible and invalid.
- The same authoring contribution supports both workflow definitions and activity-definition graphs; it is instantiated once for the active resource host.
- Layout metadata is missing, partial, or contains entries for removed nodes; valid entries are retained and missing active nodes are placed safely.
- Exact-revision autosave races with a code-buffer apply or conflict response; only one accepted revision becomes authoritative and the other state is recoverable or explicitly discarded.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The creation experience MUST distinguish genuinely different implementation types while hiding provider schema revisions that do not represent a user choice.
- **FR-002**: New Activity Graph definitions MUST use the latest server-advertised supported schema and MUST NOT offer legacy schema 1 for new creation.
- **FR-003**: Legacy Activity Graph drafts MUST remain edit-only in place until an explicit, reviewed provider migration is accepted.
- **FR-004**: Graph activity creation MUST require Flowchart, Sequence, or BPMN composition selection and MUST create identity plus the initial rooted draft atomically.
- **FR-005**: Category MUST be a free-form combobox whose suggestions come only from activity definitions visible through the authorized catalog.
- **FR-006**: Category matching and deduplication MUST trim whitespace and compare case-insensitively while preserving one source spelling for display.
- **FR-007**: Graph authoring MUST use a controlled, persistence-agnostic workspace shared with workflow authoring for canvas, palette, inspector, breadcrumbs, history commands, layout, and designer interactions.
- **FR-008**: Workflow-definition and activity-definition hosts MUST retain separate loading, persistence, conflict, lifecycle, validation, migration, test, publication, and resource-header responsibilities.
- **FR-009**: The activity graph root MUST be represented as the scope owner rather than a selectable canvas node or root-activity dropdown.
- **FR-010**: Graph variables MUST be activity-graph-local variables and MUST remain distinct from public inputs.
- **FR-011**: Public inputs MUST automatically enter graph expression scope by stable reference key.
- **FR-012**: The activity editor MUST expose Designer, Public Interface, and Code as primary authoring views, with Inspector and Runtime supporting surfaces and activity-specific header actions.
- **FR-013**: Every activity definition provider contract MUST allow multiple public outcomes.
- **FR-014**: Newly authored outcomes MUST default to emitted while non-emitted outcomes remain supported for compatibility and history.
- **FR-015**: Each graph root outcome MUST map to at most one emitted public outcome; multiple root outcomes MUST be allowed to map to the same public outcome.
- **FR-016**: Only emitted public outcomes MUST require an implementation or boundary mapping before publication.
- **FR-017**: Each required public output MUST have exactly one boundary output mapping; each optional output MUST allow zero or one.
- **FR-018**: Boundary output mappings MUST use the shared expression editors and input/variable picker.
- **FR-019**: Provider-inferred public-contract proposals MUST remain background, exact-revision, contextual suggestions and MUST never modify the draft without explicit selected-change application.
- **FR-020**: The Code view MUST edit an authoring draft projection containing editable contract, implementation, layout, and presentation fields rather than the raw persisted database record.
- **FR-021**: Server-managed identity, type key, revision, timestamps, validation results, lifecycle state, and provider-migration state MUST be excluded or read-only in Code.
- **FR-022**: Invalid or unapplied JSON MUST remain local, suppress autosave, and block dependent or destructive actions until applied, reset, or explicitly discarded.
- **FR-023**: Applying valid JSON MUST create one undoable authoring operation and MUST preserve layout for unchanged node identifiers while placing new nodes safely.
- **FR-024**: Graph edits and undo/redo MUST persist through the activity host's existing exact-revision autosave and recovery behavior.
- **FR-025**: Undo/redo history MUST reset after conflict resolution, provider migration, or accepted external replacement.
- **FR-026**: Graph authoring contributions MUST declare supported resource kinds: `workflow-definition`, `activity-definition-graph`, or both.
- **FR-027**: Shared node property and expression editors MUST remain automatically available in both resource hosts without duplicate contribution registration.
- **FR-028**: A collapsible diagnostics surface MUST group graph, public-contract, mapping, provider, and JSON issues and focus the affected authoring context when possible.
- **FR-029**: Activity test runs MUST reuse shared runtime interactions while using activity-specific inputs, outputs, outcomes, incidents, logs, and execution semantics.
- **FR-030**: The feature MUST NOT add workflow-definition version-list, publish-history, deployment, instance-management, trigger, or scheduling lifecycle UI to activity-definition graph editing.
- **FR-031**: All changed interactions MUST be keyboard operable, visibly focused, assistive-technology labelled, and explicit about loading, disabled, empty, conflict, and validation states.
- **FR-032**: New or changed styling MUST use the public `--studio-*` token contract and MUST NOT introduce raw color literals.

### Key Entities

- **Activity Definition Authoring Draft**: Editable projection of activity metadata, public contract, provider implementation payload, graph layout, and presentation metadata at an exact draft revision.
- **Activity Graph Composition Model**: Immutable-at-creation Flowchart, Sequence, or BPMN model governing graph structure and root semantics.
- **Activity Definition Public Interface**: Stable public inputs, outputs, and emitted or historical outcomes exposed to activity consumers.
- **Boundary Outcome Mapping**: Association from one stable graph root outcome reference to one emitted public outcome; targets may be shared.
- **Boundary Output Mapping**: At most one expression that supplies a public output from public inputs, graph variables, node outputs, or other expression-scope values.
- **Activity Category**: Normalized free-form label suggested from authorized visible activity definitions, not a separately managed entity.
- **Graph Authoring Resource Host**: Workflow-definition or activity-definition adapter responsible for resource-specific persistence, capabilities, validation, lifecycle, and runtime operations.
- **Activity Contract Proposal**: Exact-revision provider suggestion for adding, changing, or removing public interface members, applied only after explicit review.
- **Activity Test Run**: Execution of an activity-definition draft using public inputs and reporting public outputs, outcomes, incidents, logs, and execution details.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In acceptance testing, an author can create a latest-format graph activity and reach its designer without seeing provider-schema jargon or making more than one implementation choice.
- **SC-002**: 100% of the workflow designer's agreed shared graph interactions—palette insertion, selection, property editing, nested-scope navigation, layout movement, undo, redo, and keyboard canvas operation—are available in the activity graph editor.
- **SC-003**: 100% of tested activity providers can represent multiple public outcomes, and graph tests demonstrate both one-to-one mappings and multiple root outcomes converging on one public outcome.
- **SC-004**: All required contract, mapping, graph, and JSON errors tested can be located from diagnostics and prevent test/publication without preventing draft recovery.
- **SC-005**: Valid visual-to-JSON-to-visual round trips preserve all editable draft fields and the positions of every unchanged node identifier in the acceptance fixture.
- **SC-006**: Invalid JSON never creates a server revision in automated tests, and unapplied changes cannot be lost through navigation without an explicit confirmation.
- **SC-007**: Focused component, integration, accessibility, and browser tests cover creation, graph authoring, public interface mapping, JSON editing, diagnostics, and activity test-run gating with no regression in existing workflow authoring.

## Assumptions

- The server remains the authority for available providers, supported schema revisions, migrations, validation, exact-revision concurrency, contract proposals, publication, and activity test runs.
- Existing activity-definition draft, validation, proposal, migration, publication, and test-run APIs are extended only where the contract audit proves a gap; this feature does not replace those services.
- A composition-model change after creation is a future explicit destructive migration, not an ordinary property edit.
- Categories remain simple labels derived from authorized activity definitions; category administration and global taxonomy are outside scope.
- Mobile-specific graph authoring is outside scope, but responsive layouts must not create page-level overflow at supported Studio widths.
- Import/export of raw database documents, workflow-definition lifecycle screens, and automatic schema migration are outside scope.
