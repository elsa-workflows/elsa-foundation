# Feature Specification: Reconciler Definition-Metadata Update Path

**Feature Branch**: `TBD` (own branch — cross-cutting reconciler behavior)

**Created**: 2026-07-07

**Status**: Draft

**Input**: Extend `WorkflowsVersionReconciler` so a reconciliation pass **updates an existing
definition's mutable metadata (name, description)** from the incoming source model, not only on
first create. Prerequisite for git-sourced renames to propagate
([ADR 0034](../../docs/adr/0034-workflow-definitions-reconcile-from-and-export-to-git.md) D5), and a
correctness fix for every reconciliation source.

**Program goal**: `none/free-flow`. Unblocks [`specs/085`](../085-workflow-definition-gitops/spec.md).

## Context

`WorkflowsVersionReconciler.ReconcileVersion`
(`src/Elsa/Workflows/Design/Reconciliation/Services/WorkflowsVersionReconciler.cs:53`) creates a
definition only when absent (`if (definition is null) addDefinitionCommand.Add(...)`) and **never
updates it**. `WorkflowDefinition.Name`/`Description` are mutable, but a source that changes them
(a rename) is silently ignored on every subsequent pass — git or otherwise. ADR 0034 (D5) needs
`definition.json` to be the latest-wins authority for an existing definition's metadata; this unit
adds the generic update path the reconciler lacks.

Scope note: this unit handles the **source-agnostic** name/description update. The **git-specific**
`definition.json` shape and the soft-delete (`deleted`) flag are `specs/085`'s concern (a delete flag
is not yet on `WorkflowVersionReconciliationModel`).

## User Scenarios & Testing *(mandatory)*

### User Story 1 — A renamed definition propagates on the next pass (Priority: P1)

A source changes a definition's name/description; the next reconciliation pass updates the persisted
definition to match.

**Independent Test**: Reconcile a definition, change the source model's `Name`/`Description`,
reconcile again, assert the persisted `WorkflowDefinition` reflects the new values and that **no new
version rows** are created by the metadata change.

**Acceptance Scenarios**:

1. **Given** an existing definition and an incoming model with a different `Name`, **When** the pass
   runs, **Then** the persisted definition's `Name` is updated (via `ISaveWorkflowDefinitionCommand`).
2. **Given** identical incoming metadata, **When** the pass runs, **Then** no write occurs (idempotent
   — update only on change).
3. **Given** a metadata-only change, **Then** no `WorkflowDefinitionVersion` rows are added or altered
   (retention authority: versions untouched).

### Edge Cases

- Multiple version entries for one definition carrying different names in one pass — define the
  authority (last-entry-wins vs. reject); recommend the source supplies one consistent metadata value
  per definition (git: from `definition.json`).
- Concurrent passes: metadata update must not race version upserts (same pass ordering as today).

## Requirements *(mandatory)*

- **FR-001**: When a reconciled definition already exists and the incoming model's `Name`/`Description`
  differ, the reconciler MUST update the persisted definition via `ISaveWorkflowDefinitionCommand`.
- **FR-002**: The update MUST be **idempotent** — a write happens only when metadata actually changed.
- **FR-003**: A metadata update MUST NOT add or alter any `WorkflowDefinitionVersion` (versions remain
  immutable and retention-authoritative).
- **FR-004**: Behavior MUST be source-agnostic (applies to every `IWorkflowReconciliationSource`, not
  just git).
- **FR-005**: Tests MUST cover the rename-propagates, idempotent-no-op, and versions-untouched cases.
- **FR-006** *(interface with 085)*: Soft-delete (`deleted`) propagation and the `definition.json`
  shape are **out of scope** here; they land in `specs/085`. This unit MAY leave a documented seam
  (e.g. the reconciler applies whatever metadata the model carries) so 085 can extend it without a
  second refactor.

### Key Entities

- **`WorkflowsVersionReconciler`** — gains an injected `ISaveWorkflowDefinitionCommand` and a
  metadata-diff-then-save step in `ReconcileVersion`.
- **`WorkflowVersionReconciliationModel`** — already carries `Name`/`Description`; no schema change
  required for this unit.

## Success Criteria *(mandatory)*

- **SC-001**: A source-side rename is reflected in the catalog after one pass; a no-change pass writes
  nothing.
- **SC-002**: No version rows change on a metadata-only update.
- **SC-003**: Existing reconciliation tests pass; new tests cover the three cases in FR-005.

## Out of Scope / Non-Goals

- Soft-delete propagation and `definition.json` (→ `specs/085`).
- Any change to version reconciliation / Model X dedup semantics.

Prerequisite for [`specs/085`](../085-workflow-definition-gitops/spec.md) (ADR 0034 D5).
