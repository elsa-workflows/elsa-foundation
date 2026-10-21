# Feature Specification: Preview Feature Impact Before Applying

**Feature Branch**: `agent/codex/3074-feature-impact-review`

**Created**: 2026-10-21

**Status**: In progress

**Input**: User description: "Show the exact target host and full cascade before applying feature administration changes."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Stage feature changes (Priority: P1)

As a feature administrator, I can stage single or bulk enable and disable changes for the selected Studio or Server host. Required dependencies and dependants continue to follow the existing cascade behavior, while optional dependencies do not cause automatic changes. Staging remains a draft until I apply it.

**Why this priority**: The impact review must describe the same draft the operator can already prepare; it should not change when or where edits take effect.

**Independent Test**: Stage single and bulk changes on either host, including a cascade that affects a feature hidden by the active filter, and verify that the draft changes while both hosts' saved configurations remain unchanged.

**Acceptance Scenarios**:

1. **Given** a feature with required dependencies, **When** an operator stages a single enable or disable, **Then** the existing required-dependency cascade is reflected in that host's draft and no saved host configuration changes.
2. **Given** several selected features, **When** an operator stages a bulk enable or disable, **Then** all resulting required-dependency changes are reflected in the same host draft, including changes to features outside the active filter.
3. **Given** an optional dependency, **When** its related feature is enabled or disabled, **Then** that optional relationship alone does not automatically change the other feature.

### User Story 2 - Review the complete impact (Priority: P1)

As a feature administrator, I can inspect the selected host's complete pending impact before applying it. The review distinguishes my direct enable/disable and setting edits from automatically affected features, explains the current pending edits or clearly historical cascade causes behind automatic changes, and includes changes hidden by the current filter.

**Why this priority**: Operators need to understand the complete consequence of a change before they authorize it, rather than infer it from a filtered list.

**Independent Test**: Stage overlapping single and bulk changes, edit a setting, hide an affected feature with a filter, and verify the review shows each net change, its direct or automatic status, all applicable pending roots and the required-dependency reason. Reverse edits until changes match the loaded baseline and verify those changes disappear from the review.

**Acceptance Scenarios**:

1. **Given** pending edits on one host, **When** the operator opens the review, **Then** it names that exact host and lists every net feature change, with direct edits distinguished from automatically affected features.
2. **Given** multiple pending edits require the same automatic change, **When** the operator reviews the impact, **Then** the affected feature lists every applicable pending root and the required-dependency path that explains each change.
3. **Given** an affected feature is excluded by the active filter, **When** the operator reviews the impact, **Then** that feature remains listed.
4. **Given** the operator reverses or resets pending edits so a feature matches its loaded baseline, **When** the review is refreshed, **Then** that feature is omitted as a net-zero change and no reverted action is presented as pending.
5. **Given** the operator cancels the review, **When** the operator returns to editing, **Then** the staged draft is still available and no saved host configuration has changed.
6. **Given** the operator changed a feature setting that will be submitted, **When** the operator reviews the draft, **Then** that direct setting edit is represented in the review using the existing handling for sensitive setting values.
7. **Given** A and required B start disabled, **When** the operator enables A and then disables A, **Then** the existing cascade leaves B enabled, the review shows B as an automatic retained effect of the earlier dependency change, omits net-zero A, and does not claim a direct B edit, a pending A edit, or that disabled A currently requires B.
8. **Given** a configured feature is disabled directly or by a required cascade, **When** the operator reviews the change, **Then** the review shows the existing outgoing configuration clear, does not promise locally retained setting values that will not be submitted, and keeps an automatic clear classified as automatic.
9. **Given** keyboard focus is on Review changes, **When** the operator opens and navigates the review, **Then** focus stays inside the dialog; Escape cancels without applying and returns focus to the invoking control.

### User Story 3 - Apply only what was reviewed (Priority: P1)

As a feature administrator, I can apply the exact draft I reviewed to the selected host. If the draft, selected host, or loaded feature catalog changes after review, I must review the new state before applying. Backend conflicts and other failures remain visible as failures.

**Why this priority**: A review is useful only when Apply cannot silently act on a different target or a later draft.

**Independent Test**: Review a draft for each host, then apply it and verify only that host changes. Separately change the draft, refresh the catalog, or select the other host after review and verify that the old review cannot be applied. Simulate a conflict and verify there is no success indication.

**Acceptance Scenarios**:

1. **Given** a current review, **When** the operator applies it without changing the draft or target, **Then** the complete reviewed state is applied to the named host and the other host remains unchanged.
2. **Given** a review followed by a draft edit, host change, catalog refresh, or catalog replacement, **When** the operator attempts to continue, **Then** the old review is invalidated and the changed state must be reviewed before Apply.
3. **Given** Apply is rejected because the configuration revision is stale or the backend refuses activation, **When** the error is shown, **Then** the backend's reason remains visible, the UI does not report success, and a guard refusal is not mislabeled as proof of a stale revision.
4. **Given** a dirty draft and an operator chooses Reset, **When** the draft returns to the loaded state, **Then** only the selected host's draft is reset and any review of the previous draft is invalidated.

### User Story 4 - Inspect with read-only access (Priority: P2)

As an authenticated user who can inspect feature management but cannot manage it, I can view the selected host's feature information without being offered controls that stage, reset, or apply changes.

**Why this priority**: A read-only user should be able to understand the host state without being led into actions that will be rejected.

**Independent Test**: Open each host as an authenticated read-only user, verify information remains inspectable and all change controls are unavailable, and confirm the backend remains the authority for any attempted write. Separately verify the existing anonymous or authentication-disabled demo remains writable as before.

**Acceptance Scenarios**:

1. **Given** an authenticated user with read access but without management access, **When** the user inspects a host, **Then** feature information is visible while single and bulk edits, setting edits, Reset, and Apply are unavailable.
2. **Given** an authentication-disabled or anonymous demo session, **When** the user opens feature management, **Then** the existing demo behavior is preserved.

### Edge Cases

- A cascade changes a feature outside the current text or category filter.
- Locally retained settings on a disabled feature are omitted by the existing Apply contract; review must describe the outgoing clear rather than promise those retained values.
- More than one pending direct edit requires the same feature to change.
- A later edit reverses a prior direct or automatic change, leaving the feature at its loaded baseline.
- Reverting a direct enable leaves its required dependency changed under the existing cascade; that retained automatic effect still needs a truthful explanation.
- The operator switches between Studio and Server while a review is open; drafts and reviews must remain host-specific.
- The loaded feature catalog is refreshed or replaced while a review is open.
- The backend reports a stale configuration revision or refuses activation; both remain visible without false success or an unsupported explanation.
- Available metadata does not establish a reload consequence; the review must not invent one or promise hot reload, activation, or rollback.
- A user can inspect but cannot manage features; demo behavior without authentication remains unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The feature-management experience MUST retain the existing single and bulk enable/disable behavior for required feature dependencies and dependants.
- **FR-002**: Optional dependencies MUST NOT cause an automatic feature change solely because the related feature is enabled or disabled.
- **FR-003**: Drafts MUST remain isolated by selected host; a review MUST identify whether it applies to Studio or Server and MUST NOT affect the other host.
- **FR-004**: Before Apply, the operator MUST be able to review every net change between the selected host's loaded state and the complete state that Apply will submit, including applicable direct setting edits and regardless of the current feature filter. Locally retained values omitted for a disabled feature MUST NOT be presented as submitted; any outgoing configuration clear MUST remain visible.
- **FR-005**: The review MUST distinguish direct edits from automatically affected features. For automatic changes required by current pending edits, it MUST show the required dependency or dependant path from each applicable root. An automatic change retained after its initiating edit is reverted MUST instead be explained as an earlier cascade effect, with historical context clearly distinguished from a current pending requirement. It MUST NOT invent a direct edit or a current requirement. Direct setting edits MUST follow existing handling for sensitive setting values.
- **FR-006**: For each automatic change, the review MUST identify all applicable pending direct edits that require it. Explanations MUST be recalculated from the remaining pending draft when edits change; reverted or net-zero changes MUST NOT be shown as pending impact. A retained automatic effect may identify its earlier dependency path only as history, with no effective pending root asserted for it.
- **FR-007**: The review MUST show known reload implications for the selected host when that information is available. It MUST label unknown implications as unknown and MUST NOT imply an unverified hot reload, activation, or rollback guarantee.
- **FR-008**: A review MUST be bound to the selected host, the complete desired feature state, the loaded feature catalog snapshot, and the current feature-configuration revision represented by that snapshot.
- **FR-009**: A draft mutation, selected-host change, catalog refresh, or catalog replacement MUST invalidate the review; Apply MUST NOT silently use an invalidated review.
- **FR-010**: Apply MUST use the same selected host and complete feature state that the operator reviewed, together with the configuration revision represented in that review.
- **FR-011**: A canceled review MUST leave the draft available for further editing and MUST NOT change saved host configuration.
- **FR-012**: Reset MUST restore only the selected host's draft to its loaded state and MUST invalidate a review of the previous draft.
- **FR-013**: Backend conflicts and other Apply failures MUST remain visible as failures with their available reason; the experience MUST NOT show success for a rejected Apply or identify every conflict as a stale revision.
- **FR-014**: Authenticated users who can inspect feature management but lack existing management access MUST be able to inspect available information but MUST NOT be able to stage, bulk-edit, reset, or apply changes. Existing backend authorization remains authoritative.
- **FR-015**: Anonymous or authentication-disabled demo behavior MUST remain as it is today.
- **FR-016**: The review's revision guarantee MUST remain bounded to the loaded catalog snapshot and the existing feature-configuration revision. It MUST NOT claim that a later out-of-band package or dependency-catalog change is atomically detected by that revision.
- **FR-017**: This feature MUST reuse the existing feature-management and cascade behavior; it MUST NOT add automatic enablement beyond the existing required-dependency cascade, broaden the managed host scope, or add shared configuration or deployment behavior.
- **FR-018**: Opening the review dialog MUST move focus inside it, contain keyboard navigation, and support Escape cancellation with focus returned to the invoking control; cancellation MUST NOT apply changes.

### Key Entities *(include if feature involves data)*

- **Host-scoped feature draft**: The operator's complete desired feature state for one selected host, compared with that host's loaded state.
- **Impact entry**: One net feature change, classified as direct or automatic, with any applicable pending root edits, dependency reasons, changed setting fields where applicable, and known reload implications.
- **Impact review**: A host-specific view of the complete draft, loaded feature catalog snapshot, and configuration revision. It is valid only while those reviewed inputs remain unchanged.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the single and bulk enable/disable acceptance scenarios, the review includes every net feature change, including changes hidden by the active filter, and labels direct and automatic changes distinctly.
- **SC-002**: In overlapping-cascade scenarios, every automatically affected feature identifies all applicable pending direct edits and required-dependency reasons; net-zero features are absent. Retained automatic effects without an applicable pending root are explained as earlier cascade effects without false direct or pending-edit claims.
- **SC-003**: In both Studio and Server scenarios, Apply changes only the host and complete draft named by the current review; a changed draft, target, or catalog cannot be applied under an old review.
- **SC-004**: Cancellation and Reset do not change saved configuration; stale revisions and other backend refusals remain visible without a false success state.
- **SC-005**: Authenticated read-only scenarios allow inspection but no draft-changing or Apply action, while the existing unauthenticated demo behavior is preserved.

## Assumptions

- Existing feature management remains scoped to the currently selected Studio or Server host. The user does not select or manage additional hosts through this change.
- The loaded feature catalog supplies the dependency relationships used to explain the current draft. Optional relationships are not required edges and do not create automatic changes.
- The existing configuration revision protects feature configuration. It does not atomically detect package or dependency metadata changes that occur after the catalog was loaded; refreshing or replacing the catalog requires a new review but does not strengthen that backend guarantee.
- Existing reload information may be incomplete. The review reports only known implications and does not promise immediate activation, hot reload, or rollback.
- Existing read and management access rules continue to apply. Authenticated read-only users may inspect where allowed; existing backend enforcement remains authoritative, and anonymous or authentication-disabled demo behavior is preserved.
- This feature does not introduce a new dependency model, automatic enablement beyond the existing required-dependency cascade, cluster-wide management, shared host configuration, or deployment behavior. Existing feature-management behavior is described in [Shell Feature Management](../072-shell-feature-management/spec.md).
