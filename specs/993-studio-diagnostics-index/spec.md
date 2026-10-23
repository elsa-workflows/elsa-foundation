# Feature Specification: Studio Diagnostics Index

**Feature Branch**: `agent/codex/3073-diagnostics-index`

**Created**: 2026-10-20

**Status**: Implemented

**Input**: User description: “Provide a task-oriented Diagnostics index in Elsa Studio that helps each user find the diagnostic tools available in their current app and access context. Explain unsupported OpenTelemetry views before entry, preserve each destination's own state, and present runtime payload capture controls as Runtime evidence settings without changing their behavior.”

## User Scenarios & Testing

### User Story 1 - Find a diagnostic task (Priority: P1)

An operator opens Diagnostics to find the Studio destination that can help with a specific operational task. Known built-in destinations explain their supported tasks; registered custom destinations keep their existing labels and use neutral navigation wording when no task description exists.

**Why this priority**: The index is useful only if a user can quickly identify and reach a tool that exists in the current app and is available to that user's access level.

**Independent Test**: In each supported Workbench composition, compare the index entries with the diagnostic destinations the same user can reach from the existing navigation, including registered custom entries in the Diagnostics grouping, then open each listed entry and confirm it reaches that destination.

**Acceptance Scenarios**:

1. **Given** the normal Workbench composition and a user permitted to view OpenTelemetry and Structured Logs, **When** the user opens Diagnostics, **Then** the index shows those known built-in destinations with plain-language descriptions of their supported tasks.
2. **Given** the baseline composition, which does not include the optional OpenTelemetry and Structured Logs modules, **When** the user opens Diagnostics, **Then** the index does not offer links to those absent destinations.
3. **Given** a diagnostic destination is listed in the index and appears in another navigation surface, **When** the user follows either link, **Then** both links open the same destination and preserve its current route.
4. **Given** a diagnostic route is registered and the user can navigate to it, but its backend is missing or unavailable, **When** the user follows the index link and opens the destination, **Then** that destination explains its backend state.

---

### User Story 2 - Understand when no diagnostic links are available (Priority: P1)

A demo user or an authenticated user with restricted access can tell when the current app and access context provide no diagnostic destinations to open.

**Why this priority**: An empty index must not make users mistake absent or permission-filtered links for a backend outage or a lack of diagnostic data.

**Independent Test**: Open Diagnostics with a composition that registers no diagnostic destinations and with a user whose permissions filter out all such destinations. In both cases, check the empty-state wording and verify that restricted tool names are not exposed.

**Acceptance Scenarios**:

1. **Given** the current app and user's access context expose no diagnostic destinations, **When** the user opens Diagnostics, **Then** the empty state says that no diagnostic tool links are available for this app or access context.
2. **Given** a user cannot access a destination, **When** the user opens Diagnostics or enters that destination's address directly, **Then** existing access restrictions continue to apply and the index does not reveal the restricted destination.
3. **Given** at least one diagnostic destination is accessible, **When** the user opens Diagnostics, **Then** the page shows the accessible entries rather than the no-links empty state.

---

### User Story 3 - Choose a supported telemetry destination (Priority: P2)

An operator can tell which OpenTelemetry tasks are supported both before opening the destination and while on its page, and can follow a Structured Logs handoff only when that destination is present and accessible.

**Why this priority**: Users should not have to open unsupported views to discover that they cannot perform the task there, and the application must not imply that a different log destination is available when it is not.

**Independent Test**: Review the index copy and the OpenTelemetry page in a normal composition. Verify the index identifies trace inspection and explains the Metrics and OpenTelemetry Logs limitations before entry. On the OpenTelemetry page, verify each unsupported view is identified as unavailable, or its entry affordance is omitted and the page explains the limitation; neither may lead to an unexplained placeholder. Verify any Structured Logs handoff is available only when truthful for that user.

**Acceptance Scenarios**:

1. **Given** OpenTelemetry is registered and accessible, **When** the index is shown, **Then** its description identifies supported trace inspection and states that Metrics and OpenTelemetry Logs are not available in this experience.
2. **Given** the user opens OpenTelemetry, **When** the page presents signal choices, **Then** it explains that Metrics and OpenTelemetry Logs are unavailable before any placeholder content can be entered, and neither unsupported choice appears to be a functioning tool.
3. **Given** Structured Logs is registered and accessible, **When** the index or OpenTelemetry page offers a direct Structured Logs link, **Then** the link opens Structured Logs and its copy accurately identifies the destination.
4. **Given** a surface cannot establish whether Structured Logs is registered and accessible, **When** it offers a handoff, **Then** it directs the user to Diagnostics with neutral wording and does not claim Structured Logs is available.
5. **Given** Structured Logs is absent or not accessible to the user, **When** the index or OpenTelemetry page is shown, **Then** neither surface offers a direct Structured Logs link or claims that destination is available.
6. **Given** the user opens OpenTelemetry, **When** traces are loading, empty, permission-restricted, unavailable, or failed, **Then** the destination continues to distinguish those existing states and does not present a failure as an empty result.
7. **Given** the user opens Structured Logs, **When** it is loading, empty, permission-restricted, unavailable, or failed, **Then** Structured Logs continues to own and explain its existing state.

---

### User Story 4 - Open Runtime evidence settings and retain contributed diagnostics (Priority: P2)

An operator looking for capture controls can find them under the name Runtime evidence settings. Existing diagnostic widgets remain part of the Diagnostics experience.

**Why this priority**: Users need a clear distinction between runtime evidence controls and operational diagnostic tools, while applications that contribute diagnostic widgets must retain their existing experience.

**Independent Test**: Compare the current route, permission behavior, capture-policy result, and save outcome before and after the label change. Register successful, loading, streaming, and failing diagnostic widgets and verify they remain visible with their existing states.

**Acceptance Scenarios**:

1. **Given** the user is permitted to save Runtime evidence settings, **When** the user opens Diagnostics, **Then** the settings destination is named “Runtime evidence settings” and opens the same existing page.
2. **Given** the user lacks the existing settings permission, **When** the user opens Diagnostics or enters the settings address directly, **Then** the current permission behavior remains in force.
3. **Given** the user may view Runtime evidence settings but may not save them, **When** the user opens Diagnostics, **Then** the same settings destination remains discoverable and opens without granting access to save changes.
4. **Given** the user is authorized to save Runtime evidence settings, **When** the user changes and saves a setting, **Then** the same policy and save behavior apply as before the label change.
5. **Given** the app contributes diagnostic widgets, **When** the user opens Diagnostics, **Then** the widgets remain present and retain their existing content and loading, ready, streaming, or error behavior.

---

### Edge Cases

- A destination is registered and visible to the user, but its backend does not support the destination or is unavailable. The link remains truthful about navigation; the destination owns the backend explanation.
- A destination is registered, but feature or permission rules hide it from this user. The index omits its link without describing the backend as unhealthy.
- A registered custom diagnostics entry has a label but no task description. The index retains its existing label and destination and uses neutral navigation wording rather than guessing its purpose.
- OpenTelemetry Metrics or Logs is represented by a page choice without backend support. The user sees its unsupported status before entering placeholder content, or the unsupported choice is absent.
- A user may view Runtime evidence settings but may not save changes. The destination remains discoverable and readable, while the existing write gate remains in force.
- No tool links are accessible, but contributed diagnostic widgets still exist. The page must retain the widgets and must not treat their count as a destination-health signal.
- One contributed widget fails to load or render while other widgets or destination links remain usable.
- OpenTelemetry returns no traces versus a permission failure, unavailable backend, or general failure. Those outcomes remain distinct.
- Structured Logs returns no entries versus a permission failure, unavailable backend, or general failure. Those outcomes remain distinct.
- A user arrives by a bookmarked direct URL, the dock, or the sidebar. Existing routing and access behavior remain consistent with opening the same destination from Diagnostics.
- The page is used by anonymous/demo users and by authenticated users with limited permissions. Restricted entries and their names are not exposed.
- At 320 pixels wide, long tool names, descriptions, and keyboard focus do not make the page unusable or require horizontal scrolling.

## Requirements

### Functional Requirements

- **FR-001**: The Diagnostics index MUST show known built-in diagnostic destinations with meaningful, plain-language descriptions of their supported tasks. A registered custom diagnostics entry MUST retain its existing label and destination; if its existing contribution provides no task semantics, the index MUST use neutral navigation wording and MUST NOT guess its purpose.
- **FR-002**: The index MUST show only destinations registered in the current app and eligible for the current user's existing feature and permission rules.
- **FR-003**: For each diagnostic destination, the index and any existing sidebar or dock entry MUST resolve to the same destination and honor the same access rules. Opening the existing direct address MUST retain its current behavior.
- **FR-004**: When no diagnostic destination links are available in the current app or access context, the index MUST state that no diagnostic tool links are available for this app or access context. It MUST NOT describe this state as a backend outage or as an empty result set.
- **FR-005**: A listed destination MUST communicate only that the user can navigate to it. The index MUST NOT describe backend connectivity, backend health, or data availability based on route registration, widget registration, or widget count.
- **FR-006**: Each destination MUST remain responsible for its actual loading, permission, unsupported-backend, failure, and empty-data states. The index MUST NOT replace those destination states with an inferred summary.
- **FR-007**: The Diagnostics description for OpenTelemetry MUST identify trace inspection as the supported task and explain that Metrics and OpenTelemetry Logs are not available in this experience. On the OpenTelemetry page, those views MUST be clearly identified as unavailable, or their entry affordances MUST be omitted and the page MUST explain that they are unsupported. No unsupported choice may lead users into placeholder content as though it were a functioning tool.
- **FR-008**: When the Diagnostics index can confirm that Structured Logs is registered and accessible to the current user, it MUST offer the application-log handoff. The index and OpenTelemetry page MUST offer a direct Structured Logs link only when both conditions are established. If a surface cannot establish both conditions, it MUST offer no direct link and any handoff MUST lead to Diagnostics with neutral wording that makes no claim that Structured Logs is present or accessible.
- **FR-009**: OpenTelemetry MUST retain its existing distinct trace-loading, trace-empty, permission, backend-unavailable, and error outcomes. A failed request MUST NOT be presented as a successful empty trace result.
- **FR-010**: Structured Logs MUST retain its existing destination-owned loading, empty, permission, backend-unavailable, and error outcomes.
- **FR-011**: The capture-policy destination MUST be presented as “Runtime evidence settings.” Its existing address, view-access rule, save authorization, capture policy, and save behavior MUST remain unchanged. A user who may view the settings but may not save MUST still be able to discover and open the page; the existing write gate MUST continue to control saving. No permission may be broadened or narrowed.
- **FR-012**: All diagnostic widgets contributed to the existing Diagnostics experience MUST remain available with their current content and loading, ready, streaming, and error behavior. A widget failure MUST NOT hide unrelated widgets or tool links.
- **FR-013**: Existing custom diagnostic widgets and registered custom diagnostics entries in the existing Diagnostics grouping MUST remain available without registration changes. Custom entries MUST retain their exact existing labels, destinations, and access rules. The index MUST NOT discover unrelated routes by guessing from their names or require new descriptive metadata.
- **FR-014**: The index MUST remain operable by keyboard and readable in light and dark themes at viewport widths of 320, 768, 1024, and 1440 pixels.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Across the normal and baseline compositions and the anonymous/demo, permitted, and restricted access cases, 100% of index links point to destinations the current user can open, and no destination excluded by current registration or access rules is listed.
- **SC-002**: Every listed diagnostic destination resolves to the same route as its existing sidebar or dock entry when one exists; the current direct address continues to resolve to the same destination.
- **SC-003**: In all tested registration, permission, and backend-unavailable cases, zero index states describe a destination as connected, healthy, or populated based only on the presence or count of routes or widgets.
- **SC-004**: Before entry, every OpenTelemetry index description states that Metrics and OpenTelemetry Logs are not available in this experience; the OpenTelemetry page also marks them unavailable or omits their affordances with an explanation before placeholder content. A direct Structured Logs link appears on either surface only when that registered destination is accessible; otherwise any handoff makes no availability claim.
- **SC-005**: The Runtime evidence settings label opens the existing address for users with view access, including users who cannot save. Only users authorized by the existing save gate can save, and every tested setting and save outcome matches its pre-change policy behavior.
- **SC-006**: At each of the four target widths in both light and dark themes, all index destinations can be reached and opened by keyboard without horizontal scrolling or clipped descriptions.
- **SC-007**: Every existing contributed diagnostic widget retains its prior content and loading, ready, streaming, and error outcome, including when a neighboring widget fails.

## Assumptions

- The normal Workbench composition includes OpenTelemetry and Structured Logs; the baseline composition omits these optional diagnostics modules. Their registration in a composition does not assert that a particular backend is connected or healthy.
- “Available” in the index means that a user can navigate to a registered destination under current feature and permission rules. It does not mean the destination's backend is ready or that it contains data.
- “Runtime evidence settings” is a discovery label for the existing workflow runtime payload capture settings. This specification does not revise the setting's policy meaning.
- The index relies on existing Studio destination links, access rules, Workbench compositions, and diagnostic widgets. It introduces no new data or runtime capability.
- Known built-in diagnostic destinations receive meaningful task descriptions. Custom diagnostics entries retain their current labels, routes, and access gates; when their existing label gives no task semantics, neutral navigation wording is used instead of a guessed description. No new contribution metadata is introduced, and unrelated routes are not inferred to be diagnostic tools.
- Existing custom diagnostic widgets are in scope for preservation with their current content and states.

## Out of Scope

- A shared backend capability or health service, destination probes, or a new way to infer destination readiness.
- OpenTelemetry ingestion, telemetry storage changes, or implementing Metrics or OpenTelemetry Logs views.
- A new Studio SDK, API, or contribution-contract field.
- Enabling optional providers or diagnostic modules in the baseline composition.
- Deployment or hosted runtime changes.
