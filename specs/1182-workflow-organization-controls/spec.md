# Feature Specification: Workflow organization controls

**Feature Branch**: `agent/codex/3595-workflow-organization-controls`

**Created**: 2026-11-01

**Status**: Implemented — [PR #3599](https://github.com/elsa-workflows/elsa-foundation/pull/3599), bounded follow-up #3595 under #2559.

**Input**: Make workflow organization independently available or unavailable for a backend, retaining the delivered appearance choices and saved organization data.

## User Scenarios & Testing

### User Story 1 - Configure organization availability (Priority: P1)

An administrator decides whether a backend offers folders, tags, saved views and favorites. Existing installations continue to offer them until an administrator changes the setting.

**Why this priority**: Integrators need a simple, reversible product choice independent of appearance and ordinary workflow authoring.

**Independent Test**: Disable organization for one backend, refresh its advertised availability, and browse ordinary workflows with organization controls absent. Re-enable it and observe the controls return.

**Acceptance Scenarios**:

1. **Given** an existing backend with organization support and no explicit choice, **when** Studio connects, **then** existing organization behavior remains enabled.
2. **Given** an authorized administrator and two configured backends, **when** they review and apply the organization setting to one backend, **then** only the selected backend and reviewed setting change.
3. **Given** organization is disabled, **when** Studio refreshes that backend's availability, **then** folder, tag, saved-view and favorite actions disappear while ordinary browsing, paging, search and workflow authoring remain available.
4. **Given** a stale organization action, **when** it reaches a disabled backend, **then** it fails with a truthful unavailable response and does not modify organization data.

### User Story 2 - Keep saved organization (Priority: P1)

An administrator temporarily disables organization and later restores it without recreating folders, tags or saved views.

**Why this priority**: A presentation/product choice must not destroy a workflow library or change its security and execution behavior.

**Independent Test**: Save a parent/child folder, tag assignment, private/shared views and favorite; disable organization, restart, continue normal workflow work, then re-enable and compare all saved identities and values.

**Acceptance Scenarios**:

1. **Given** saved organization data, **when** organization is disabled and the backend restarts, **then** the saved data remains intact.
2. **Given** organization is disabled, **when** an authorized user edits, publishes or runs a workflow, **then** workflow operations retain their existing behavior and do not rewrite its organization.
3. **Given** organization is restored, **when** users browse the library, **then** their original folders, tags, assignments, views and favorites are available with the same ownership and visibility.
4. **Given** tenant/resource access restrictions, **when** the setting changes, **then** the same restrictions continue to govern the workflows a user may read or change.

### User Story 3 - Understand unavailable organization links (Priority: P2)

A user opens a saved browser link or keeps a dialog open while organization availability changes. Studio explains the unavailable operation and offers a clear route back to ordinary browsing.

**Why this priority**: Optional features should leave a usable, understandable workspace without silently changing the meaning of a filtered view.

**Independent Test**: Open an organization-filtered link against a disabled backend at desktop and narrow widths, clear the unavailable organization filters, and browse visible workflows using the keyboard.

**Acceptance Scenarios**:

1. **Given** a link using folder/tag/favorite/view constraints and unavailable organization, **when** the link opens, **then** Studio explains the missing organization support and does not silently display a broader result set.
2. **Given** that explanation, **when** the user clears the unavailable organization constraints, **then** normal browsing resumes while unrelated search/state constraints remain intact.
3. **Given** a saved organization-column choice, **when** organization becomes unavailable and later returns, **then** unavailable organization columns are hidden without losing the saved choice.
4. **Given** a selected backend changes or its availability refreshes, **when** an old request finishes, **then** it cannot restore obsolete controls/results or retarget a pending administrator action.

### Edge Cases

- A backend has no organization storage implementation even though the setting defaults to enabled.
- A module offers only some organization operations; supported operations remain independently discoverable.
- A user lacks administrator or organization-management permissions.
- An organization dialog is open when its backend stops supporting the operation.
- A filtered browser URL or selected view survives a reload or backend switch.
- An administrator changes the setting from source-owned configuration rather than an editable draft.
- Organization is toggled off/on across a backend restart while appearance, optional columns and normal workflow state remain saved.

## Requirements

### Functional Requirements

- **FR-001**: Organization availability MUST be independently configurable by an authorized administrator for the selected backend/shell, using the existing configuration and review/apply flow.
- **FR-002**: The default MUST preserve enabled behavior; unsupported storage implementations MUST remain truthfully unavailable.
- **FR-003**: Disabled organization MUST stop offering its operations and Studio MUST retire their controls, requests and organization-only columns after availability refresh.
- **FR-004**: Ordinary workflow browsing, paging, search, authoring, publishing and execution MUST remain available under their existing authorization and tenancy rules.
- **FR-005**: Disabling/re-enabling MUST preserve all organization data and identities. No data migration, deletion, ownership transfer or authorization change is implied.
- **FR-006**: Direct stale organization operations MUST fail without a write. Ordinary requests containing unavailable organization constraints MUST fail clearly instead of silently broadening their result set.
- **FR-007**: Studio MUST explain unsupported organization-filtered links and provide a keyboard-accessible action that clears only organization constraints.
- **FR-008**: A setting change MUST affect only the reviewed backend/shell and configuration value; delayed requests, cancelled reviews and backend switches MUST preserve existing isolation rules.
- **FR-009**: Re-enabled organization MUST restore saved organization-column choices and continue to honor private/shared view and resource access rules.
- **FR-010**: Administrator/integrator documentation MUST describe defaults, configuration, data retention, refresh/restart behavior, unavailable-link recovery and backend scope.
- **FR-011**: Delivered evidence MUST include actual configured on/off/on behavior, persistence and runtime proof, affected tests, independent review, exact-head and resulting-main gates, with skips/retries/quarantines reported distinctly.

### Key Entities

- **Organization availability**: A backend/shell configuration choice independent of appearance and data ownership.
- **Saved workflow organization**: Existing folders, tags, assignments, views and favorites; their ownership, identifiers and semantics stay unchanged.
- **Browse constraints**: Existing folder/tag/favorite/view constraints in a user's location; unavailable constraints require explicit recovery.

## Success Criteria

### Measurable Outcomes

- **SC-001**: One reviewed setting change controls organization for the selected backend without changing another setting or backend.
- **SC-002**: All saved organization identities and values survive an off/restart/on cycle unchanged.
- **SC-003**: Normal browsing, editing, publishing and an existing published workflow execution succeed with organization disabled under the same permissions.
- **SC-004**: Disabled organization produces zero successful stale organization writes and zero silently broadened organization-filtered views.
- **SC-005**: Desktop and 320px keyboard users can recover from an unavailable organization link and return to ordinary browsing without document overflow.

## Assumptions

- Scope is the missing backend-admin organization control. Delivered visual presets and the folder-layout repair remain independent and available.
- Existing settings authorization, source ownership, dependency composition and revision-checked application are reused.
- No new storage provider, schema, permissions, theme or authentication behavior is introduced. Sign-in diagnosis is a separate work unit if a cause is reproduced.
- Organization does not become an access boundary; current query visibility and authorization contracts remain authoritative.
