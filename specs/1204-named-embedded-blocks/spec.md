# Feature Specification: Named embedded blocks

**Feature Branch**: `agent/codex/3748-embedded-blocks-plan`
**Created**: 2026-11-03
**Status**: In progress — establishment merged; prototype gates production completion
**Input**: Program [#3610](https://github.com/elsa-workflows/elsa-foundation/issues/3610) and the owner's 3 November instruction to persist the durable breakdown and deliver the program end to end.

## User Scenarios & Testing

### User Story 1 — Organize logic in a named block (Priority: P1)

An author selects eligible activities, extracts them into **Prepare shipment**, opens the implementation and returns to the surrounding flow. An empty block can also be created and populated. Workflow and reusable Activity Definition authors receive equivalent operations.

**Why this priority**: Organization should not require publishing a separate reusable activity or changing surrounding data access.
**Independent Test**: Create/extract, name, enter, edit, return, save/reload, undo/redo and execute a block in each authoring resource.

**Acceptance Scenarios**:

1. Given contiguous Sequence steps, extraction replaces them with one named block while retaining child identities, order, surrounding data access and externally consumed results.
2. Given a supported linear Flowchart region, extraction retains entry/exit behavior and external interactions. Unsupported connections or outcomes are explained before mutation.
3. Immediately after extraction, one Undo reverses the complete refactoring; Redo restores it, with coherent selection, layout and dirty state. Later child edits remain separate history actions and are undone before the extraction.
4. Given an eligible plain block, Ungroup restores its contents without losing scope, settings or references; otherwise the operation explains the restriction.
5. Given published, running, read-only or source-owned content, inspection remains possible but authoring cannot mutate that content.

### User Story 2 — Extract a BPMN region (Priority: P1)

An author extracts a supported linear BPMN selection into an ordinary subprocess, edits its nested process and executes the same intended work.

**Why this priority**: The owner explicitly included BPMN extraction in this release.
**Independent Test**: Extract a same-lane native task region, save/reload, run and inspect it, then audit supported downloaded/reimported process structure, binding metadata and layout.

**Acceptance Scenarios**:

1. Given eligible activity-bound tasks, extraction creates a real nested process and preserves identities, unconditional routing and existing task configuration.
2. Given gateways, boundary events, transactions, event subprocesses, compensation, direct-model bindings or other unsupported crossings, extraction explains why and changes nothing.
3. Given an exported supported profile, subprocess structure, element/flow identities, supported bindings and diagram layout survive the round trip. Unsupported binding export cannot silently produce an apparently equivalent executable process.

### User Story 3 — Promote a suitable block (Priority: P2)

An author reviews the block's captured data and the proposed reusable inputs, outputs, local variables, outcomes and call-site bindings. Studio creates an ordinary reusable draft; the embedded block remains until an exact published version can replace it.

**Why this priority**: Reuse should be an explicit boundary introduced after organization, with reviewable changes to the public contract.
**Independent Test**: Promote a typed-reference example, review/publish normally, resume and replace the block, then execute two independently bound call sites.

**Acceptance Scenarios**:

1. Given supported dependencies, analysis proposes explicit contract and binding changes without modifying source or creating a definition until the author applies the review.
2. Given opaque scripts, unsafe external writes, changed control targets or unpreservable evaluation timing, analysis locates the restriction and retains the original block.
3. Given an unpublished, invalid, stale or unavailable target, replacement cannot proceed and the source remains intact.
4. Given a reviewed exact published target, replacement is one undoable source edit. Undo restores the block without deleting shared reusable resources.

### Edge Cases

- A selected child result is consumed after the block, including repeated executions and loop iterations.
- A producer outside the selection supplies a child input; parent and ancestor variables retain existing visibility.
- A child waits, resumes, faults, is retried through supported recovery, or is cancelled while waiting.
- Break and Complete must retain their existing targets during extraction; promotion refuses changed targets.
- Names and layout survive draft/version/source transitions and do not drift in historical inspection.
- Catalog versions, permissions, draft revisions or the selected published target change while a preview is open.
- An optional module is absent, or the host cannot support the requested composite or graph publication.

## Requirements

### Functional Requirements

- **FR-001**: Authors MUST create, name, open and edit embedded blocks through ordinary authoring, without a separate definition catalog or implicit execution-policy/value-isolation boundary.
- **FR-002**: Extraction MUST support contiguous Sequence steps and bounded single-entry/normal-exit linear Flowchart regions, preserving intended behavior and existing authorized data visibility.
- **FR-003**: BPMN extraction MUST use a real nested process and the explicitly supported native binding profile; unsafe process crossings MUST be refused before mutation.
- **FR-004**: Extraction MUST preserve existing child identities and exact referenced activity versions wherever possible; necessary reference/layout changes MUST be consistent and validated.
- **FR-005**: Refactoring, rename and eligible Ungroup MUST preserve selection, navigation, layout, dirty state and meaningful atomic undo/redo through normal save/reload.
- **FR-006**: Block names and child inspection MUST remain available for published and historical runs; authoring MUST NOT mutate published versions or running execution material.
- **FR-007**: Promotion MUST analyze supported dependencies and review explicit inputs, outputs, local declarations, outcomes and call-site bindings; unsupported dependencies MUST be located and refused safely.
- **FR-008**: Promotion MUST use ordinary reusable draft/validation/publication lifecycle, retain the source until exact-version replacement is available, and recheck source/target freshness before applying.
- **FR-009**: Workflow and Activity Definition graph editors MUST honor equivalent operations and their own persistence, authorization, source-authority and read-only contracts.
- **FR-010**: Program delivery MUST have native issue hierarchy, a Kanban scheduling surface, canonical specifications, dependencies and evidence-based parent completion. Establishment and prototype completion MUST remain distinct from product delivery.

### Key Entities

- **Embedded block**: a named occurrence of an existing composite, embedded at one parent location.
- **Refactoring preview**: the complete proposed source transformation or located refusal reasons.
- **Promotion proposal**: candidate reusable implementation/public interface and explicit call-site rewrites for an exact source snapshot.
- **Activity presentation**: typed per-node authoring information frozen with the relevant version/source for historical inspection.

## Success Criteria

- **SC-001**: On both authoring resources, supported create/extract/edit/rename/Ungroup journeys retain their saved content and reverse/reapply as complete user actions.
- **SC-002**: Paired grouped/ungrouped runs agree on expected outputs, outcomes, surrounding variable effects and external interactions, including externally consumed child results.
- **SC-003**: Supported waiting/resumption, cancellation, failure recovery and repeated executions show the block and relevant child occurrences without changing historical versions.
- **SC-004**: BPMN extraction retains supported subprocess behavior, bindings and audited interchange/layout fidelity; unsupported selections and exported binding profiles never silently degrade.
- **SC-005**: An eligible promotion completes review, normal publication and exact-version replacement; two call sites execute independently with the reviewed inputs/outputs, and replacement can be undone.
- **SC-006**: Every rejection, stale preview, failed publication and denied mutation leaves the original source intact.
- **SC-007**: Every original #3610 acceptance criterion has verified evidence; parent issues close only after relevant merged changes, required checks and resulting-main qualification.

## Assumptions

- Product name: **Block**. Extraction is bounded; arbitrary graph extraction and workflow-local callable subroutines are outside this release.
- The existing integrated BPMN editor is sufficient; optional bpmn-js work #3597 is independent.
- Promotion requires safe analysis and follows review → publish → replace. No automatic publication or deletion of shared resources on Undo.
- The owner authorized autonomous end-to-end implementation, scoped PR delivery and resulting-main verification. Releases and deployment are excluded.
- Prototype findings may require narrowly scoped supporting fixes; they do not authorize weakening value-scope, causal ambiguity or historical immutability rules.
