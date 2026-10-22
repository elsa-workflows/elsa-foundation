# Feature Specification: Consistent Studio Definition Lists

**Feature Branch**: `agent/codex/3070-list-consistency`

**Created**: 2026-10-22

**Status**: Approved — root approval recorded in [issue comment 6343367959](https://github.com/elsa-workflows/elsa-foundation/issues/3070#issuecomment-6343367959)

**Input**: Issue [#3070: Align workflow and activity lists with the Runs list conventions](https://github.com/elsa-workflows/elsa-foundation/issues/3070), including its pre-claim refinements.

## User Scenarios & Testing

### User Story 1 — Scan workflow and activity definitions consistently (Priority: P1)

As a Studio operator, I want the workflow and activity definition collections to present their heading, primary action, search and filters, row actions, and result states in a consistent hierarchy so that I can scan and use either list without relearning its layout.

**Why this priority**: These are the two definition lists covered by the issue. Their collection behavior is already established; the remaining work is to make the shared presentation predictable while keeping domain-specific controls clear.

**Independent Test**: Open both definition collections with populated, empty, long-name, and failed-read data. Compare their hierarchy side by side in light and dark themes and confirm that each collection's existing search, filters, lifecycle choices, and available actions still work.

**Acceptance Scenarios**:

1. **Given** an operator opens either definition collection, **when** the page loads, **then** the content inset, title and description, primary action, search and filter placement, and row-action priority follow the same visible hierarchy.
2. **Given** the collections expose different lifecycle or authority controls, **when** the operator changes them, **then** each list retains its existing domain-specific choices and meanings.
3. **Given** a row has a primary action and secondary actions, **when** the operator scans or operates the row, **then** the primary action is clear and secondary or destructive actions retain their current confirmation and permission behavior.
4. **Given** a list has no matches or cannot be read, **when** Studio renders the result, **then** an empty result remains distinct from an error, denial, or unavailable capability and provides the existing appropriate recovery.

### User Story 2 — Find and copy exact definition identities (Priority: P1)

As an operator, I want the name and primary identity to be easy to scan while exact technical identifiers remain available and copyable, so that I can recognize a row quickly and still use its precise identity when troubleshooting or sharing it.

**Why this priority**: The two collections currently hide or de-emphasize useful identifiers, while the initial Runs row layout is too dense. All three lists need the same compact scan with accessible technical detail.

**Independent Test**: Use populated lists containing repeated names and long identifiers. Navigate to each identity control with the keyboard, copy its value, and confirm the clipboard contains the exact untruncated identifier. Open, expand, and copy from a row without opening that row's editor or detail view.

**Acceptance Scenarios**:

1. **Given** a populated list, **when** an operator scans a row, **then** the human-readable name and the list's primary identity, status, and timing information remain visible without a dense block of secondary identifiers.
2. **Given** a row has additional technical identifiers, **when** an operator needs one, **then** an accessible progressive disclosure reveals the exact value and a keyboard-operable copy action.
3. **Given** the operator activates an identity disclosure or copy control inside an interactive row with a pointer, Enter, or Space, **when** that nested action completes, **then** the row's editor/detail action does not also run.
4. **Given** the operator uses the Runs list, **when** it is compared with both definition lists, **then** its row actions use the same themed action hierarchy and its technical identifiers use the same compact, progressively disclosed treatment.

### User Story 3 — Return to the Activity Definition collection (Priority: P1)

As an operator editing an Activity Definition, I want to return to the exact collection view from which I opened it, so that I can continue reviewing results without rebuilding my search and page position.

**Why this priority**: The collection currently unmounts on editor entry, losing its local query and page state. This breaks a normal back-and-forth review workflow.

**Independent Test**: Open an Activity Definition from a later page with a search, provider, and authority filter applied. Return with the editor's Back action and with browser Back. Repeat from more than one collection history entry. Verify that a stale or expired page cursor is recovered by the existing collection contract while the applicable filters and page size remain intact.

**Acceptance Scenarios**:

1. **Given** an Activity Definition is opened from a collection with search, provider, authority, page-size, and continuation state, **when** the operator chooses the editor's Back action, **then** that originating collection context is restored.
2. **Given** the operator has opened definitions from more than one collection history entry, **when** browser Back returns to each entry, **then** each entry restores its own context rather than a single most-recent global snapshot.
3. **Given** an origin page is reloaded or its authorized snapshot is no longer valid, **when** the operator returns, **then** Studio retains the valid search, provider, authority, and page-size choices, uses the existing restart/recovery behavior for the continuation state, and explains the recovery.
4. **Given** an Activity Definition is opened directly without collection context, **when** the operator returns, **then** Studio uses the existing safe collection default.
5. **Given** the Activity Definition collection uses a scoped opaque continuation value, **when** context is preserved, **then** its scope and existing server contract remain intact; Studio does not invent a URL or page-number representation for that value.

### Edge Cases

- A long workflow or activity name and a long identifier must remain distinguishable at laptop and narrow viewport widths.
- Repeated workflow names must not hide the exact workflow identity.
- A screen reader must identify collection headings, table headers, row actions, disclosures, and copy status without relying on hover text.
- An Activity Definition authorization denial or unavailable capability must not leave previously displayed identities visible. A general refresh failure and an expired snapshot retain their distinct existing treatments.
- A workflow definition's active/deleted/all state, folder scope, selection and Move action, and row lifecycle actions must retain their current meaning.
- An Activity Definition's Designed/Built-in/All authority choices and draft/published summaries must retain their current meaning; the collection gains no selection or lifecycle operation.
- Copy failure, a filtered empty result, an initial loading state, a failed request, and a denied/unavailable response must remain distinguishable.

## Requirements

### Functional Requirements

- **FR-001**: The Workflow Definitions, Activity Definitions, and reference Runs lists MUST use a consistent content inset and visible hierarchy for the page heading, primary action, search and filters, rows, and row actions. Each list MAY retain a documented domain-specific exception when its meaning or existing workflow requires it.
- **FR-002**: The three lists MUST present primary and secondary row actions consistently, including themed styling, keyboard focus, disabled state, destructive treatment, and existing confirmation and permission checks.
- **FR-003**: Studio MUST preserve live search and the existing filter behavior of each collection. The interface MAY align where search and filters appear, but MUST NOT change their query, debounce, authority, folder, lifecycle, paging, or permission semantics.
- **FR-004**: Workflow Definitions MUST preserve its existing Active/Deleted/All views, folder and unfiled navigation, search, selection, Move action, and row Delete/Restore/Delete permanently behavior. Activity Definitions MUST preserve its Designed/Built-in/All authority choices, search, provider filter, page sizes, lifecycle summaries, and existing workbench operations.
- **FR-005**: Rows in all three lists MUST keep a compact default scan centered on the human-readable name and the relevant primary identity/status/timing. Additional exact technical identifiers MUST remain discoverable through an accessible progressive disclosure and copyable by keyboard.
- **FR-006**: Activating a nested disclosure or copy control MUST NOT also activate its containing row's navigation action by pointer or keyboard.
- **FR-007**: Returning from an Activity Definition workbench or editor through its Back action or browser history MUST restore the correct originating collection context, including search, provider, authority, page size, and the matching opaque continuation scope.
- **FR-008**: Activity Definition context MUST be associated with its own history entry. Multiple origins, reloads, changed authorization, and expired/invalid continuation state MUST recover using the current collection contract without conflating entries or exposing an opaque cursor through a newly invented URL contract.
- **FR-009**: Loading, populated, empty, filtered-empty, failed, denied, unavailable, stale-confirmed, and expired-snapshot states MUST remain truthful and distinct according to each list's existing behavior.
- **FR-010**: The lists MUST retain meaningful table and column-header semantics for assistive technology while their visual layout changes.
- **FR-011**: Shared presentation MUST be extracted only when repeated structure is proven by the target consumers. Studio MUST NOT gain a generic list framework or replacement grid as part of this work.
- **FR-012**: The existing Studio design specification MUST record the resulting list composition and name any intentional domain-specific exceptions.

### Key Entities

- **Workflow Definition collection context**: The existing workflow lifecycle state, folder scope, search, page state, selection, and available actions.
- **Activity Definition collection context**: Search, provider, authority, page size, and the opaque continuation state scoped to the relevant authorization and filter snapshot.
- **Row identity**: The human-readable name and the exact technical identifiers that distinguish a workflow, activity, or run.
- **Collection history entry**: The origin-specific collection context associated with a browser history entry that opened an Activity Definition.
- **List result state**: The confirmed data, loading status, empty result, failure, denial, unavailable capability, or expired snapshot presented by a collection.

## Success Criteria

### Measurable Outcomes

- **SC-001**: Side-by-side review confirms that all three lists share the defined inset, heading/action hierarchy, search/filter placement, row-action priority, and empty/error presentation, with every exception named in the design specification.
- **SC-002**: Keyboard acceptance verifies that every required exact identifier can be reached and copied on all three lists, and that expanding or copying a row detail never opens the containing row.
- **SC-003**: Both Activity Definition return paths restore their own search, provider, authority, page size, and valid continuation context across at least two distinct history entries; expired or invalid continuation recovers without losing valid filters.
- **SC-004**: A bounded normal-host review covers representative populated, empty, failed-read, denied/unavailable, and expired-snapshot states across all three lists at 1280px/light and 1440px/dark; long names/identifiers, keyboard operation, and selected return/action states are checked at a 390px narrow width.
- **SC-005**: Regression checks confirm that workflow browse/lifecycle actions and Activity Definition authority, lifecycle summaries, authorization, and paging retain their existing behavior.

## Assumptions

- Issues #3062 and #3068 are accepted prerequisites; their Runs reference behavior and workflow browse-session behavior remain the source contracts for this slice.
- The existing public Studio UI identity, action, disclosure, and overflow controls are sufficient for these interactions.
- Activity Definition collection context can be restored without changing the backend, cursor contract, or authentication model.
- Existing authorization and lifecycle rules remain authoritative even where they create a deliberate visual exception between lists.

## Scope Boundaries

This work covers presentation and return context for Workflow Definitions and Activity Definitions, plus only the Runs row-action and compact-identity refinement needed to align the reference pattern. It does not change Run filtering, selection, or detail behavior; workflow browse semantics; Activity Definition lifecycle rules; backend APIs; global authorization; package dependencies; optional-module composition; or unrelated Studio pages. It adds no Activity Definition selection, delete, restore, or bulk lifecycle actions and does not replace the lists with a generic data grid.

## References

- [Issue #3070](https://github.com/elsa-workflows/elsa-foundation/issues/3070)
- [Studio design specification](../../Studio/docs/ux/design-spec.md)
