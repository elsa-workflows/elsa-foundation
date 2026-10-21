# Feature Specification: Direct Home authoring paths

**Feature Branch**: `agent/codex/3063-home-golden-path`

**Created**: 2026-10-22

**Status**: Approved

**Input**: [#3063](https://github.com/elsa-workflows/elsa-foundation/issues/3063), the Home slice of Studio UX epic #3057 / Elsa 4 Simplification #2559: durable Create workflow, Continue editing and Needs attention entry points while preserving configurable widgets.

## User Scenarios & Testing

### User Story 1 - Start a workflow from Home (Priority: P1)

A workflow author can start creating directly from Home, with Browse workflows available when they want the collection instead.

**Why this priority**: The current Create destination opens only the collection, requiring the author to find creation again.

**Independent Test**: Choose Create workflow on Home, complete or cancel the existing creation flow, and verify its normal readiness, failure and successful exact-definition behavior.

**Acceptance Scenarios**:

1. **Given** creation is permitted, **When** the author chooses Create workflow, **Then** the existing creation dialog opens without an additional collection action or full reload.
2. **Given** the author cancels creation, **When** they refresh or navigate Back and Forward, **Then** the consumed creation request does not reopen the dialog.
3. **Given** creation choices are unavailable or fail to load, **When** the author enters creation, **Then** its existing explanation, retry and readiness checks remain effective; submission is not silently attempted.
4. **Given** the current account is known to lack creation authority, **When** Home is shown, **Then** Create remains discoverable with a plain-language reason for its disabled state and Browse workflows remains available where permitted.

### User Story 2 - Continue the intended draft (Priority: P1)

A returning author can resume the exact current draft they intend, without guessing between definitions with the same name or silently creating a replacement.

**Why this priority**: Continuing work must preserve identity and distinguish an absent draft from a failed request.

**Independent Test**: Exercise no, one and multiple current drafts, duplicate names, stale selections and unavailable discovery; observe the selected definition and a recoverable outcome.

**Acceptance Scenarios**:

1. **Given** the complete authorized collection contains exactly one editable current-draft candidate, **When** the author chooses Continue editing, **Then** that exact definition opens with its current draft.
2. **Given** multiple candidates exist, **When** the author chooses Continue editing, **Then** a clearly labelled recent-draft chooser shows up to eight choices, names and stable identities, plus Browse all; it never infers uniqueness from the displayed window.
3. **Given** discovery succeeds with no current drafts, **When** Continue editing is used, **Then** an honest empty state offers appropriate create/browse recovery.
4. **Given** discovery fails or is denied, **When** Continue editing is used, **Then** it shows an unavailable/permission outcome and recovery instead of saying there are no drafts.
5. **Given** a chosen draft disappears or becomes inaccessible before opening, **When** its editor loads, **Then** the exact selected definition stays explicit and recovery is offered without opening another draft or silently creating one.
6. **Given** selections change rapidly, **When** an older request completes late, **Then** it cannot replace the newly selected target with another definition's draft.

### User Story 3 - Find everyday tasks independently of widgets (Priority: P2)

An author or operator sees the everyday task entries before configurable summaries, including after widgets are reordered or hidden.

**Why this priority**: Saved layouts and completed onboarding must not remove the normal path into authoring or incident triage.

**Independent Test**: Use default, reordered, all-hidden and completed-onboarding layouts at both laptop sizes, then navigate to Needs attention and return without changing layout preferences.

**Acceptance Scenarios**:

1. **Given** any supported saved widget order/visibility, **When** Home opens at 1280×800 or 1440×900, **Then** the compact task entries remain discoverable independently of that grid.
2. **Given** the operator chooses Needs attention, **When** Runs opens, **Then** its accepted active-incident view is selected, independently of lifecycle status.
3. **Given** Workflows is not composed, **When** Home opens, **Then** workflow task entries do not offer unavailable module destinations.
4. **Given** keyboard or narrow/zoom use, **When** an author opens and dismisses the draft chooser, **Then** controls remain reachable, focus is contained while open and returns to the trigger after dismissal.

### Edge Cases

- Published definitions with current drafts and published-only definitions are different candidates.
- A ninth or later draft must prevent a false single-candidate shortcut even if only eight are displayed.
- Duplicate or long names require readable stable identities; a hover-only title is insufficient.
- Permission grants may cover a resource subtree or imply reading through management; an absent exact client key alone is not denial.
- Anonymous auth-disabled operation retains its existing posture; feature availability is not authorization.
- Discovery canceled by identity/tenant change cannot expose stale candidates from the previous authority.
- A consumed creation request preserves unrelated location context, and a request targeting another/unsupported intent is not treated as Create.
- Loading, successful empty, request failure, known denial and missing current draft stay distinguishable.

## Requirements

### Functional Requirements

- **FR-001**: Home MUST provide a compact durable Create workflow, Continue editing and Needs attention group outside the configurable widget grid, with Browse workflows available.
- **FR-002**: Create MUST enter the existing creation flow once, preserve its readiness and error behavior, and select the exact created definition on success.
- **FR-003**: Consuming a creation request MUST prevent replay after Cancel, refresh, Back or Forward without discarding unrelated navigation context.
- **FR-004**: Continue MUST use actual current-draft existence from the complete authorized collection; publication state alone MUST NOT imply a mutable draft.
- **FR-005**: Exactly one candidate in that complete result MAY open directly; multiple candidates MUST use an explicit recent chooser of at most eight entries with names, stable identities and Browse all.
- **FR-006**: Empty, loading, failed and denied discovery MUST remain distinct and recoverable. A stale/no-current-draft selection MUST retain exact identity and MUST NOT create or substitute a draft silently.
- **FR-007**: Late responses and authority changes MUST NOT show a different definition's draft or a previous account/tenant's candidates as the current selection.
- **FR-008**: Needs attention MUST navigate to the Runs-owned active-incident view without reimplementing its predicate or conflating incident health with run lifecycle.
- **FR-009**: Feature and permission affordances MUST preserve current module availability, server authorization, wildcard/inherited grants and auth-disabled operation; known-denied Create/Continue actions MUST explain their disabled state in plain language.
- **FR-010**: The change MUST preserve saved widgets, contents, ordering, options and onboarding behavior, ordinary internal navigation and native modified-click behavior for links.
- **FR-011**: New UI MUST use existing Studio tokens and module-safe controls/focus conventions, remain keyboard accessible and reflow at narrow sizes without introducing another design system.

### Key Entities

- **Workflow definition**: stable identity, human name and the existence of its current mutable draft; a published version alone is not a current draft.
- **Recent draft choice**: one authorized current-draft candidate with its stable definition identity and existing update-order metadata.
- **Creation request**: a one-use request to enter the existing dialog, consumed before it can replay through navigation.
- **Home layout**: the user's existing widget document; task navigation does not mutate it.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A permitted author reaches the existing Create dialog from Home in one action; Cancel and subsequent refresh/Back/Forward produce zero unwanted reopenings.
- **SC-002**: Zero, one, multiple, duplicate-name, beyond-eight, failed, denied and stale-draft scenarios all produce the specified explicit outcome with zero silent substitutions or draft creation.
- **SC-003**: All three task entries are discoverable at both 1280×800 and 1440×900 in light/dark with default, reordered and all-hidden widget layouts, preserving the saved layout.
- **SC-004**: Keyboard users can enter, choose from and dismiss the draft chooser with correct focus restoration; narrow/zoom use retains reachable controls.
- **SC-005**: Needs attention opens the accepted active-health Runs view, and feature/permission scenarios retain their existing security boundary.

## Assumptions

- Scope is the already composed Workbench modules. No new backend endpoint, permission policy, runtime/recovery behavior, metrics, widget rewrite or optional module setup is included.
- Continue selects a definition's current draft, not an observed historical draft version. Recent ordering uses the existing update-time convention and is not personal editing history.
- #3061 is qualified. Runs #3062 has merged with reviewed-tree equivalence and resulting-main Studio success; its final Core qualification remains mandatory before this slice merges or qualifies under the [scheduling refinement](https://github.com/elsa-workflows/elsa-foundation/issues/3057#issuecomment-6328158365).
- If a required base gate fails during the recorded overlap, implementation stops until root reconciles that gate.
- Existing valid tests remain valid; changed behavior receives focused observable regression and red/green evidence plus normal-host proof, not a new performance benchmark.
