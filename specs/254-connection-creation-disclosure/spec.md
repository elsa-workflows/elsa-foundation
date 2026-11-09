# Feature Specification: Connection Creation Disclosure

**Feature Branch**: `agent/codex/3065-connection-creation`

**Created**: 2026-10-20

**Status**: Implemented

**Delivery State**: Implementation is in PR #3105. The current local review-fix source passes 46/46 Connections tests (30/30 focused) and 35/35 related browser cases (including 9/9 Connections management cases); other local results are recorded in tasks.md. Round-one findings have local fixes; revised-head independent review, hosted exact-head CI, merge, and resulting-main verification remain pending.

**Input**: User description: Issue #3065, “Studio UX: Prioritize credentials and relevant options in connection creation.”

## User Scenarios & Testing

### User Story 1 - Create a connection with relevant options (Priority: P1)

As a Studio operator creating an HTTP API-key connection, I can enter the credential on the primary path, see only relevant optional fields, and understand the network restriction before I submit. If an advanced setting or Secret capability fails, I can recover without losing my work, and stored Secret values never appear in the form.

**Why this priority**: This is the issue’s common connection-creation path and its security restrictions must remain understandable and effective.

**Independent Test**: Create the built-in HTTP API-key connection with an existing Secret and with a new credential; exercise both address choices, placement switching, errors, and keyboard disclosure controls using metadata-only Secret selection.

**Acceptance Scenarios**:

1. **Given** a new built-in HTTP API-key connection, **When** the operator reaches the credential step, **Then** Existing Secret and New value appear before Base URL, Base URL remains directly available after the credential, and optional network and metadata settings remain secondary while the address restriction and safe defaults stay intact.
2. **Given** the built-in HTTP API-key scheme, **When** the operator chooses a supported placement, **Then** only that placement’s optional name field is shown, both authored name values remain preserved and submitted across switching, unsupported values are rejected, and unfamiliar contributed schemes retain generic descriptor-driven fields.
3. **Given** an authored or invalid advanced value, a denied or unavailable Secret capability, or a create failure, **When** the operator corrects or retries the form, **Then** relevant values remain discoverable and retained, the failure has useful feedback, and no stored Secret value is loaded for display.

### Edge Cases

- Both HTTP address fields are optional and empty; either Base URL or Allowed hosts must satisfy the restriction when both fields are present in the recognized descriptor.
- One or both HTTP address fields are descriptor-required; keep required validation and direct controls, and do not show either/or guidance that contradicts the required field.
- One address field is unavailable in incomplete or unfamiliar descriptor metadata; retain generic validation and backend error behavior instead of inventing an unsatisfiable client gate.
- Placement is missing or whitespace-only, uses a supported value with different casing, or contains an unsupported nonblank value (including a value with trailing whitespace).
- Header and query names are both authored; switching placement preserves both values while showing only the relevant optional name.
- A descriptor marks a name or contributed field required; it remains directly visible and required regardless of relevance-based grouping.
- A user explicitly enters a name equal to a runtime default, toggles the insecure-HTTP setting on and back off, or corrects an error while an advanced field is focused.
- Existing Secret selection is denied, unavailable, cancelled, retried, changed to New value, or followed by a create failure.

## Requirements

### Functional Requirements

- **FR-001**: The built-in HTTP API-key path MUST present the required credential before Base URL and other HTTP settings, while keeping Base URL directly available after the credential. It MUST retain the existing Existing Secret and New value choices.
- **FR-002**: When a recognized HTTP descriptor supplies both Base URL and Allowed hosts as optional fields, creation MUST require at least one nonblank value and show the guidance “Enter the Base URL above or configure Allowed hosts in More options.” When either address field is descriptor-required, its direct control and mandatory validation MUST remain, and the form MUST NOT show either/or guidance. If either recognized address field is missing or unfamiliar, the form MUST retain generic descriptor validation and the existing backend error path.
- **FR-003**: The built-in API-key placement editor MUST recognize the verified Header and Query contract values case-insensitively. A missing or whitespace-only value MUST act as Header without authoring a placement setting; unsupported nonblank values MUST remain invalid rather than being silently changed. New selections MUST use canonical lowercase values.
- **FR-004**: For the recognized built-in HTTP API-key scheme, only the name field relevant to the effective placement SHOULD be shown when optional. Both authored name values MUST be retained in form state and submitted across switching, including while one is hidden. Descriptor-required address and other fields MUST remain direct controls with mandatory validation; required or unrecognized fields MUST be rendered exactly once. Optional inactive name fields may be hidden.
- **FR-005**: The form MUST preserve safe, un-authored defaults: effective placement Header; helper or placeholder values Authorization and api_key; and Allow insecure HTTP off. Blank names and untouched defaults MUST be omitted from the submitted settings. Turning Allow insecure HTTP on and back off MUST omit its setting. An explicitly authored override, including a value equal to a runtime default, MUST remain configured and count as an authored advanced value.
- **FR-006**: “More options” MUST begin collapsed for an empty new form and support keyboard operation with accessible expanded-state and control relationships. A real advanced error MAY reveal the section; after correction it MUST remain open until the operator manually collapses it. Recovery MUST preserve child identity, controlled values, and useful focus, and an automatic reveal MUST NOT be saved as the operator’s disclosure preference. A different draft/activity MUST not inherit the prior activity’s automatic reveal.
- **FR-007**: The HTTP address one-of gate MUST apply to recognized HTTP descriptors that provide both address fields, independent of authentication scheme. The either/or guidance MUST appear only when both fields are optional. Descriptor-required address fields MUST remain direct and mandatory without either/or guidance. API-key placement and relevant-name handling MUST be limited to the exact built-in HTTP/API-key combination. Unknown types, other schemes, and contributed fields MUST retain generic descriptor-driven presentation and validation; field meaning MUST NOT be inferred from labels or names on other schemes.
- **FR-008**: Secret selection MUST remain metadata-only. The form MUST never load or display stored Secret values; it MUST preserve the existing reference-only or operator-entered-value submission paths, eligibility and permission checks, denial/unavailable feedback, retry, cancellation, stale-endpoint isolation, and duplicate-submit protection.
- **FR-009**: Validation, denied/unavailable capability, and create failures MUST provide actionable feedback and a recovery path without adding a new error classification or changing backend, descriptor, endpoint, Secret policy, persistence, or public SDK contracts.

### Key Entities

- **Connection draft**: The operator’s current name, type, scheme, settings, and credential source before submission.
- **Authored advanced setting**: A setting the operator explicitly entered or selected; distinguished from an effective runtime default that was never authored.
- **Secret selection**: Either an eligible Secret reference chosen from metadata or a new operator-entered value; stored values are not part of the draft’s display data.
- **Connection descriptor**: The existing description of supported types, schemes, settings, requiredness, sensitivity, and available field metadata.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In the built-in HTTP API-key flow, the required credential controls appear before Base URL, with Base URL still directly available; when both address fields are optional, Create remains unavailable until either supported address is supplied, while descriptor-required fields remain mandatory without either/or guidance.
- **SC-002**: For supported placement choices, only the relevant optional name is visible, both authored names survive mode changes, and submitted settings contain authored nonempty values—including a preserved inactive name—while omitting untouched defaults.
- **SC-003**: Unknown types and schemes continue to use generic descriptor behavior, with no field hidden, duplicated, or silently coerced.
- **SC-004**: A user can correct an advanced-field failure without the section collapsing or losing the field value or focused control; a manual collapse remains possible afterward.
- **SC-005**: Existing Secret and New value creation journeys remain available, while the browser and test evidence shows no stored Secret value requested or displayed.

## Assumptions

- The issue’s built-in HTTP/API-key path is identified by the existing type and scheme metadata; other contributed schemes remain generic.
- The supported placement values and default behavior come from the verified existing runtime contract. The descriptor does not advertise a placement enum, and this work does not add one.
- Existing Secret eligibility, permissions, metadata lookup, and submission behavior are retained as established by the existing Secret picker contract.
- This work is a Studio client presentation and validation change, plus an internal dependency on the already exported shared Studio UI package if required. No backend/API/public SDK or Secret policy change is in scope.
