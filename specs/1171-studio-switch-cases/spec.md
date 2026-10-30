# Feature Specification: Visual Switch cases

**Feature Branch**: `agent/codex/3493-switch-cases`
**Created**: 2026-10-31
**Status**: In progress
**Input**: Owner-requested end-to-end delivery of #3493.

## User Scenarios & Testing

### User Story 1 - Author cases visually (Priority: P1)

An author adds a Switch, configures its Value, adds cases, gives each an exact match value and chooses the activity to run. Default remains available separately. The canvas exposes Add case and the inspector exposes Cases without requiring JSON editing.

**Independent Test**: From an empty Switch, add Approved and Rejected cases, populate a branch, rename its match, and confirm the same branch remains. Undo and redo each structural edit and reload the saved document.

**Acceptance Scenarios**:

1. Given no cases, adding a case creates a unique editable match and a branch that can be opened or populated.
2. Given a populated case, renaming changes only its exact match; descendants and unrelated structure fields remain.
3. Given an existing match, trying that exact value in another case shows an inline error and preserves the valid authored document. Differently cased strings and the empty string remain valid.

### User Story 2 - Edit safely across hosts (Priority: P1)

Workflow and editable Activity Definition graph authors use the same case control. Populated-case deletion is confirmed. Read-only/source-owned views retain navigation but offer no mutations.

**Independent Test**: Repeat on Sequence and Flowchart surfaces, in both hosts; verify narrow keyboard operation and a read-only Activity Definition.

**Acceptance Scenarios**:

1. Removing an empty case needs one action; removing a populated case requires confirmation and can be cancelled without change.
2. Renaming, adding and removing use normal document edits so persistence and undo/redo work.
3. Parallel uses the same collection-slot mechanism for its named branches.

### Edge Cases

Preserve empty strings, whitespace, non-Latin text, brackets and replacement tokens literally. Reject duplicate exact values. Preserve unknown structure/item fields. Discard stale pending edits after external document replacement or selection change. Do not edit unknown/malformed structures or collections with multiple child properties through this small control.

## Requirements

- **FR-001**: Add, rename, remove, choose and open cases visually, keeping Default separate.
- **FR-002**: Preserve case branch contents on rename and require confirmation before deleting contents.
- **FR-003**: Validate exact duplicate values inline without trimming or changing matching rules.
- **FR-004**: Respect read-only/authorization boundaries, keyboard interaction, narrow layouts, undo/redo and normal persistence in both editor hosts.
- **FR-005**: Reuse catalog collection-slot metadata for Switch and Parallel without changing backend contracts.

## Success Criteria

- **SC-001**: A new Switch with two populated cases can be authored and edited without Code view.
- **SC-002**: Case and branch contents survive rename, undo/redo and saved-document reload in both hosts.
- **SC-003**: Focused browser regressions demonstrate a failing baseline and passing implementation; whole affected frontend suites, repository gates and exact-head hosted checks pass, followed by resulting-main verification.

## Assumptions

Existing exact string matching, nullable branch activities, catalog descriptors and persistence contracts remain authoritative. This is bounded `none/free-flow` work. No new runtime behavior, module, public backend API or schema is required.
