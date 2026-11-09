# Feature Specification: Guide HTTP activity configuration with safe progressive disclosure

**Feature Branch**: `agent/codex/3064-http-configuration`

**Created**: 2026-10-20

**Status**: Implemented — #3064; the implementation PR carries this transition and the final delivery receipt under the spec lifecycle.

**Input**: GitHub issue #3064, “Studio UX: Guide HTTP activity configuration with safe progressive disclosure”

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Configure a straightforward HTTP request (Priority: P1)

A workflow author opens a new HTTP request activity and can immediately identify where to enter the destination URL, choose a method, and select a connection. The request body remains available, and content type and less frequently used controls are accessible in a clearly labeled secondary-options section.

**Why this priority**: These are the choices needed to understand and configure the common request path. Clear hierarchy reduces scanning without removing capabilities.

**Independent Test**: Open a new HTTP request activity and verify the first view presents URL, method, and connection; the body is available; and content type, headers, expected status, and timeout can be reached in secondary options.

**Acceptance Scenarios**:

1. **Given** a new HTTP request activity, **When** its settings open, **Then** URL, method, and connection are prominent, and the body remains available.
2. **Given** the same activity, **When** the author opens secondary options, **Then** content type, headers, expected status, and timeout are available under a clear, activity-appropriate label.

### User Story 2 - Keep authored request settings safe (Priority: P1)

An author who has already configured request content, bindings, conversions, or advanced values can simplify the view and change the method without losing or silently changing that work. Any configured or invalid advanced state remains visible or is clearly signaled while its section is collapsed.

**Why this priority**: Safe disclosure must preserve existing workflows and make hidden configuration discoverable.

**Independent Test**: Save a request with a body and with advanced values that include a binding, a value equal to a declared default, a non-default value, and a validation error. Collapse and reopen secondary options, change among methods including GET and HEAD, then verify every value and its meaning remain intact.

**Acceptance Scenarios**:

1. **Given** an existing request body and content type, **When** the author changes the method to GET or HEAD, **Then** the body and content type remain available and their values are not cleared or changed.
2. **Given** a secondary option with an authored or bound value, **When** the section is collapsed, **Then** the configured state remains visible or is clearly indicated; **When** it is reopened, **Then** the value and its binding or conversion are unchanged.
3. **Given** a secondary option with a validation error, **When** the author views the activity, **Then** the error is visible or clearly indicated even if the section is collapsed.
4. **Given** an authored value equal to its declared default, **When** the author collapses and reopens secondary options, **Then** it is still treated as configured and is preserved.

### User Story 3 - Use activities with older or unfamiliar descriptors (Priority: P2)

A workflow author can still reach every available input when an activity descriptor does not provide recognized grouping information. Required inputs remain available regardless of their presentation grouping.

**Why this priority**: Safe fallback keeps the change compatible with existing and third-party activities.

**Independent Test**: View descriptors with no grouping metadata, unrecognized grouping values, and a required input classified as optional; verify available inputs are not hidden and required inputs remain accessible.

**Acceptance Scenarios**:

1. **Given** an activity descriptor with absent or unrecognized grouping metadata, **When** its settings open, **Then** its available inputs remain visible through a safe fallback.
2. **Given** a required input whose presentation tier is optional, **When** the activity settings open, **Then** the input remains visible and its requiredness is not changed.

### Edge Cases

- An existing body is authored while the selected method is GET or HEAD.
- A configured value equals the descriptor's declared default, while a separate default is only supplied automatically.
- A secondary value uses a binding or conversion, or has a validation error.
- Grouping metadata is absent, null, or unfamiliar; a required input is marked optional.
- The secondary section is collapsed, reopened, and collapsed again after values have been authored.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The HTTP request activity MUST present URL, method, and connection as prominent configuration choices.
- **FR-002**: The request body MUST remain available for authoring with any selected method, including GET and HEAD. Changing methods MUST NOT clear or alter an existing body.
- **FR-003**: Content type MUST remain available to the author alongside the activity's secondary request options. Changing methods MUST NOT clear or alter an existing content type.
- **FR-004**: Headers, expected status, and timeout MUST be available in a clearly labeled secondary-options section that follows Studio’s shared disclosure interaction. Its label MUST be understandable in the context of the activity.
- **FR-005**: Collapsing or reopening secondary options MUST preserve authored values and their meaning, including bindings, expression syntax, and conversions.
- **FR-006**: Configured, bound, non-default, explicitly authored default-valued, and invalid secondary values MUST remain visible or be clearly indicated while collapsed. Opening the section MUST expose the value and any validation state.
- **FR-007**: A value supplied automatically from a descriptor default MUST NOT be mistaken for an authored value solely because it appears in the editor.
- **FR-008**: Input grouping MUST use descriptor-provided metadata rather than input-name guesses. Missing or unrecognized grouping metadata MUST use a safe fallback that leaves browsable inputs visible without opening secondary options. Existing category labels and input order MUST remain coherent.
- **FR-009**: Requiredness MUST remain independent of presentation grouping. A required input MUST stay visible without opening secondary options when its grouping is optional; presentation changes MUST NOT make the optional connection required at runtime.
- **FR-010**: The module-author guidance MUST document the `essential`, `optional`, and `setting` grouping values, explain that grouping is for presentation and is separate from requiredness, and describe the fallback for missing or unrecognized values.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In a new HTTP request activity, an author can identify URL, method, and connection without opening secondary options; the request body remains available.
- **SC-002**: Content type, headers, expected status, and timeout can each be reached through one clearly labeled secondary-options section.
- **SC-003**: In the acceptance matrix of configured, bound, default-valued, non-default, and invalid secondary values, every value is either visible while collapsed or has a clear configured/error indicator; reopening exposes the unchanged value and state.
- **SC-004**: Switching methods, including to GET and HEAD, and collapsing/reopening options causes no loss or change to the body's contents, authored settings, bindings, syntax, or conversions.
- **SC-005**: Descriptors with missing or unfamiliar grouping metadata keep their browsable inputs visible without opening secondary options, and required inputs remain visible regardless of grouping.

## Assumptions

- This work changes how inputs are presented and documented; it does not change HTTP execution behavior or introduce method-specific rules for request-body applicability.
- Connection prominence is a presentation choice and does not make connection selection mandatory at runtime.
- Existing descriptor metadata is the source for input grouping; missing or unfamiliar metadata can occur and must degrade safely.
- “Available” means the author can configure the body and reach content type through the activity's settings. No automatic content-type selection or method-dependent hiding is implied.
- This is the #3064 work unit within epic #3057 and the existing Elsa 4 Simplification program #2559. Its prerequisite #3058 is merged and accepted; #3092 is merged with resulting-main verification pending. No optional modules or services are enabled by this work.
