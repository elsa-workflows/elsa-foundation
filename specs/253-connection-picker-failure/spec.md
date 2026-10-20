# Feature Specification: Explain Connection picker load failures

**Feature Branch**: `agent/codex/3102-connection-picker-failure`
**Created**: 2026-10-20
**Status**: Approved
**Delivery State**: Root reviewed the specification, plan and task sequence on 2026-10-20; implementation authorized. Verification and delivery remain pending.
**Input**: Issue [#3102](https://github.com/elsa-workflows/elsa-foundation/issues/3102), “Studio UX: Explain Connection picker load failures before offering Retry.”

## Evidence and Context

A rejected metadata lookup currently leaves the Connection selector unavailable and presents Retry without explaining that loading failed. Issue #3102 records a source-level observation and a supporting request fixture; it does not claim that a permission-denied journey was reproduced in a composed Workbench.

## User Scenarios & Testing

### User Story 1 - Understand and recover from a Connection picker load failure (Priority: P1)

A workflow author is choosing a Connection when its metadata cannot be loaded. The editor explains that the list failed to load and gives the author a clear retry action. If the author retries and the list becomes available, the editor recovers without losing the previously stored Connection reference.

**Why this priority**: Without an explanation, authors see a disabled selector and a bare action with no indication of what happened. This small authoring failure blocks the immediate task and is the additional Phase 1 reliability leaf recorded in #3102.

**Independent Test**: Use a metadata-only picker fixture that first rejects and then succeeds. Verify the failure explanation, disabled selector, retry/loading transition, recovered options, and retained reference. Exercise the visible failure and retry in the browser with keyboard input at narrow and desktop widths in light and dark themes.

**Acceptance Scenarios**:

1. **Given** the Connection metadata request fails without a verified status available to the editor, **when** the picker renders its failure state, **then** the author sees a useful, accessible generic load-failure explanation and an identifiable Retry action while selection remains disabled. The message does not claim that access was denied.
2. **Given** a failed load and a stored Connection reference, **when** the author retries, **then** the editor shows loading and can return to a usable picker after success without replacing the stored reference. Changing the connection environment while loading, or leaving the editor, does not let an earlier response change the current view. Disabled selection and the unavailable-reference state remain intact.
3. **Given** a metadata-only browser fixture, **when** the author reaches and recovers from the failed state by keyboard at narrow and desktop widths in light and dark themes, **then** the explanation and Retry action remain accessible and usable. The journey requests and displays no stored secret value.

### Edge Cases

- Repeated request rejection keeps the generic explanation and Retry action available.
- Changing the connection environment while loading, or leaving the editor, must prevent an earlier response from changing the current view or value.
- If a stored reference is absent from a successful metadata response, it remains visible as unavailable and is not silently cleared.
- A failed or loading request keeps selection disabled. The error path does not change the workflow value.
- A failure with no verified response status uses the generic message; the UI does not infer a permission cause.

## Requirements

### Functional Requirements

- **FR-001**: On a failed Connection metadata load, the author MUST receive a useful, accessible inline explanation and a clearly identified retry action. When the failure cause is not verified, the explanation MUST be generic and MUST NOT describe it as denied access.
- **FR-002**: Retrying MUST expose a loading state and allow the picker to recover after a successful load without discarding an existing Connection reference. Changing the connection environment while a request is pending, or leaving the editor, MUST NOT let an earlier response change the current view or value. Disabled selection and unavailable-reference behavior MUST remain intact.
- **FR-003**: Rejection and retry/recovery MUST have bounded regression coverage that demonstrates failure before the change and success after it. Browser proof MUST include keyboard use and narrow/light/dark presentation with fixtures containing only Connection descriptions. Coverage MUST NOT request or display stored secret values.

### Key Entities

- **Connection reference**: The existing opaque workflow value identifying a Connection. This work preserves it and adds no stored fields.
- **Picker metadata state**: The existing loading, available, or failed view of Connection metadata. This work adds no persisted entity or credential data.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In every failed metadata-load scenario, the selector remains disabled and both a generic explanation and a usable Retry action are available to sighted and assistive-technology users.
- **SC-002**: After Retry, the loading state is observable; a successful response restores selection, and an existing reference is unchanged unless the author changes it.
- **SC-003**: The focused browser proof passes for keyboard recovery at narrow and desktop widths in light and dark themes using metadata-only fixtures, with no stored secret value requested or displayed.

## Assumptions

- Current evidence provides no verified reason for a failed load, so the smallest safe explanation is generic for all failures.
- Retry continues to use the existing Connection metadata picker operation.
- No Connection permission policy, credential storage, lifecycle, server behavior, network security settings, module activation, or error-format changes are part of this feature.
