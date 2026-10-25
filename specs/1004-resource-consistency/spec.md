# Feature Specification: Studio resource-list consistency and Home access

**Feature Branch**: `agent/codex/3071-resource-consistency`

**Created**: 2026-10-24

**Status**: Implemented — takes effect when implementation PR #3313 merges; acceptance and resulting-main verification are tracked in #3071.

**Input**: User description: Deliver the approved #3071 Studio Secrets, Connections, and Home consistency repair: align familiar resource-list hierarchy while preserving each page's distinct actions and security behavior; keep Connection creation within the accepted search context; retain truthful Secrets inventory coverage; gate only Home's Needs attention destination by effective runtime-read access; and make the browser fixture's attempted Secrets writes observable, including failures.

## User Scenarios & Testing

### User Story 1 - Manage Secrets from a clear, bounded inventory (Priority: P1)

An operator opens Secrets and can quickly understand the list, search and filter it, create a secret, and choose a clear primary action for an existing row. Secondary operations remain discoverable without making sensitive values visible. The page explains the limits of the loaded inventory and distinguishes a query being edited from one whose results are shown.

**Why this priority**: Secrets combines the highest-impact list consistency work with security-sensitive data and the loaded-result contract.

**Independent Test**: Exercise the Secrets page with a populated inventory larger than one page, a selected scope outside the loaded rows, an edited query, and successful and rejected write attempts in the browser fixture. Verify action access, displayed counts, query state, and metadata-only evidence.

**Acceptance Scenarios**:

1. **Given** a Secrets list, **When** the operator scans its heading and controls, **Then** the page presents one clear primary create action, a familiar search/filter region, and a consistent resource-list hierarchy.
2. **Given** a secret row, **When** the operator uses its actions, **Then** Edit is the primary row action and Rotate, Test, Revoke, and Delete remain discoverable as secondary actions with their existing permission, confirmation, busy, error, and focus-return behavior.
3. **Given** a secret with a display name, **When** the operator uses Copy, **Then** only its technical name is copied; the secret value is never shown or copied.
4. **Given** more results exist than are currently loaded, **When** the operator views the inventory, **Then** shown, loaded, and total counts remain truthful, and coverage wording agrees with the page limit.
5. **Given** a selected scope has no matching loaded rows, **When** local filters are shown, **Then** the page explains that the selected scope is absent from loaded results rather than implying that no matching secret exists anywhere.
6. **Given** the operator edits a search without applying it, **When** current results are displayed, **Then** the page distinguishes the draft query from the accepted query; a failed attempt remains identifiable so retry uses that attempted query.
7. **Given** a browser-fixture POST, PUT, or DELETE attempt, **When** it succeeds or is rejected, **Then** the visible attempt count includes it without exposing request bodies or secret values.

### User Story 2 - Keep Connections actions and creation in context (Priority: P2)

An operator scans and searches Connections using the familiar resource-list hierarchy, while retaining Details as the domain-specific row entry point. Creating a connection leaves the operator in the same accepted search context and clearly reports which connection was created.

**Why this priority**: Operators need consistent navigation across resource pages, while Connection details and query-preserving creation must remain specific to this domain.

**Independent Test**: Open Connections with a non-empty accepted search, create an item that is not returned for that search, then verify the query, exact success notice, selected item, and existing Details actions.

**Acceptance Scenarios**:

1. **Given** the Connections page, **When** the operator scans its heading and controls, **Then** its high-level hierarchy matches Secrets while its domain-specific columns and Details entry remain intact.
2. **Given** a connection row, **When** the operator opens its actions, **Then** Details remains the row entry and Test, Revoke, and Delete remain in the existing details surface with their current confirmation and error behavior.
3. **Given** an accepted non-empty search, **When** the operator creates a connection, **Then** the accepted query is retained, the exact existing success notice names the created connection, and the same query is reloaded.
4. **Given** the created connection is not present in the returned result set, **When** the reload completes, **Then** it is not selected; it is selected only when the returned results actually contain its exact name.
5. **Given** a later successful or failed action, **When** the operator returns to the list, **Then** the accepted search context is preserved.

### User Story 3 - Open Home's active-incident destination only with access (Priority: P3)

An authenticated operator can follow Home's Needs attention destination only when their effective permissions allow reading workflow runtime data. Users in an authentication-disabled session keep the current behavior. The Continue section continues to represent the complete authorized definition catalog.

**Why this priority**: The change closes a narrow access gap in a Home task link without changing the broader dashboard or authorization model.

**Independent Test**: Render Home for authenticated sessions with and without effective runtime-read permission, and for an authentication-disabled session. Verify link availability, the active-incident destination, and unchanged Continue results and counts.

**Acceptance Scenarios**:

1. **Given** an authenticated session with effective `workflows/runtime:read`, **When** Home renders Needs attention, **Then** its destination is available and retains `/workflows/instances?incidentHealth=active`.
2. **Given** an authenticated session without effective `workflows/runtime:read`, **When** Home renders Needs attention, **Then** that destination is unavailable.
3. **Given** authentication is disabled, **When** Home renders Needs attention, **Then** its current usable behavior is preserved.
4. **Given** any of these sessions, **When** Continue is rendered, **Then** it retains its complete authorized definition-catalog semantics and truthful counts.

### Edge Cases

- The Secrets server reports more results than the loaded page; local facets must not imply that the loaded subset is the complete inventory.
- The selected Secrets scope is absent from the loaded subset, including when the current local filters produce no rows.
- A Secrets search is edited but not accepted, or its request fails; refresh/success continue to use the accepted query, and retry uses the failed attempted query.
- A Connection created while a search is accepted does not appear in the returned rows, including because it is beyond the current result window.
- A Connection create or later action fails; the accepted search remains the list context and the existing failure is presented.
- A fixture write is counted even when URL validation or the simulated handler throws before returning a response.
- An authenticated grant differs in case or uses the existing supported permission grammar; the Home decision must follow the existing effective-permission semantics.
- An unauthenticated or authentication-disabled session must retain current behavior; Continue must not inherit the new Needs attention restriction.

## Requirements

### Functional Requirements

- **FR-001**: Secrets and Connections MUST present a consistent high-level page heading, search/filter area, primary create action, and row-action hierarchy while retaining their distinct domain-specific columns and entry points.
- **FR-002**: Secrets MUST present Edit as the primary row action and keep Rotate, Test, Revoke, and Delete available as secondary actions; existing access checks, confirmation rules, busy states, errors, and focus return MUST be preserved.
- **FR-003**: Secrets MUST provide copying of the technical name only and MUST NOT reveal or copy secret values as part of this work.
- **FR-004**: Secrets MUST preserve the 100-record loaded-page limit, server-side search and total count, local facets over loaded records, qualification for a selected scope absent from loaded rows, and separate draft, accepted, and failed query states. The displayed coverage explanation MUST agree with the loaded-page limit.
- **FR-005**: Secrets refresh and successful operations MUST continue to reload the accepted query; retrying a failed search MUST use the failed attempted query.
- **FR-006**: Connections MUST retain Details as its row entry and keep Test, Revoke, and Delete in the existing details surface with current operation semantics.
- **FR-007**: After successful Connection creation, Connections MUST retain the accepted query, preserve the existing exact success notice naming the created connection, and reload that same query.
- **FR-008**: Connections MUST select the created connection only when the exact name appears in the returned results; it MUST NOT clear the accepted query to select a connection outside those results.
- **FR-009**: Home MUST expose the Needs attention destination to authenticated users only when they have effective `workflows/runtime:read`; the allowed destination MUST preserve `/workflows/instances?incidentHealth=active`.
- **FR-010**: Home MUST preserve its current Needs attention behavior when authentication is disabled, and MUST preserve the complete authorized Continue catalog and its truthful counts.
- **FR-011**: The browser Secrets fixture MUST visibly count every attempted POST, PUT, and DELETE, whether its handler succeeds or throws. The counter and its evidence MUST not expose request bodies, headers, secret values, or credential-bearing data.

### Key Entities

- **Secret inventory item**: A listed secret described by technical name, optional display name, type, store, scope, status, and timestamps. Secret material is not part of list presentation or copy behavior.
- **Connection inventory item**: A listed connection identified by its exact technical name and represented by its descriptor and current local status.
- **Search state**: The editable query, the query represented by current accepted results, and a failed attempted query when retry is available.
- **Home task destination**: The active-incident Runs destination whose availability depends on the authenticated session's effective runtime-read access.

## Success Criteria

### Measurable Outcomes

- **SC-001**: In visual and interaction review, both resource pages have one clear primary create action and the same high-level heading/search/action hierarchy, while Secrets and Connections retain their distinct row entry points.
- **SC-002**: With 101 or more Secrets, operators can see that the current page is bounded, use server search to locate an item beyond the initially loaded set, and distinguish displayed rows, loaded rows, and total results without local filters implying a complete inventory.
- **SC-003**: When a Connection is created while a non-empty search is accepted, the search value and exact success notice remain, the accepted query is reloaded, and an absent created item is not selected.
- **SC-004**: Authenticated Home sessions with and without effective runtime-read access produce the corresponding allowed and unavailable Needs attention destination, while authentication-disabled behavior and Continue semantics stay unchanged.
- **SC-005**: Browser fixture evidence counts each attempted POST, PUT, and DELETE including rejected attempts, while containing no secret values or request payloads.

## Assumptions

- Existing Secrets and Connections endpoints, permission checks, operation semantics, and Home catalog behavior remain the source of truth; no backend contract change is needed.
- The established Studio list styling and existing controls can provide the hierarchy without introducing a generic table framework or new dependency.
- The standard built-in HTTP Connection test is a local readiness result and makes no external request. Revoke changes Elsa-side status; it does not establish remote credential revocation.
- Host proof, tests, builds, browser runs, and review gates are not claimed complete by this specification; they remain implementation-time evidence.

## Scope Boundaries

This work adds no backend/API changes, dependencies, modules, global permission helper/model, authorization rewrite, Connection-manager race repair, new permission, secret reveal/copy behavior, cloud-vault/OAuth/provider capability, remote Connection test, or remote credential-lifecycle guarantee.
