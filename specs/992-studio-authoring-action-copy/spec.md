# Feature Specification: Studio Authoring Action Copy and Toolbar Consistency

**Feature Branch**: `agent/codex/3072-authoring-action-copy`

**Created**: 2026-10-20

**Status**: Approved — root reviewed the source-backed eligibility, label and geometry boundaries on 2026-10-20; implementation and verification remain pending.

**Input**: User description: "#3072: make Studio authoring actions truthful and consistent across workflow and activity authoring while preserving existing action eligibility and lifecycle behavior."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Recognize and safely submit a workflow test (Priority: P1)

As a workflow author, I can identify the toolbar action as **Test Run**, understand that it uses the connected environment and may cause real side effects, submit the generic setup with its existing module-supplied submission label or fallback intact, and retain direct access to the test result details.

**Why this priority**: Authors must distinguish a test from publishing or starting an ordinary workflow run, and must understand the environment and side effects before they submit it.

**Independent Test**: Open a workflow with a transient test action and inspect the toolbar action and its accessible name, loading copy, setup submission label, trigger-provided field labels, and warning before submitting. After submission, verify the complete accessible status text and direct result-details affordance remain available while the action and status change.

**Acceptance Scenarios**:

1. **Given** a workflow with a transient test action, **When** the author views or accesses the toolbar action that opens its setup, **Then** its visible label and accessible name identify it as “Test Run.”
2. **Given** the generic Test Run setup is loading, **When** the author sees its loading state, **Then** the visible and accessible loading copy identifies the Test Run setup.
3. **Given** the generic test setup is open with no module-supplied submission label, **When** the author reviews its fields and submit action, **Then** the existing trigger-provided input labels remain associated with the correct inputs and the generic submission's visible label and accessible name are “Start Test Run.”
4. **Given** the test setup has a module-supplied submission label, **When** the author reviews its fields and submit action, **Then** that submission label and accessible name and the existing trigger-provided input labels remain intact and associated with their respective actions and inputs.
5. **Given** the workflow test will execute in a connected environment, **When** the author is asked to submit it, **Then** the connected-environment and real-side-effects warning remains visible before submission.
6. **Given** a workflow test has run, **When** the author views its status and result, **Then** the full accessible status text and direct result-details affordance remain available.
7. **Given** saving, testing, or publication status changes for the same workflow definition at the same viewport, **When** the author views the primary toolbar, **Then** the Test Run action remains visible and its position relative to the other primary actions remains stable, including when a conditional test status or result is present.

### User Story 2 - Choose the truthful activity draft action (Priority: P1)

As an activity author, I can tell whether I will continue one authorized draft, create a draft from the current version, start a draft from an available starting point, or open a chooser when the draft state cannot safely identify one action.

**Why this priority**: Clear actions prevent authors from believing that an existing draft or version will be selected or cloned when that choice is not established.

**Independent Test**: Present each supported draft state and verify the action label and destination identify only the draft or version authorized by that state. For incomplete, ambiguous, or denied states, verify the existing drafts chooser remains available with exact draft identities.

**Acceptance Scenarios**:

1. **Given** the confirmed active-draft list is complete and contains exactly one active draft that this author is allowed to edit, **When** the author opens the activity authoring actions, **Then** the action is “Continue editing draft” and continues that exact draft.
2. **Given** the confirmed active-draft list is complete and contains zero active drafts, the activity definition has a current head and that exact head is confirmed cloneable, and the author is allowed to create a draft, **When** the author opens the activity authoring actions, **Then** the action is “Create draft from current version” and identifies that exact version.
3. **Given** the confirmed active-draft list is complete and contains zero active drafts, the activity definition explicitly has no current head, and the author is allowed to create a draft, **When** the author opens the activity authoring actions, **Then** the action is “Create draft” and opens the existing starting point without claiming that no version has ever existed.
4. **Given** none of the preceding direct-action conditions can be confirmed, **When** the author opens the activity authoring actions, **Then** “Open drafts” remains available and any listed drafts retain their exact identities. A confirmed single editable draft does not require permission to create another draft or to read or clone the current head.
5. **Given** save, test, or publication status changes for the same activity definition at the same viewport, **When** the author views the primary toolbar, **Then** draft actions remain visible and in their stable primary positions, including when the Activity publication status is present.

### User Story 3 - Understand save and publication status from action copy (Priority: P2)

As a Studio author, I can understand what a save or publication action promises and whether it has completed from its label and status copy.

**Why this priority**: Consistent wording is useful only when it preserves the meaning and completion promise of each operation.

**Independent Test**: Compare the existing workflow and activity review-before-publication entries in primary and menu presentations. Verify each uses “Review & publish,” separate workflow “Publish” mode and post-preflight “Continue publication review” remain distinct, and “Save now” continues to mean the existing immediate flush operation.

**Acceptance Scenarios**:

1. **Given** an existing review-before-publication entry is shown as a primary action or menu option, **When** the author views it, **Then** its label is “Review & publish” while its existing subsequent steps remain unchanged. The separate workflow “Publish” mode keeps its label, remembered choice, and behavior.
2. **Given** “Save now” is offered, **When** the author selects it, **Then** its copy continues to accurately promise the existing immediate flush behavior.
3. **Given** an operation is pending or has failed, **When** its status is shown, **Then** the copy does not report successful completion before that operation has completed successfully.

### Edge Cases

- “Create draft” is available only when the complete active-draft list is confirmed to contain zero active drafts, the activity definition explicitly has no current head, and the author is allowed to create a draft.
- “Create draft from current version” is available only when the complete active-draft list is confirmed to contain zero active drafts, the exact current head is confirmed cloneable, and the author is allowed to create a draft.
- If no direct action can be confirmed because the draft list, required current head or relevant permission is unavailable, retain “Open drafts” and existing exact draft identities. The confirmed single-editable-draft path does not depend on head or create permissions.
- More than one active draft: retain the existing chooser and exact draft identities.
- The generic Test Run setup is loading: its visible and accessible loading copy identifies the setup as Test Run.
- A trigger supplies field labels or a module supplies a submission label in the generic test setup: preserve those labels and associations; when there is no module-supplied submission label, use “Start Test Run.”
- A workflow test is connected to an environment where execution can cause real side effects: keep the warning visible before the author submits.
- Save, test, and publication status text changes for the same editor definition while the viewport is unchanged: primary actions remain visible and their positions differ by no more than 1 CSS pixel, including when conditional Test Run result/status or Activity publication status is present.
- At widths from 320 to 1440 pixels, primary actions remain visible and usable with no hidden action or overflow; when viewport width changes, responsive layout may adapt.
- A publication or run action has different semantics from a comparable save action: retain copy that accurately reflects its own operation.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The workflow toolbar action that opens the transient test setup MUST be labeled “Test Run” wherever the author can see or access it, including its accessible name.
- **FR-002**: The generic workflow test setup MUST retain trigger-provided field labels and their association with the corresponding inputs, and MUST preserve a module-supplied submission label and accessible name; when no module-supplied submission label exists, its submission's visible label and accessible name MUST be “Start Test Run.”
- **FR-003**: While the generic workflow test setup is loading, its visible and accessible loading copy MUST identify the Test Run setup.
- **FR-004**: The generic workflow test setup MUST retain the existing connected-environment and real-side-effects warning before submission.
- **FR-005**: After a workflow test, the author MUST retain direct access to its result details and the complete accessible test-status text.
- **FR-006**: Activity authoring MUST offer “Continue editing draft” only when the complete, confirmed active-draft list contains exactly one active draft that the author is allowed to edit; that action MUST continue that exact draft.
- **FR-007**: Activity authoring MUST offer “Create draft from current version” only when the complete, confirmed active-draft list contains zero active drafts, the exact current head is confirmed cloneable, and the author is allowed to create a draft; the action MUST use that exact head.
- **FR-008**: Activity authoring MUST offer “Create draft” only when the complete, confirmed active-draft list contains zero active drafts, the activity definition explicitly has no current head, and the author is allowed to create a draft; the action MUST open the existing starting point without claiming that no version has ever existed.
- **FR-009**: When none of FR-006–FR-008 applies, activity authoring MUST retain “Open drafts” and preserve exact draft identities where drafts are listed. FR-006 takes precedence and MUST NOT require permission to create a draft or to read or clone the current head.
- **FR-010**: Each existing workflow and activity review-before-publication entry, whether a primary action or menu option, MUST use “Review & publish”; this adds no new menu. The separate workflow “Publish” mode, its remembered choice, and the post-preflight “Continue publication review” step MUST retain their existing labels and meanings.
- **FR-011**: Save and publication-entry copy in scope MUST be aligned only where the actions have equivalent user-visible semantics and completion meaning; existing action eligibility and lifecycle behavior MUST remain unchanged.
- **FR-012**: “Save now” MUST continue to accurately promise its existing immediate flush behavior, and pending or failed operations MUST NOT be described as successfully complete.
- **FR-013**: For the same editor definition and viewport, primary authoring actions MUST remain visible and each action's on-screen position MUST differ by no more than 1 CSS pixel while save, Test Run, and publication statuses change, including conditional Test Run status/result and Activity publication status.
- **FR-014**: Across viewport widths from 320 through 1440 pixels, including 320, 768, 1024, and 1440 pixels, primary authoring actions MUST remain visible and usable without being hidden in overflow; responsive layout may adapt when viewport width changes.

### Key Entities *(include if feature involves data)*

- **Activity draft and activity definition version**: Existing authoring records retain their exact identities and permissions; the [Elsa glossary](../../docs/glossary/elsa.md) owns their domain meanings. Their confirmed state determines the action copy in this feature.
- **Workflow test action**: A transient action that submits a workflow test against the connected environment and can produce real side effects.
- **Trigger-provided label**: A label supplied for a workflow test setup field and retained with that field.
- **Authoring status**: The visible save, test, or publication state that must accurately reflect whether the associated operation is pending, complete, or failed, with complete accessible status text.

### Scope Boundaries

This feature covers truthful authoring action and setup-loading copy, equivalent publication-entry copy, activity draft action copy, existing Test Run result/details access, and stable visibility and positioning of primary toolbar actions while statuses change for the same editor definition and viewport. It does not change Home routing, Runs detail naming, the separate actual published-workflow run action or its input-dialog copy, retry behavior, runtime behavior, publication semantics, autosave behavior, draft-selection semantics, the expression editor, or backend and SDK behavior.

### Success Criteria *(mandatory)*

#### Measurable Outcomes

- **SC-001**: Activity authoring offers “Continue editing draft” only when the complete, confirmed active-draft list contains exactly one draft the author may edit. It offers “Create draft from current version” only when that list is confirmed to contain zero drafts, the exact current head is confirmed cloneable, and the author may create a draft. It offers “Create draft” only when that list is confirmed to contain zero drafts, the definition explicitly has no current head, and the author may create a draft. Otherwise, it retains “Open drafts” and exact draft identities where listed.
- **SC-002**: The workflow toolbar action and setup-loading state identify “Test Run.” The generic submission fallback is “Start Test Run”; supplied submission and input labels remain intact with their associations. The existing warning appears before submission, and direct result details and the full accessible status remain available afterward.
- **SC-003**: For the same editor definition at each fixed viewport width of 320, 768, 1024, and 1440 pixels, each primary authoring action's on-screen position differs by no more than 1 CSS pixel while save, Test Run, and publication statuses change, including conditional Test Run status/result and Activity publication status. Primary actions remain visible and usable without overflow throughout 320–1440 pixels. Responsive layout may adapt when the viewport changes.
- **SC-004**: Existing workflow and activity review-before-publication entries use “Review & publish”; separate workflow “Publish” mode and the post-preflight “Continue publication review” step keep their existing labels and meanings. “Save now” retains its immediate flush promise and no pending or failed operation is described as successfully complete.

## Assumptions

- Existing authorization, draft eligibility, version identity, cloning, routing, and lifecycle behavior remain authoritative; this feature changes how existing choices are named and presented.
- A draft action uses a confirmed complete active-draft list and its relevant permission. Creating also requires the applicable explicit head state; continuing an editable draft does not require head or create permissions. An unreadable or unconfirmed head is not evidence that no head exists.
- The connected-environment and real-side-effects warning already exists and remains part of the workflow test submission experience.
- A lack of a current version does not establish that the activity has never had a version.
- “Equivalent semantics” means the same user-visible operation and completion meaning; similar labels alone do not establish equivalence.
- Responsive layout may adapt when viewport width changes, provided all primary actions remain visible and usable within the stated range.
