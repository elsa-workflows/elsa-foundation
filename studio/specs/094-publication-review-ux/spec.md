# Feature Specification: Workflow Publication Review UX

**Feature Branch**: `[not-created]`

**Created**: 2026-07-28

**Status**: Draft

**Input**: User description: "Redesign the workflow publication dialog so authors can understand what will be published, where it will be published, and whether it is safe at a glance; explain publication channels, prevent success and alert content from hiding dialog actions, and support a capability-gated exact semantic-version override."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Review a routine publication at a glance (Priority: P1)

As a workflow author, I can review the publication destination, version, effect, readiness, and material draft changes in one compact view so that routine publishing feels understandable rather than intimidating.

**Why this priority**: Most publications target the normal channel and should require attention only to the facts that affect the decision.

**Independent Test**: Open publication review for a valid draft targeting the normal publication channel and verify that the destination, automatically assigned version, replacement effect, readiness, comparison baseline, compact change summary, and Publish action are understandable without opening technical details.

**Acceptance Scenarios**:

1. **Given** a valid draft and a populated normal publication channel, **When** review opens, **Then** the author sees that the current publication will be replaced, which version is expected, which published version is the comparison baseline, and whether publication is ready.
2. **Given** a draft with no blocking trigger or policy concerns, **When** review opens, **Then** policy evidence, internal identifiers, concurrency details, and authoritative claims remain available but collapsed.
3. **Given** the author publishes from the review, **When** the current editor contains changes, **Then** the captured current state is included and saved as part of publication without requiring a separate Save action.

---

### User Story 2 - Choose a publication channel with clear consequences (Priority: P1)

As a workflow author, I can choose an existing publication channel or create a named channel and immediately understand whether the choice creates or replaces a publication.

**Why this priority**: The current free-text Slot field and publication-behavior radios expose implementation language while obscuring the actual release effect.

**Independent Test**: Switch among the normal channel, an occupied named channel, and creation of a new named channel and verify the derived effect, comparison baseline, validation, and publication readiness for each choice.

**Acceptance Scenarios**:

1. **Given** existing publication channels, **When** the author opens the channel selector, **Then** those channels are listed and the normal channel is clearly identified.
2. **Given** an occupied channel is selected, **When** its authoritative review completes, **Then** the dialog states that the channel's current publication will be replaced and compares the draft with that channel's published version.
3. **Given** the author chooses to create a channel, **When** a valid unused name is entered, **Then** the dialog states that a separate publication channel will be created and explains that no previous publication exists for comparison.
4. **Given** the author changes channel, **When** authoritative review is required, **Then** the dialog checks the new target automatically, keeps Publish disabled while checking, and surfaces any conflict beside the affected decision without changing the primary action into a separate review command.

---

### User Story 3 - Complete or recover from publication without losing controls (Priority: P1)

As a workflow author, I always retain access to the dialog actions and receive a focused outcome or recovery view after publication progresses.

**Why this priority**: Success, validation, and partial-failure messages can currently push the only Close action outside the visible dialog area.

**Independent Test**: Exercise success, blocking validation, stale preflight, promotion failure, activation failure, and long-content states at supported viewport sizes and verify that the current action controls remain visible and each state offers the correct recovery.

**Acceptance Scenarios**:

1. **Given** publication succeeds, **When** the result arrives, **Then** the review is replaced by a compact success state showing the published version and channel, with visible Close and Open published executable actions.
2. **Given** a version is promoted but channel activation fails, **When** the failure is shown, **Then** the dialog explains that the promoted version was retained and offers Retry publication and Close without promoting another version.
3. **Given** validation, conflict, or technical detail makes the content taller than the available space, **When** the author scrolls the details, **Then** the header context and action footer remain visible.
4. **Given** routine replacement of the normal channel is ready, **When** the author chooses Publish, **Then** no redundant checkbox or nested confirmation interrupts the action.

---

### User Story 4 - Override an automatically assigned version when supported (Priority: P2)

As an advanced workflow author, I can override the automatically assigned version with an exact semantic version when the connected backend supports that capability.

**Why this priority**: Routine authors should not confront version policy, while release managers sometimes require an exact forward version or prerelease label.

**Independent Test**: Connect to backends with and without custom-version support and verify capability-gated visibility, forward semantic-version validation, authoritative checking, and final publication labels.

**Acceptance Scenarios**:

1. **Given** the backend does not advertise custom-version support, **When** review opens, **Then** the automatic version is shown as policy-assigned and no unusable override control appears.
2. **Given** the backend advertises custom-version support, **When** the author chooses Edit version, **Then** an exact-version field appears while automatic assignment remains the default.
3. **Given** a valid unused semantic version greater than the latest promoted version, including a forward prerelease, **When** authoritative review completes, **Then** the requested version can be published.
4. **Given** an invalid, duplicate, or non-forward version, **When** it is entered or checked, **Then** publication is blocked with a specific explanation and no version is promoted.

### Edge Cases

- The workflow has never been published; the normal channel has no baseline and the draft changes are described as a first publication.
- A named channel becomes occupied, changes publication, or disappears while the dialog is open; authoritative review detects the stale target before mutation.
- A channel name differs only by surrounding whitespace or reserved-name casing; it cannot create an ambiguous duplicate or misuse the normal channel.
- The selected named channel is occupied even though the author originally chose the create-channel path; the resulting replacement risk is shown before publication.
- The draft has no material behavior changes but does contain layout or saved editor-state changes; the dialog does not invent semantic-version reasoning from the change summary.
- Trigger claims are non-blocking; they remain in technical details. Trigger conflicts are blocking; they surface automatically.
- The automatic version shown before publication differs from the server-confirmed version; the confirmed value replaces it in the outcome.
- A custom prerelease version is greater than the latest stable version according to semantic-version precedence.
- Publication succeeds but refreshing the editor or opening the executable fails; success remains truthful and the secondary failure is reported separately.
- Keyboard and screen-reader users can reach the channel selector, disclosures, version override, scrolling content, and all current actions.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Publication review MUST use one single-page modal with a persistent header, one independently scrollable content region, and a persistent action footer.
- **FR-002**: The default review MUST prioritize publication channel, version, publication effect, readiness, comparison baseline, and a compact change summary.
- **FR-003**: The user-facing term MUST be "Publication channel"; "slot" MAY remain an internal contract term but MUST NOT be the unexplained primary field label.
- **FR-004**: The dialog MUST explain that the normal `default` channel represents the workflow's normal publication and that a named channel such as `canary` keeps a separately addressable publication.
- **FR-005**: Publication behavior MUST be derived from the selected channel rather than chosen through separate replace-versus-side-by-side controls.
- **FR-006**: The channel control MUST list existing channels and provide a distinct create-new-channel path; free text MUST only be requested while creating a channel.
- **FR-007**: Selecting the normal or any occupied channel MUST state that its current publication will be replaced.
- **FR-008**: Creating an unused named channel MUST state that a separate publication channel will be created.
- **FR-009**: The compact change summary MUST state its comparison baseline as a channel and version, or state that no previous publication exists.
- **FR-010**: Detailed activity, input, output, and trigger counts MUST remain available through progressive disclosure.
- **FR-011**: Policy source and revision, trigger claims, internal artifact and Source Reference identities, and concurrency details MUST remain available through an advanced-details disclosure.
- **FR-012**: Blocking validation errors and trigger conflicts MUST surface automatically and MUST NOT depend on opening a disclosure.
- **FR-013**: Changing publication channel or exact version MUST automatically invalidate stale authoritative evidence and initiate a new authoritative review.
- **FR-014**: Publish MUST remain the stable primary mutation label; it MUST be disabled with an inline checking state while authoritative review is pending.
- **FR-015**: Routine replacement of the normal channel MUST NOT require another checkbox or nested confirmation after the review.
- **FR-016**: The dialog MUST explain that the captured current editor state is included and saved as part of publishing.
- **FR-017**: Successful publication MUST replace the review body with a compact outcome showing the confirmed version and channel.
- **FR-018**: The success outcome MUST offer Close and Open published executable actions.
- **FR-019**: A failure after promotion but before channel activation MUST preserve the promoted version, explain the partial outcome, and offer Retry publication without repeating promotion.
- **FR-020**: Validation failure before promotion MUST explain whether the draft was saved and MUST state that no version or publication was created.
- **FR-021**: Appending success, error, conflict, or validation content MUST NOT move the current action controls outside the visible modal.
- **FR-022**: The automatic version MUST be labeled as assigned according to version policy and MUST NOT be described as inferred from draft changes.
- **FR-023**: Exact-version editing MUST be hidden unless the connected backend advertises support.
- **FR-024**: When supported, exact-version editing MUST be optional and progressively disclosed; automatic assignment MUST remain the default.
- **FR-025**: A requested exact version MUST be a valid, unused semantic version with precedence greater than the latest promoted version; forward prerelease versions MUST be allowed.
- **FR-026**: Invalid, duplicate, or non-forward exact versions MUST be rejected authoritatively before promotion.
- **FR-027**: Channel or version changes MUST be protected against stale concurrent publication state before mutation.
- **FR-028**: The review, success, and recovery states MUST remain keyboard operable, retain visible focus, expose validation and status changes to assistive technology, and restore focus when closed.
- **FR-029**: Existing publication permissions and host/workflow policy authority MUST remain unchanged.
- **FR-030**: The Studio redesign MUST remain compatible with backends that do not implement exact-version override.

### Key Entities

- **Publication Review**: The captured workflow state, selected channel, requested or automatic version policy, comparison baseline, change summary, readiness, authoritative evidence, and current publication phase shown before mutation.
- **Publication Channel**: A named address for one active workflow publication. The normal channel is `default`; a named channel can be created or can replace its own current publication.
- **Promoted Version**: An immutable workflow definition version created from the captured draft, with a server-confirmed semantic version.
- **Publication Preflight**: Authoritative, short-lived evidence that resolves policy, target channel, requested version, trigger claims, conflicts, and whether activation is allowed.
- **Publication Outcome**: The confirmed promoted version, Executable, Source Reference, publication channel, and success or recoverable partial-failure state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In acceptance testing, authors identify the publication channel, expected version, replacement-or-create effect, readiness, and comparison baseline within 10 seconds without opening technical details.
- **SC-002**: A routine ready publication can be confirmed with one primary action after the modal opens and without a nested confirmation.
- **SC-003**: At every supported viewport height, 100% of review, validation, success, and recovery scenarios keep the current action footer visible while long details remain scrollable.
- **SC-004**: Every channel choice tested—normal, occupied named, and new named—shows the correct create-or-replace effect and comparison baseline before publication.
- **SC-005**: Every target or version change invalidates stale readiness and displays a new authoritative result before Publish becomes available.
- **SC-006**: Backends without exact-version support complete all automatic-version publication scenarios with no unavailable override control or regression.
- **SC-007**: With exact-version support, 100% of valid forward semantic versions and forward prereleases tested are accepted, while invalid, duplicate, and non-forward versions are rejected before promotion.
- **SC-008**: Keyboard and assistive-technology verification reaches and identifies every channel, disclosure, version, status, and action control without relying on pointer or hover behavior.

## Assumptions

- Existing publication policy remains authoritative for the default channel and for whether named channels are allowed.
- Existing publication permissions govern both automatic and exact-version publication; this feature introduces no new role.
- Exact-version support is an additive backend capability and older deployments remain supported.
- The connected Foundation service remains responsible for validating and assigning the final promoted version.
- Publication channel names continue to follow the backend's existing identity and reserved-name rules.
- Existing Executable and Source Reference retention, inspection, and concurrency semantics remain unchanged.
- The Studio redesign and overflow correction can ship before exact-version capability is available.
