# Feature Specification: Activity Input Editor Options

> **Current status (2026-07-16): retained and reconciled with [spec 095](../095-value-flow-redesign/spec.md).** Editor metadata is discovered from plain `[ActivityInput]` CLR properties and affects authoring only; it does not create runtime argument wrappers or value addresses.

**Feature Branch**: `codex/090-activity-input-editor-options`

**Created**: 2026-07-11

**Status**: Approved for implementation

**Input**: Activity authors need to declare allowable activity-input values, including static strings, typed labeled values, and context-dependent providers, so Studio renders constrained editors such as dropdowns and checklists.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Author constrained activity inputs (Priority: P1)

As an activity author, I can declare an input's allowable values and intended editor so workflow authors cannot accidentally enter unsupported values.

**Why this priority**: Static allowable values solve the immediate `HttpEndpoint.SupportedMethods` usability problem and establish the shared descriptor contract.

**Independent Test**: Catalog a CLR activity with static string and typed labeled options, inspect its descriptor, and verify the declared labels, values, order, and editor intent are preserved.

**Acceptance Scenarios**:

1. **Given** a scalar input with allowable values, **When** its activity descriptor is requested, **Then** the descriptor contains the ordered options and single-selection intent.
2. **Given** a collection input configured as a checklist, **When** its activity descriptor is requested, **Then** the descriptor contains the ordered options and multiple-selection intent.
3. **Given** conflicting, duplicate, blank, incompatible, or otherwise ambiguous option metadata, **When** the CLR activity is reconciled, **Then** reconciliation rejects it with an actionable error.

---

### User Story 2 - Select allowable values in Studio (Priority: P1)

As a workflow author, I see a control appropriate to the descriptor: a dropdown for one value or a checklist for multiple values, including clear handling of values that are no longer available.

**Why this priority**: Descriptor metadata has no user value until Studio consistently converts it into safe authoring controls.

**Independent Test**: Open properties for scalar and collection descriptor fixtures and verify selection, persistence, stale-value warnings, and expression syntax behavior without a running options provider.

**Acceptance Scenarios**:

1. **Given** a scalar descriptor with options, **When** its properties are shown, **Then** Studio renders a single-select dropdown and preserves the option's original value type.
2. **Given** a collection descriptor with checklist intent, **When** its properties are shown, **Then** Studio renders a distinct multi-select checklist without duplicate values.
3. **Given** a saved value absent from the current option set, **When** options are displayed, **Then** Studio preserves and flags the unavailable value until the author explicitly changes it.

---

### User Story 3 - Resolve context-dependent options (Priority: P2)

As an activity author, I can associate an input with a registered provider whose options depend on the current workflow and activity, and Studio refreshes those options when declared dependencies change.

**Why this priority**: Dynamic providers enable inputs whose valid values cannot be known when the activity catalog is built, while keeping static options simple.

**Independent Test**: Register a provider, request options for a current workflow/activity snapshot, modify a declared dependency, and verify Studio refreshes while preserving values and surfacing retryable failures.

**Acceptance Scenarios**:

1. **Given** a descriptor containing a provider key, **When** Studio requests options, **Then** the registered provider receives the validated workflow state, activity node, and input definition.
2. **Given** a declared dependency changes, **When** the debounce interval elapses, **Then** Studio cancels obsolete requests and displays only the newest option result.
3. **Given** the provider is missing or fails, **When** Studio loads the input, **Then** the constrained editor remains disabled, the current value is preserved, and an inline retry action is available.

### Edge Cases

- Static shorthand, typed option attributes, and provider metadata are mutually exclusive where combining them would make precedence ambiguous.
- Provider keys and dependency names are nonblank and case-sensitive; dependency names must identify inputs on the same activity.
- Duplicate option values are rejected even when their labels differ; repeated labels with distinct values remain valid.
- Supported typed values are JSON scalars: strings, booleans, JavaScript-safe numbers, and enum names. Integral values outside ±9,007,199,254,740,991, non-finite values, and decimals that cannot round-trip exactly through a browser number are rejected. Complex object and null options are outside this work unit.
- Provider requests cannot select arbitrary provider keys; the server resolves the key from the cataloged activity input.
- Provider cancellation is not converted into a provider failure.
- Existing enum inference remains available when author-provided option metadata is absent.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Activity authors MUST be able to declare an input UI hint, ordered string options, ordered typed labeled options, or a stable dynamic provider key with dependency input names.
- **FR-002**: Static shorthand options MUST use each string as both label and value.
- **FR-003**: Typed labeled options MUST support string, boolean, numeric, and enum values and MUST preserve their JSON scalar type in the descriptor.
- **FR-004**: CLR reconciliation MUST translate author metadata into the existing activity input UI-specification field without replacing its opaque persistence contract.
- **FR-005**: CLR reconciliation MUST fail with actionable diagnostics for conflicting sources, blank keys/labels/values, duplicate values, unknown dependencies, dependencies without providers, or values incompatible with the input's scalar or collection-element type.
- **FR-006**: The closest property declaration in an inheritance chain MUST replace inherited option declarations; inherited input metadata MUST continue to apply when the derived property supplies no replacement.
- **FR-007**: Author-supplied UI specifications MUST take precedence over inferred enum options, and existing enum inference MUST remain unchanged when author metadata is absent.
- **FR-008**: Dynamic providers MUST be registered under unique, case-sensitive keys and MUST receive the validated current workflow state, selected activity node, and activity input definition.
- **FR-009**: Duplicate provider keys MUST fail during host startup; a missing or failed provider MUST produce a sanitized, retryable unavailable response while internal details are logged.
- **FR-010**: The dynamic options operation MUST derive the provider key from the cataloged descriptor, reject mismatched workflow/activity/input requests, propagate cancellation, and prevent response caching.
- **FR-011**: Studio MUST render scalar options as a single-select dropdown by default and collection options as a checklist by default.
- **FR-012**: Explicit checklist intent MUST claim the whole collection; explicit dropdown intent on a collection MUST retain collection-row authoring with a dropdown per element.
- **FR-013**: Studio MUST preserve typed option values when authoring activity inputs.
- **FR-014**: Studio MUST fetch provider options on editor open and 150 milliseconds after a declared dependency changes, cancel obsolete requests, and offer manual retry.
- **FR-015**: Studio MUST preserve and visibly flag authored values that are absent from the current option set and MUST never clear them automatically.
- **FR-016**: Provider failure MUST disable the constrained editor and MUST NOT fall back to unconstrained free-text editing.
- **FR-017**: `HttpEndpoint.SupportedMethods` MUST advertise GET, POST, PUT, HEAD, and DELETE as checklist options.
- **FR-018**: Public provider and descriptor contracts MUST be documented as extension points, and relevant generated navigation maps MUST be refreshed.

### Key Entities

- **Activity Input Option**: An ordered display label and JSON-scalar authored value.
- **Input UI Specification**: Opaque design-time metadata containing either static options or dynamic provider metadata.
- **Options Provider Descriptor**: A stable provider key plus names of activity inputs that invalidate its current results.
- **Options Provider Context**: The validated workflow state, selected activity node, activity input definition, and cancellation scope used to resolve dynamic options.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A workflow author can configure `SupportedMethods` using five visible checkboxes without typing or manipulating raw collection rows.
- **SC-002**: All supported scalar option types round-trip through selection and workflow authoring without conversion to strings.
- **SC-003**: Invalid activity option declarations are rejected before an activity version becomes available in the catalog, with the offending input identified.
- **SC-004**: After a declared dependency changes, Studio displays only options from the latest request and does so after no more than the specified 150-millisecond debounce plus provider response time.
- **SC-005**: Missing, failing, and stale provider results cause no silent workflow value changes and always offer a visible recovery path.
- **SC-006**: Existing option-free inputs and enum inputs retain their current editor and descriptor behavior.

## Assumptions

- Work remains in the `none/free-flow` program-goal state.
- Existing workflow-management authentication and authorization protect the new options operation.
- Dynamic provider implementations live in design-side modules; runtime activity libraries only persist a stable provider key.
- Complex object options, null options, grouped options, pagination, and provider-result caching are outside this work unit.
- The draft constitutions are applied as provisional quality gates without amendment.
