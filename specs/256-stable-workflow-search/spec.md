# Feature Specification: Stable workflow search

**Feature Branch**: `agent/codex/3068-workflow-search`

**Created**: 2026-10-20

**Status**: Implemented

**Input**: User outcome for Foundation #3068: keep workflow-definition search useful and predictable while results refresh, and return to the same browse context after opening a definition.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Search without losing useful results (Priority: P1)

As a workflow author, I want to type a search without the list disappearing on every edit, so that I can keep my place and understand which search produced the rows I can still see.

**Why this priority**: Stable search is the primary outcome: the author should be able to refine a query without losing the useful result already on screen.

**Independent Test**: Open a workflow list with results, enter several search edits quickly, and verify that the input responds immediately, the previous result is clearly identified until the latest search finishes, and only the latest search becomes current.

**Acceptance Scenarios**:

1. **Given** a workflow list has loaded, **When** the author changes the search, **Then** the input and page address reflect the change immediately and the existing rows remain visible with a clear indication that they belong to the previous search while the new search is pending.
2. **Given** the author makes several quick edits and requests can finish in a different order, **When** the newest search finishes, **Then** only results for that newest search become current; an older response cannot replace them.
3. **Given** the workflow list has not loaded successfully yet, **When** its first search is pending, **Then** the page shows its normal first-load waiting state rather than implying that the search has no matches.

### User Story 2 - Understand refresh, no matches, and failure (Priority: P1)

As a workflow author, I want the page to distinguish a refresh, a genuine no-match result, and a failed search, so that I do not mistake old rows or an error for the answer to my current search.

**Why this priority**: Clear status prevents incorrect decisions based on rows that no longer match the requested view or on a failed request presented as an empty result.

**Independent Test**: Exercise a same-search refresh, a successful empty search, a failed search with retry, and an access/session change; check that each outcome is visibly distinct and tied to the current browse context.

**Acceptance Scenarios**:

1. **Given** the current search already has results, **When** that same view refreshes, **Then** the rows remain visible and the page clearly announces that they are refreshing.
2. **Given** the latest search succeeds with no matches or fails, **When** its result is shown, **Then** the page distinguishes “no matches” from failure, identifies the current search and filters, and offers a retry for a failure.
3. **Given** a refresh fails while previously successful rows remain visible, **When** the error is shown, **Then** the page marks those rows as previous or out of date, identifies the failed current search, and offers a retry without presenting the old result as current.
4. **Given** Studio reports that the active sign-in/access session or destination has changed, or the current request is denied, **When** the author returns to the list or the denial is handled, **Then** previously shown protected rows and remembered page context are cleared before new results are shown.

### User Story 3 - Return to the same workflow browse page (Priority: P2)

As a workflow author, I want Back from a workflow definition to return to the search and page I was browsing, so that I can continue where I left off.

**Why this priority**: Authors often open a definition to inspect or edit it and then need to continue browsing the same result set.

**Independent Test**: Find a definition on a later page with a search and filters applied, open it, and return using both the editor’s Back action and browser Back. Confirm that the same browse view is restored; then verify that a full page reload starts from the default page.

**Acceptance Scenarios**:

1. **Given** the author has a search, folder, lifecycle filter, non-default page size, and is on a later page, **When** the author opens a definition and returns with the editor’s Back action, **Then** those browse choices and the same page are restored.
2. **Given** the author opened a definition from the workflow list, **When** browser Back returns to that list, **Then** the same search, filters, page size, and page are restored; changing the search, folder, lifecycle filter, or page size still starts the browse at its first page.
3. **Given** the workflow page is opened after a full reload or route remount without the earlier browse context, **When** the list appears, **Then** it starts from the normal default page and page size rather than guessing a previous page.

### Edge Cases

- The first search has no prior successful result to keep visible.
- The prior search returned no rows, or the latest search succeeds with no rows.
- A previous request completes after a newer search has been entered, including while the newer search is still waiting to start.
- A search is cleared or contains only whitespace.
- The folder or lifecycle filter changes while results are visible; rows from the prior filter must not be presented as current.
- A page or page-size change occurs while a search is pending.
- Access ends or changes while the editor is open, before the author returns to the list.
- A full reload or route remount removes the in-memory browse context.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The search input MUST reflect each edit immediately while the page waits briefly before requesting results, so rapid edits do not each replace the visible list.
- **FR-002**: While a new search is pending, the page MUST keep a prior successful result visible only when the active Studio destination and access session, folder, and lifecycle filter remain the same. This includes a search entered from a later page, even though the new search starts at page one. It MUST identify the prior search and page and MUST NOT present that result’s count, page controls, empty message, selection, or row actions as belonging to the new search.
- **FR-003**: The first search MUST use the existing first-load waiting state when no successful result is available for the current Studio destination and access session.
- **FR-004**: Only the latest entered search and selected browse view MAY update the displayed rows, status, counts, page controls, or errors. An older pending result MUST NOT become current after the author enters a newer search, including before that newer search starts loading.
- **FR-005**: The page MUST distinguish a successful empty result from a failed request. A failure MUST identify the current search and offer a retry of that search.
- **FR-006**: A same-view refresh MUST leave its current rows visible and provide an accessible indication that they are refreshing.
- **FR-007**: A change to the folder, lifecycle filter, page, or page size MUST clear rows retained for the previous view. Normal page navigation MUST open the selected page; only the search, filter, and page-size changes described in FR-011 start at the first page.
- **FR-008**: When Studio reports a change to the active destination or access session, or the current request is denied, the page MUST clear previously displayed protected rows, pending results, selection, and remembered page context before the new access context is shown.
- **FR-009**: The page MUST apply the existing workflow search, folder, lifecycle, and access rules. It MUST NOT require a new permission or expose definitions outside the author’s current access.
- **FR-010**: Returning from an opened definition with the editor’s Back action or browser Back, while still in the same workflow-management visit and access context, MUST restore the search, folder, lifecycle filter, page size, and page that the author left.
- **FR-011**: Changing the search, folder, lifecycle filter, or page size MUST start browsing at the first page and MUST clear page context from the previous view.
- **FR-012**: When the workflow page is opened without its prior in-memory browse context, it MUST use the existing default page and page size; it MUST NOT guess a prior page.
- **FR-013**: The current search MUST continue to appear in the page address using the existing browse behavior; clearing the search MUST remove it from the address and show the unsearched workflow list.
- **FR-014**: This feature MUST apply only to workflow definitions and MUST leave other workflow, activity, run, inventory, and connection lists unchanged.

### Key Entities *(include if feature involves data)*

- **Workflow definition**: A workflow that the author can find, open, and edit within their existing access.
- **Browse view**: The visible workflow definitions and the search, folder, lifecycle, page, and page-size choices that explain which definitions belong in that view.
- **Previous result**: The last successful definitions shown while a different search is pending; its original search and page remain clear to the author.
- **Access context**: The active Studio destination and sign-in/access session under which the author may view workflow definitions. A reported change or denied request ends the validity of previously shown protected results.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Across rapid-edit and out-of-order scenarios, no superseded search is presented as current, and one settled search is requested for a group of rapid edits.
- **SC-002**: In every tested pending-search case with an eligible prior result, that result stays visible and is identified as previous; its count, page controls, empty message, selection, and row actions are not presented as current.
- **SC-003**: In all tested first-load, refresh, successful-empty, failed-search, retry, and access-denied cases, the page shows the matching distinct status, and protected previous rows are absent after an access-context change or denial.
- **SC-004**: In every tested same-visit editor return using either Back control, the author regains the same search, filters, page size, and page; a full reload starts on the default page and page size.

## Assumptions

- Authors use the existing Studio workflow-definition list and have only the permissions already granted to them.
- The current search, folder, lifecycle, and page-size choices remain the basis for finding definitions.
- A previous-search result may remain visible only while the active access context, folder, and lifecycle filter remain unchanged; folder or lifecycle changes clear it.
- The page can respond to access-context changes reported by Studio and to a denied request. If an access change is not reported to the page and no request has yet been denied, the page cannot know that it occurred.
- Browse context is temporary for the current workflow-management visit. A full reload or route remount starts at the normal defaults.
