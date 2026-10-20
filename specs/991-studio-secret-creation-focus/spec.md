# Feature Specification: Keep the required source visible in Studio secret creation

**Feature Branch**: `[agent/codex/3066-secret-creation]`

**Created**: 2026-10-20

**Status**: Implemented

**Input**: [Foundation UX issue #3066](https://github.com/elsa-workflows/elsa-foundation/issues/3066), “Studio UX: Make secret creation focus on name and value before optional metadata,” under [epic #3057](https://github.com/elsa-workflows/elsa-foundation/issues/3057) in [Simplification program #2559](https://github.com/elsa-workflows/elsa-foundation/issues/2559).

## User Scenarios & Testing

### User Story 1 - Enter the required secret source first (Priority: P1)

As a Studio user creating a secret, I want its technical name, type, and required value or configuration key to lead the form, so I can complete the main task without optional details pushing the required source out of view.

This story covers acceptance criterion 1 in [issue #3066](https://github.com/elsa-workflows/elsa-foundation/issues/3066).

**Why this priority**: The value or configuration key is essential to creation and should be available in the ordinary path.

**Independent Test**: At 1280×800 with the actual host styles, open Create with the ordinary Text value-backed store selected and no scrolling. Verify that Technical name and Type precede the required source and the whole Value control fits inside the visible dialog content. Repeat with Configuration selected when the dialog opens and verify that the whole Configuration key control fits. Then switch stores and verify that source text, creation behavior, scrolling, and focus retain their existing behavior.

**Acceptance Scenarios**:

1. **Given** the ordinary value-backed store is selected, **When** the user opens Create, **Then** Technical name, Type, and Value appear before optional metadata and verbose descriptor help, and Expires remains directly available.
2. **Given** the user opens Create with a Configuration store selected, **When** the form appears, **Then** Configuration key is directly available as the required source and retains the existing trim-on-submit behavior.

---

### User Story 2 - Find optional choices without losing context (Priority: P2)

As a Studio user, I want optional details grouped together while the selected store and scope meaning remain clear, so I can configure them when needed without obscuring the required source.

This story covers acceptance criterion 2 in [issue #3066](https://github.com/elsa-workflows/elsa-foundation/issues/3066).

**Why this priority**: Store, Scope, and descriptive metadata are useful when needed, but they should not delay the required source or hide the meaning of the current scope.

**Independent Test**: In the ordinary default-store path, verify that More options starts collapsed, opens to the optional fields, and preserves the existing descriptor labels and canonical scope guidance outside the disclosure. Also verify that the selected-store context and Expires remain directly available and that an empty Store state can be recovered through the existing selectors.

**Acceptance Scenarios**:

1. **Given** no optional metadata has been authored and the existing default Store remains selected, **When** the user opens Create, **Then** More options starts closed and the default Store is not counted as authored configuration.
2. **Given** the user opens More options, **When** they inspect the optional fields, **Then** Display name, Description, Tags, Store, and Scope are grouped there while Expires, compact selected-store context, and the canonical scope guidance remain directly available.
3. **Given** changing Type leaves the current Store selection empty, **When** the user continues using the form, **Then** the Store selector remains directly reachable and Type remains available to restore a supported Store using the existing behavior.

---

### User Story 3 - Preserve authored values and recover safely (Priority: P3)

As a Studio user, I want authored optional values, masking, and recovery behavior to remain intact, so disclosure changes do not lose my work or expose secret material.

This story covers acceptance criterion 3 in [issue #3066](https://github.com/elsa-workflows/elsa-foundation/issues/3066).

**Why this priority**: The presentation change must preserve current creation behavior, cancellation, error recovery, and the boundary around secret values.

**Independent Test**: Exercise collapse and reopen, Store and Type changes, scope-suggestion failure and retry, save failure, cancellation, and creation. Verify retained values, masking, focus and scroll behavior, and the existing creation outcome. Confirm that the form never loads a previously stored Secret value to display it.

**Acceptance Scenarios**:

1. **Given** optional values and a custom Scope have been authored, **When** the user collapses and reopens More options, **Then** those values, including their exact text, remain unchanged.
2. **Given** the scope catalog is unavailable while a custom Scope is present or focused, **When** the user retries or continues, **Then** More options opens for recovery, the custom Scope remains intact, and creation eligibility does not change solely because suggestions failed.
3. **Given** a save error or cancellation, **When** the user corrects the form or exits, **Then** current field retention, the Value Show/Hide behavior and remasking when its control is recreated, focus, Cancel, and Escape behavior remain unchanged.
4. **Given** the user opens Create and enters a new value or configuration key, **When** the form loads, submits, or fails, **Then** the authored source is not replaced by previously stored material and no existing Secret value is loaded or displayed.

### Edge Cases

- The current Store can be empty after an existing Type change; keep Store and Type directly reachable and add no new eligibility rule.
- A Configuration store uses Configuration key instead of Value. Switching stores retains the currently held text and preserves the existing submission and masking behavior.
- Type or Store descriptors can omit display names or descriptions; show only available names and do not infer storage guarantees.
- Optional values may be empty, defaulted, or authored. Only authored optional metadata and a Store changed from its existing default contribute to the generic count.
- Scope suggestions can fail while a custom Scope is present or focused; recovery must not erase that value.
- Save errors and cancellation preserve current retention, masking, and focus behavior.

## Requirements

### Functional Requirements

- **FR-001**: The Create flow MUST present the existing editable Technical name and Type before the required source field. The selected Value or Configuration key MUST remain directly available in the primary form.
- **FR-002**: Expires MUST remain directly available outside More options and retain its current conversion and policy.
- **FR-003**: Display name, Description, Tags, Store, and Scope MUST be grouped under the shared More options disclosure. An empty current Store selection MUST remain directly reachable under existing behavior. Existing defaults and Store eligibility MUST remain unchanged.
- **FR-004**: More options MUST start closed when optional values are unconfigured and the existing default Store is selected. A generic count MUST count only authored optional configuration and a Store changed from its default, without exposing field contents or counting the default Store, Technical name, Value, Configuration key, or Expires. Authored values MUST remain available unchanged after collapse and reopen.
- **FR-005**: Compact selected-store context, available descriptor copy, and the existing canonical Scope matching-versus-access guidance MUST remain visible outside More options without displacing the required source. Missing descriptor copy MUST NOT be replaced with a storage guarantee.
- **FR-006**: Creation MUST preserve current requiredness, normalization, defaults, Store switching, Scope matching, expiry, field retention, the Value Show/Hide behavior and remasking when its control is recreated, submitted values and optional-field presence or omission, errors, cancellation, and focus behavior.
- **FR-007**: Scope-suggestion failure MUST remain advisory. Recovery MUST keep the existing custom Scope and MUST NOT change creation eligibility solely because suggestions failed.
- **FR-008**: The dialog MUST NOT load or display a previously stored Secret value. Metadata summaries MUST NOT expose the authored Value or Configuration key.

### Key Entities

- **Secret being created**: The new Secret's technical name, type, required Value or Configuration key, and any optional descriptive metadata, Store, Scope, or expiry.
- **Create-dialog state**: The current field values, Store selection, disclosure state, and local Value reveal state while the user creates a Secret.

## Success Criteria

### Measurable Outcomes

- **SC-001**: At 1280×800 with actual host styles, the entire required Value control in the ordinary Text value-backed path is visible within the dialog content before scrolling; the same is true for Configuration key when a Configuration store is selected before the dialog opens.
- **SC-002**: In the ordinary default-store path with no authored optional metadata, More options is initially collapsed while the required source, Expires, selected-store context, and canonical Scope guidance remain directly available.
- **SC-003**: Collapse and reopen, and current Store and Type switching, preserve authored values and produce the same creation outcome for the same inputs, including existing defaults, normalization, Scope matching, expiry, and masking behavior.
- **SC-004**: The Create flow performs zero reads of previously stored Secret values, and no metadata summary reveals the authored Value or Configuration key.

## Assumptions

- Issue #3066 builds on the completed and verified prerequisites [#3060](https://github.com/elsa-workflows/elsa-foundation/issues/3060) and [#3065](https://github.com/elsa-workflows/elsa-foundation/issues/3065).
- The specification was prepared on [main at `04ab513`](https://github.com/elsa-workflows/elsa-foundation/commit/04ab513208cee2934dbe3765cae63d2b498aa5cb). The relevant Secrets and shared disclosure source was verified byte-identical there to the reviewed source at [`4c933f7`](https://github.com/elsa-workflows/elsa-foundation/commit/4c933f768f4cb11b16249d0f5a971312cd293f56).
- Existing creation requiredness, normalization, defaults, eligibility, Scope matching, expiry, masking, and error and focus behavior are authoritative; they should be checked again when planning the implementation.
- Type and Store display text may be absent. The form uses the available descriptor or Store name without implying storage guarantees.
- Create remains a new-Secret flow; it does not gain an existing-Secret selector or retrieval capability.
- This work changes the Create-dialog presentation and adds no Store type, provider, name-generation rule, or change to secret creation semantics.
