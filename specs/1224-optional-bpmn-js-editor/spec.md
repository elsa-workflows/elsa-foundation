# Feature Specification: Optional bpmn-js editor

**Feature Branch**: `agent/codex/3597-bpmn-adoption-plan` (first resubmission slice)
**Created**: 2026-11-05
**Status**: In progress — representative feasibility qualified on its recorded source; production adoption remains unmerged and unqualified.
**Input**: [#3597](https://github.com/elsa-workflows/elsa-foundation/issues/3597), owner-authorized end-to-end delivery on 5 November 2026.
**Program**: [BPMN Engine](../../docs/program-goals/bpmn-engine.md).

**Resumption, 12 November 2026**: [PR #3950 was closed without merging](https://github.com/elsa-workflows/elsa-foundation/pull/3950#issuecomment-6739901422). The owner retained delivery authorization and required stacked PRs of roughly 3,000 changed lines, one spec slice per PR. [The delivery sequence](delivery-slices.md) preserves every requirement below and distinguishes historical prototype results from current-source acceptance. The original branch and unfinished source are preserved.

The unpublished packet originally used number 1217. [Standalone triggers reserved that number first](https://github.com/elsa-workflows/elsa-foundation/issues/3582#issuecomment-6671646417), on 5 November at 22:53 UTC. This resubmission uses 1224 after checking main/archive, current open PRs (including 1223) and the conversion initiative branch. Historical references to optional-editor spec 1217 identify this packet before renumbering; they do not identify the triggers specification.

## User Scenarios & Testing

### User Story 1 — Choose a canvas for the same process (P1)

An author opens an existing BPMN process, chooses the optional bpmn-js editor, edits its routing and returns to the integrated editor. Both views show the same process and unsaved work.

**Independent test**: Start in each editor, change a gateway/default path and a supported Elsa task binding, switch twice, save/reload, publish the same process and execute inputs exercising both paths. Inspect actual activity results and identifiers.

**Acceptance scenarios**:
1. Switching preserves process, element, flow and activity binding identities and all untouched Elsa configuration, including expressions, inputs/outputs, connection/secret references, typed variables and nested/listener bodies.
2. Supported canvas and property edits participate in the ordinary dirty/save/recovery and undo/redo lifecycle; switching neither saves nor discards changes implicitly.
3. Reloaded layout and downloaded XML reflect the supported edited process. Serialization differences do not change behavior. Any XML export omissions remain visible and are distinguished from internal switching fidelity.
4. Both editors support editing the full BPMN toolbox. Advanced content must retain accurate notation, editable configuration, identity and layout in the integrated Elsa editor; missing modeling support alone must not substitute a read-only view (owner correction, 6 November 2026).

### User Story 2 — Configure Elsa behavior while modeling (P1)

Selecting an element connects the author to the existing Elsa configuration and supported nested authoring. Module-contributed editors and bindings remain useful.

**Independent test**: Change an expression-backed binding, enter/return from a nested body, retain a configured listener, and prove one meaningful module-owned activity through real execution.

**Acceptance scenarios**:
1. Selection maps to the existing element/activity, including after reconnects, undo and editor changes.
2. A missing binding/catalog entry is explained without deleting its configuration.
3. Workflow, reusable Activity Graph and nested BPMN authoring retain their host's save/version/publication contracts. Existing run inspection remains available.

### User Story 3 — Recover safely from unsupported or unavailable editing (P1)

The author understands which constructs can be modeled, retained and executed, and can recover without damaging the process.

**Independent test**: Attempt unsupported modeling, unavailable optional module/backend, invalid routing and read-only/source-owned mutation. Verify the original draft remains intact and no invalid process is silently published.

**Acceptance scenarios**:
1. Unsupported transitions are explained before canonical mutation; recoverable local edits remain available until explicitly corrected or discarded.
2. Read-only and source-owned content cannot acquire an editable bypass through editor preference or switching.
3. Native child-bound and direct human/service BPMN publication profiles remain distinct; editor choice never selects another execution engine.

### User Story 4 — Use the optional editor within Studio (P2)

The alternate canvas fits existing navigation, themes, narrow screens and keyboard/focus behavior while retaining familiar bpmn-js interactions and required attribution.

**Independent test**: Inspect actual composed-host theme/narrow/keyboard journeys and optional asset loading, then obtain independent UX acceptance of current-commit downloadable evidence.

### Edge cases

- Element deletion/type replacement with child or listener bindings; default-flow deletion/reconnection; disconnected and invalid graphs.
- Nested processes, non-BPMN child composites, unavailable versions, unknown authored properties and contributed fields.
- Concurrent property and canvas edits; pending asynchronous projection during switch, undo, save or navigation; failed optional loading.
- Existing drafts, historical/pinned versions, source-owned content, denied permissions and direct publication.
- Multiple BPMN scopes and root/process IDs that differ; position, size, waypoint and label fidelity.

## Requirements

- **FR-001**: Preserve one canonical authoring document and normal version/publication identity across editors; no diverging persisted process or sidecar store.
- **FR-002**: Preserve all untouched supported semantic/configuration data and stable identities, even when the alternate canvas does not display that data.
- **FR-003**: Route changes through ordinary host authoring operations and one coherent history; explicit save, autosave, recovery and switching must agree.
- **FR-004**: Reuse the existing Elsa inspector beside the bpmn-js canvas for authorized configuration and nested editing, including meaningful module contributions (owner choice, 5 November 2026).
- **FR-005**: Expose the full BPMN toolbox in production (owner choice, 6 November 2026), aligned with the separately owned execution-model refactoring. Both bpmn-js and the integrated Elsa editor must support editing that toolbox. Distinguish authorable/retainable constructs from currently executable constructs. Preserve draft content and explain/block unsupported execution at validation/publication without reinterpretation or silent loss. Coordinate existing safeguards under #2736; this feature does not claim future execution support already exists.
- **FR-006**: Preserve existing permissions, source ownership, validation, publication, Test Drive and actual run inspection.
- **FR-007**: Load optionally through existing Studio composition, support compatible consumer packaging, and measure composed-host network/assets against current budgets.
- **FR-008**: Preserve relevant layout and export behavior; distinguish internal preservation from public XML interchange limitations.
- **FR-009**: Keep required attribution visible/unobstructed and document the pinned dependency, license, upgrade surface and maintenance controls.
- **FR-010**: Demonstrate the real embedded prototype before freezing production integration contracts. Feasibility success alone does not complete delivery.
- **FR-011**: Extend the integrated Elsa canvas and its configuration/navigation/connection operations to accurately represent and edit the full toolbox, including advanced structures and their relevant diagram data. Read-only behavior remains for actual permissions, source ownership and historical inspection; it is not the production fallback for missing modeling support.

### Key Entities

The existing process draft, BPMN elements/flows, bound Elsa activities and design layout remain the authoring entities. Editor preference is a user choice, never an alternative process identity.

## Success Criteria

- **SC-001**: Both representative starting directions preserve all asserted identities/configuration across edit, switch, undo/redo, save and reload; mutation controls detect loss.
- **SC-002**: Both input paths execute the published edited version with expected bound activity results and inspectable occurrences.
- **SC-003**: Every acceptance row explicitly records passed, failed, unsupported, not run or unavailable evidence; no omitted row counts as success.
- **SC-004**: Current-commit composed-host journeys, inspected visuals and independent acceptance cover the changed experience; current-head review/checks and resulting-main qualification pass before closure.
- **SC-005**: A construct/configuration matrix covering the pinned bpmn-js toolbox demonstrates authoring and lossless same-process switching in both editors, including advanced drafts whose execution the current host must refuse. The integrated editor must not silently replace unfamiliar notation with generic tasks or lock those drafts solely because its modeling support is incomplete.

## Assumptions and Boundaries

Owner-selected same-process switching and retaining the integrated editor are fixed. The existing host-agnostic semantics core remains authoritative (ADR 0063). Adjacent execution-model, Block and testing work retain their owners. No production deployment/package release is authorized. Material incompatibility returns to the owner with concrete options; it does not silently reduce these requirements.

The owner explicitly selected the full BPMN toolbox, citing ongoing full-spec execution-model work. The first prototype's restricted admission is temporary feasibility scaffolding, not production scope. Production design must reconcile full drafting/retention with current host execution safeguards and editor-switch preservation.

On 6 November the owner corrected the subsequent advanced-draft choice to **expand Elsa's integrated editor to edit the full BPMN toolbox in this delivery**. This supersedes the earlier, immediately corrected read-only-view selection. The optional editor feature therefore owns the necessary integrated-editor modeling expansion; execution-engine completion remains separately owned.
