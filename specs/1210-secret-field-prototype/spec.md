# Feature Specification: Compact Secret field prototype

**Feature Branch**: `agent/codex/3851-secret-field-prototype`
**Created**: 2026-11-05
**Status**: Draft
**Issue**: #3851; program #2559
**Input**: Prototype compact Material-inspired fields in Create secret across all themes and field states before shaping shared components and wider rollout.

## User Scenarios & Testing

### User Story 1 — Create a secret with compact, clear fields (Priority: P1)

An administrator opens Create secret, identifies each field, enters a name and value, optionally sets expiry and metadata, and creates the secret without losing labels or guidance.

**Why this priority**: This is the dialog in the owner's reference and the smallest useful prototype.
**Independent Test**: Create, find and reopen a secret on an owned disposable composed host; cancel another draft and verify focus restoration.

**Acceptance Scenarios**:
1. Given an empty form, when a text field receives focus or a value, then its label remains readable and never overlaps entered content.
2. Given a populated form, when name validation fails and is corrected, then the error is associated with that field and saving becomes available after recovery.
3. Given type-specific editors, when the user changes type/store, then password, multiline and configuration-key fields remain usable and supporting text sits beside the appropriate control.
4. Given pending creation, when the user tries to edit or submit again, then fields and actions communicate the pending state and prevent duplicate submission; a failure permits retry without discarding the draft.
5. Given More options, when expanded, then display name, description, tags, store and suggested/custom scope use consistent labels and field help.

### User Story 2 — Evaluate the design across appearances and states (Priority: P2)

A reviewer switches every built-in theme and supported appearance and compares empty, populated, focused, invalid/recovered, disabled, read-only, loading/error/retry and narrow states.

**Why this priority**: The prototype informs a future shared contract rather than one theme-specific fix.
**Independent Test**: A reproducible interactive preview and browser matrix retain captures for each built-in theme/appearance plus state and accessibility checks.

**Acceptance Scenarios**:
1. Given any built-in theme/appearance, when opening the dialog and entering values, then labels and content remain readable, controls retain the theme's surface/shape, and focus is visible.
2. Given a narrow viewport or enlarged text, when expanding options or showing errors, then content scrolls inside the dialog and actions remain reachable without horizontal page scrolling.
3. Given reduced motion or forced colors, when navigating by keyboard, then field names, boundaries and focus remain identifiable.

### Edge Cases

Autofilled/password-manager content, long localized labels, long help/error messages, multiline key/certificate values, empty dates with native prompts, long selected options, custom scopes, no suggestions, failed catalog refresh, disabled versus read-only fields and textarea resizing.

## Requirements

### Functional Requirements

- **FR-001**: Prototype scope is Create secret and its compact scope picker. Other dialogs and forms retain their current treatment.
- **FR-002**: Field labels MUST stay visible when focused or populated, including autofill; native select/date prompts and multiline content MUST not overlap labels.
- **FR-003**: Help and errors MUST be visually adjacent and programmatically associated with the corresponding field; placeholder text MUST not be the sole label.
- **FR-004**: Existing password conceal/reveal, keyboard, validation, cancellation, submission and draft-recovery semantics MUST work.
- **FR-005**: Fields MUST use the selected theme's semantic roles and support every built-in theme/appearance. Existing custom themes inherit those roles; arbitrary external themes are not claimed as inspected.
- **FR-006**: The prototype MUST expose representative read-only and disabled states for evaluating a future shared field contract, without changing host authorization.
- **FR-007**: Retain current-revision captures and distinguish isolated preview evidence, composed-host journey results and independent visual acceptance.
- **FR-008**: Document what should move into shared components and which controls need exceptions before a wider migration.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Every built-in theme and supported appearance has executed, retained empty/populated captures with no clipped labels or horizontal page overflow.
- **SC-002**: Every named field state has executed coverage; failures/skips remain visible and cannot count as acceptance.
- **SC-003**: The default collapsed dialog has less vertical field overhead than the reference and keeps Create/Cancel visible at 1280×800; report measured before/after dimensions.
- **SC-004**: A real composed-host create/find/reopen/cancel journey succeeds with draft validation and focus behavior verified.

## Assumptions

The owner's supplied Material floating-label image is the interaction reference; Studio's own form styling and design-token contract remain authoritative for theme appearance. This is a reviewable prototype; shared API extraction, unrelated forms, a new component library and product-wide rollout are follow-on work. The preview uses disposable demonstration values only.
