# Feature Specification: Shell Feature Management

**Feature Branch**: `codex/shell-feature-management`

**Created**: 2026-06-15

**Status**: Draft

**Input**: User description: "Expose APIs and Studio UI to list, enable, disable, configure, and apply feature changes for the backend-inferred current shell."

## User Scenarios & Testing

### User Story 1 - Review Shell Features (Priority: P1)

A Studio user can open Feature Management and see the features available to the configured backend, including whether each feature is currently enabled, its package/runtime source, and any exposed settings.

**Why this priority**: This is the minimum usable capability and gives the front-end the required feature catalog surface.

**Independent Test**: Point Studio at an Elsa backend with a configured shell and verify `/features` renders enabled and available features with settings metadata.

**Acceptance Scenarios**:

1. **Given** a backend with runtime feature descriptors and a mutable shell configuration, **When** Studio loads Feature Management, **Then** it displays all known features and their enabled state.
2. **Given** a package manifest exposes settings, **When** the feature list is retrieved, **Then** Studio displays setting metadata with type, default, options, category, and sensitivity hints.

---

### User Story 2 - Stage Feature Changes (Priority: P1)

A Studio user can enable or disable features and edit feature settings locally without immediately reloading the backend shell.

**Why this priority**: Users need to review a complete configuration change before applying it.

**Independent Test**: Toggle a feature and edit settings in Studio, then verify the UI shows pending changes while backend shell state is unchanged until Apply.

**Acceptance Scenarios**:

1. **Given** a disabled feature, **When** the user toggles it on, **Then** the UI marks it enabled and dirty without sending an apply request.
2. **Given** an enabled feature with typed settings, **When** the user edits settings, **Then** the UI stores valid JSON-compatible values for the pending apply payload.

---

### User Story 3 - Apply And Reload (Priority: P1)

A Studio user can click Apply to persist staged feature changes for the backend-inferred shell and trigger a shell reload.

**Why this priority**: Feature changes must become active without requiring the user to know shell IDs or call admin APIs manually.

**Independent Test**: Stage changes, click Apply, verify shell configuration changes are persisted and the backend reloads the inferred shell.

**Acceptance Scenarios**:

1. **Given** pending changes and a current catalog revision, **When** the user clicks Apply, **Then** the backend persists the requested feature map, refreshes runtime feature descriptors, reloads the inferred shell, and returns the updated catalog.
2. **Given** a stale revision, **When** the user clicks Apply, **Then** the backend rejects the change with a conflict response and Studio prompts the user to refresh.

### Edge Cases

- Backend cannot infer a mutable shell configuration for the request.
- The shell configuration file changed after Studio loaded its feature revision.
- A setting value cannot be converted to the declared JSON type.
- A package manifest is missing or invalid while the runtime feature is still discoverable.
- A feature exists in shell configuration but no runtime descriptor or manifest exists.

## Requirements

### Functional Requirements

- **FR-001**: The backend MUST expose a shell-scoped feature catalog endpoint for the request-inferred shell.
- **FR-002**: The feature catalog MUST include feature ID, display name, description, source, package metadata, enabled state, configuration, categories, manifest warnings, and setting metadata.
- **FR-003**: The backend MUST expose an apply endpoint that accepts the catalog revision and full desired feature state for the inferred shell.
- **FR-004**: The backend MUST reject stale apply requests with a conflict result.
- **FR-005**: The backend MUST persist enabled features as configuration objects and disabled features as absent from the shell feature map.
- **FR-006**: The backend MUST refresh runtime feature descriptors before reloading the shell.
- **FR-007**: Studio MUST call the configured backend URL and MUST NOT send or display a shell ID in v1.
- **FR-008**: Studio MUST keep feature toggles and setting edits local until the user clicks Apply.
- **FR-009**: Studio MUST use a modular setting-editor registry with built-in editors for boolean, text, password/secret, number, select/options, and JSON textarea settings.
- **FR-010**: The implementation MUST NOT define new global configuration/settings semantics beyond consuming existing package manifest metadata.

### Key Entities

- **Feature Catalog**: Snapshot of available and configured features for the inferred shell.
- **Feature Item**: A single feature's identity, display metadata, enabled state, configuration, and settings metadata.
- **Feature Setting**: Manifest-provided metadata used to choose and validate a Studio editor.
- **Catalog Revision**: Token that detects changes between list and apply.
- **Apply Request**: Full desired shell feature map sent from Studio to the backend.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A user can load the feature catalog, stage at least one enable/disable change, and apply it without manually editing `shells.json`.
- **SC-002**: Applying changes reloads the backend-inferred shell and returns an updated catalog in one user action.
- **SC-003**: Stale revision conflicts are detected before overwriting shell feature configuration.
- **SC-004**: New setting editor components can be registered without changing the Feature Management page.

## Assumptions

- V1 targets JSON-backed shell configuration, matching the current `shells.json` host setup.
- The request route/backend URL determines the shell; shell selection is out of scope for Studio v1.
- Disabling a feature removes it from the shell feature map.
- Existing demo endpoints may remain during this slice; reusable feature logic moves into Modularity packages.
