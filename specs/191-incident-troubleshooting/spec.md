# Feature Specification: Workflow Incident Troubleshooting

**Feature Branch**: `1308-incident-troubleshooting`

**Created**: 2026-10-02

**Status**: Approved — product owner requested end-to-end delivery of the reviewed QA plan.

**Input**: Make runtime incidents obvious and intuitive when inspecting a workflow run, starting with an activity input that references an undefined variable.

**Program**: [#2334](https://github.com/elsa-workflows/elsa-foundation/issues/2334)

**Feature**: [#2336](https://github.com/elsa-workflows/elsa-foundation/issues/2336)

## User Scenarios & Testing

### User Story 1 — Recognize a run needing intervention (Priority: P1)

An operator scans workflow runs and can distinguish a healthy run from one containing active incidents, without conflating execution lifecycle with incident health.

**Why this priority**: An unnoticed incident cannot be investigated.

**Independent Test**: Display healthy, blocking, nonblocking and resolved-incident runs together, then filter for active/blocking incidents and open a result.

**Acceptance Scenarios**:

1. **Given** a Running run with a blocking incident, **When** its list row or viewer is displayed, **Then** a visible icon/count and intervention label show the problem alongside its truthful lifecycle status.
2. **Given** matching runs beyond the first page, **When** incident filtering is applied, **Then** the whole authorized result set is filtered before pagination.
3. **Given** a healthy run or only resolved incidents, **When** displayed, **Then** no current blocking/danger cue is shown; historical incidents remain discoverable.
4. **Given** incident data is unavailable through permission or loading failure, **When** displayed, **Then** the UI explains unavailable evidence rather than reporting zero incidents.

### User Story 2 — Identify the affected activity and input (Priority: P1)

An operator sees the activity with an incident on the executed graph and can distinguish failed input evaluation from an input that was never evaluated.

**Why this priority**: The QA failure produced a run-level incident but left the affected activity looking Scheduled with zero incidents.

**Independent Test**: Run an activity input with an undefined identifier and compare incident, activity, input and graph evidence after reopening.

**Acceptance Scenarios**:

1. **Given** an input expression throws during activity start, **When** the run is inspected, **Then** the recorded incident identifies the exact activity execution/node and failing input without relying on prose parsing.
2. **Given** a blocking incident, **When** its activity is visible, **Then** a restrained danger accent and accessible exclamation/count cue identify it while preserving selection/focus styling.
3. **Given** evaluation was attempted and failed, **When** the input is inspected, **Then** failed evaluation is shown with the useful cause and no fabricated value.
4. **Given** a genuinely unassociated engine incident, **When** displayed, **Then** it remains clearly run-level with an explanation rather than a guessed activity association.

### User Story 3 — Navigate directly to an incident (Priority: P1)

An operator activates the activity's incident cue or selects the incident and reaches the corresponding incident/activity/input context.

**Why this priority**: Recognition must lead to a useful next action.

**Independent Test**: Activate both node-to-incident and incident-to-node actions for a nested repeated activity execution.

**Acceptance Scenarios**:

1. **Given** an activity with an active incident, **When** its incident cue is activated, **Then** the details panel opens its incident evidence, selects the matching occurrence and retains the activity context.
2. **Given** an incident associated with a nested activity, **When** Show affected activity is activated, **Then** the correct graph scope opens and the activity is framed.
3. **Given** normal activity selection with active incidents, **When** selected in the instance viewer, **Then** incident details open for troubleshooting; healthy activity selection opens ordinary activity inspection.
4. **Given** multiple executions of one authored activity, **When** an incident is selected, **Then** its exact execution occurrence is selected while node-level counts may aggregate.

### User Story 4 — Read the cause without decoding internals (Priority: P2)

An operator sees the activity, input and useful error message first, and can expand/copy full technical evidence when needed.

**Independent Test**: Inspect the undefined-identifier incident and expand its exception/scheduler details.

**Acceptance Scenarios**:

1. **Given** a nested exception chain, **When** the incident is displayed, **Then** the useful root cause leads the summary and full context remains available.
2. **Given** a test run was accepted with an incident, **When** dispatch feedback appears, **Then** it communicates the incident outcome and provides a direct review action.
3. **Given** a details panel is narrow, collapsed or maximized, **When** a run has an active incident, **Then** the incident count/action remains visible independently of hidden canvas or truncated tab text.

### Edge Cases

- Resolved incidents retain history but must not leave stale current danger cues after refresh.
- Nonblocking incidents and pending retries must not falsely imply the whole workflow is faulted or requires terminal intervention.
- Parent containers advertise contained incidents without claiming the parent itself failed.
- Older records missing additive causal/evaluation fields degrade honestly; association is never inferred from exception strings or scheduler IDs.
- Keyboard activation and focus must work independently of color; current light/dark theme tokens must remain legible.
- Pinned executed graphs remain inspection sources; current editable definition navigation is distinct.
- Cancellation, lease loss, durable checkpoint failures and infrastructure failures retain existing runtime semantics.

## Requirements

### Functional Requirements

- **FR-001**: The system MUST preserve structured causal activity/node identifiers for attributable scheduler/start-time failures.
- **FR-002**: The system MUST retain existing intervention, retry, cancellation and workflow/activity lifecycle behavior while improving evidence.
- **FR-003**: The system MUST distinguish failed input evaluation from evaluation not attempted without persisting a false successful value.
- **FR-004**: The system MUST report current active/blocking incident health separately from execution lifecycle and historical incident totals.
- **FR-005**: The run list MUST provide authoritative incident-health filtering across the authorized dataset before pagination.
- **FR-006**: The viewer MUST offer an always-visible incident summary/action when active incidents exist, including hidden-panel layouts.
- **FR-007**: Affected activities MUST have a restrained visible incident icon/count and danger accent appropriate to severity/blocking status.
- **FR-008**: Incident cues MUST preserve separate focus and selection treatment and have meaningful accessible names.
- **FR-009**: Incident activation MUST select the exact incident/execution and reveal its associated graph scope/activity; reverse navigation MUST work.
- **FR-010**: Incident-bearing activity selection MUST open incident details; healthy selection MUST retain activity inspection.
- **FR-011**: The incident summary MUST lead with useful activity/input/cause information and retain expandable full technical evidence/copy actions.
- **FR-012**: Resolved/retrying/nonblocking evidence MUST be shown truthfully and MUST NOT create false current blocking cues.
- **FR-013**: Unassociated engine incidents and unavailable evidence MUST be explicitly explained without guessed association or false zero counts.
- **FR-014**: Test-run feedback MUST distinguish dispatch acceptance from incident outcome and provide navigation to incident review.
- **FR-015**: Existing graph components, theme tokens, incident projections and domain boundaries MUST be reused; expression editing assistance remains outside this feature.

### Key Entities

- **Workflow run**: One execution inspected through its pinned executed graph, lifecycle status and current/historical incident health.
- **Activity execution**: One occurrence of an activity, distinct from its authored node and other loop/repeated executions.
- **Incident**: Recorded failure evidence with severity, status, cause, intervention/recovery history and optional causal execution/node/input association.
- **Input evaluation evidence**: Whether evaluation was attempted, succeeded or failed, including useful failure context without a fabricated value.

## Success Criteria

### Measurable Outcomes

- **SC-001**: The real undefined-identifier scenario has consistent incident association/count across run, activity, graph and input inspection after reload.
- **SC-002**: One activation of the node incident cue opens the relevant incident; one activation of its activity action reveals and frames the corresponding node, including nested scopes.
- **SC-003**: All active/blocking filter results satisfy the selected health condition across multiple pages; healthy/resolved-only controls are excluded from active filters.
- **SC-004**: Healthy, resolved, nonblocking, pending-retry, repeated-execution and unassociated-engine controls pass their explicit acceptance assertions.
- **SC-005**: The incident action/count remains discoverable in split/maximized/collapsed layouts and is operable with keyboard in current light/dark themes.
- **SC-006**: Rebuilt real Studio and backend demonstrations, affected regression suites, review and required delivery gates pass; screenshot evidence shows before/after behavior.

## Assumptions

- The user's 2 October 2026 request approves moving from the reviewed QA plan through specification, implementation and delivery; no additional product-policy approval is required for preserved behavior.
- Existing WaitForIntervention policy remains authoritative; routing expression faults into a different strategy/lifecycle path is deferred outside this feature.
- Existing permission and tenant scoping are preserved for all list/detail reads and filtering.
- Backend owns causal/query/evaluation evidence; Studio owns presentation and navigation. This feature does not redesign expression editors, recovery policies, dashboards or authentication.
