# Feature Specification: Activity-by-activity execution playback

**Feature Branch**: `agent/codex/4366-activity-playback-spec`

**Created**: 2026-11-13

**Status**: In progress — owner authorized planning, tasks and end-to-end implementation on2026-11-13; coordinated runtime prerequisites have supporting component/native-provider proof; dependent playback awaits full production/composed qualification of the runtime-history dependency described in [plan.md](plan.md).

**Tracking**: [Feature #4366](https://github.com/elsa-workflows/elsa-foundation/issues/4366).

**Program-goal bucket**: **none/free-flow**. Related feature: Timeline workspace and run history ([#3849](https://github.com/elsa-workflows/elsa-foundation/issues/3849)); the accepted handoff scopes coordinated prerequisite implementation without transferring that feature's active claim.

**Input**: The owner requested a GitHub issue and Spec Kit specification for activity-by-activity playback, including previous/next, play/pause, speed controls and timeline scrubbing, so a user can interactively follow the path a workflow run took.

**Scope authority**: The original request authorized capture only. The owner's subsequent2026-11-13 instruction authorizes Spec Kit planning/tasks and end-to-end implementation. This does not establish runtime-history readiness or delivery acceptance, replace #3849's active ownership, or authorize deployment/release.

**Reference**: [Playback interaction concept](design/playback-concept.md), the existing run inspector and [Studio module UI guidance](../../Studio/docs/module-ui.md). The retained concept is illustrative, not product or evidence-readiness proof. Domain terms follow the [Elsa glossary](../../docs/glossary/elsa.md); this specification adds presentation behavior, not new execution semantics.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Walk backward and forward through recorded activities (Priority: P1)

As an operator investigating a run, I want to select its previous or next recorded activity execution so I can follow the actual path without repeatedly finding entries myself.

**Why this priority**: Exact manual traversal is useful independently and is the foundation for every other playback control.

**Independent Test**: Inspect a completed run with a branch, nested scope, loop iterations and a retry. Advance and reverse through its recorded occurrences, then explicitly open Activity details for an earlier occurrence.

**Acceptance Scenarios**:

1. **Given** a selected activity occurrence with neighbors, **When** I choose Next or Previous, **Then** exactly the adjacent eligible occurrence becomes selected, the diagram highlights its activity in its pinned scope, Timeline remains open and control focus stays usable.
2. **Given** multiple occurrences of the same authored activity, **When** I step through them, **Then** each recorded execution retains its own identity, iteration/attempt context and position; activity name alone does not collapse them.
3. **Given** a nested activity, **When** I select its occurrence, **Then** its enclosing scope is revealed without silently filtering the run timeline; composite executions and children remain distinct steps with visible hierarchy.
4. **Given** an earlier selected occurrence, **When** I explicitly open Activity and return to Timeline, **Then** details refer to that exact occurrence and the playback position is retained.
5. **Given** the first or last eligible occurrence, **When** I inspect the controls, **Then** the unavailable direction is clearly disabled and selection does not wrap.

### User Story 2 - Play the path at a chosen presentation pace (Priority: P1)

As a developer explaining a run, I want playback to advance automatically at a chosen pace and pause immediately when I need to inspect a step.

**Why this priority**: Automatic progression makes long execution paths easier to scan and demonstrate.

**Independent Test**: Play a known five-occurrence sequence at each supported pace, including a recorded long wait. Pause, change pace, navigate manually and reach the end using a controlled presentation clock.

**Acceptance Scenarios**:

1. **Given** a paused cursor before the end, **When** I choose Play, **Then** progression starts from that position, visits every next eligible occurrence once and keeps the diagram and timeline selection synchronized.
2. **Given** playback in progress, **When** I choose Pause, **Then** the cursor remains at the selected occurrence and no pending automatic advance changes it afterward.
3. **Given** a selected pace of 0.5×, 1×, 2× or 4×, **When** playback advances, **Then** presentation intervals are respectively 2 seconds, 1 second, half a second or a quarter second per activity step; this cadence does not pretend to reproduce execution duration.
4. **Given** a recorded 52-minute wait, **When** playback reaches that occurrence, **Then** its actual wait/timestamps remain inspectable while progression follows the selected presentation pace.
5. **Given** the final step or a known gap, **When** progression reaches it, **Then** playback stops with an accurate boundary state; replay from the beginning requires an explicit action.
6. **Given** playback in progress, **When** I explicitly open Activity details or manually navigate the diagram, **Then** playback pauses immediately with no queued automatic advance; Activity details retain the chosen occurrence and returning to Timeline stays paused at that occurrence.

### User Story 3 - Scrub directly to a recorded position (Priority: P1)

As an operator, I want to scrub backward or forward to an activity position so I can revisit an interesting portion of a long run quickly.

**Why this priority**: Sequential stepping alone is slow when the relevant occurrence is far from the current cursor.

**Independent Test**: Seek through a paged run, including rapid changes of direction and an occurrence outside the initially loaded range. Verify the final selection against the recorded identity and reopen its details.

**Acceptance Scenarios**:

1. **Given** a sequence with a known range, **When** I drag or use the keyboard to change its scrubber position, **Then** the requested recorded occurrence becomes selected with its position and original timestamp; the scale is activity position, not elapsed wall-clock time.
2. **Given** automatic playback, **When** I scrub, step manually or select another timeline entry, **Then** playback pauses and remains paused after that navigation.
3. **Given** several pending seek requests, **When** an older result arrives after a newer choice, **Then** it cannot replace the newer cursor or its activity details.
4. **Given** a target requiring more retained history to load, **When** I seek there, **Then** the requested position is shown as loading separately from the last confirmed selection; failure offers retry without presenting unrelated evidence as the target.

### User Story 4 - Understand ordering, gaps and live arrivals truthfully (Priority: P1)

As an operator, I want playback to distinguish recorded order from causation, available history from missing history, and my inspection position from the live run.

**Why this priority**: A visually convincing path is misleading if concurrency, missing records or current values are presented as historical facts.

**Independent Test**: Inspect a parallel run with equal timestamps, a coarse/partially retained history and an active run receiving new records. Change scope and access while paused or playing.

**Acceptance Scenarios**:

1. **Given** concurrent branches, **When** I traverse their recorded occurrences, **Then** the ordering is stable, concurrency remains visible and advancing between entries does not claim a causal edge between them.
2. **Given** a known missing interval, **When** playback approaches it, **Then** it stops at the available segment boundary and identifies the gap instead of joining the surrounding activities into a continuous path.
3. **Given** an active run, **When** additional records arrive, **Then** they are indicated without moving the cursor or extending the session's playable upper bound; including them is an explicit paused refresh.
4. **Given** absent activity-level recording, unknown ordering or an unavailable pinned diagram, **When** I inspect playback, **Then** its precise limitation is visible; retained timeline facts remain inspectable without fabricated steps or diagram traversal.
5. **Given** an access revocation or a switch of run/backend, **When** it takes effect, **Then** playback stops and stale protected selection/evidence cannot appear in the new context.

### Edge Cases

- No eligible records, one step, selection outside the chosen scope, and a count that is unknown rather than zero.
- Equal/out-of-order wall-clock timestamps, parallel branches, repeated authored node IDs, retries and a composite whose lifetime overlaps its descendants.
- A continuation within the same occurrence, a currently waiting occurrence, or a terminal run whose history has not fully materialized.
- A selected item expiring from retention, a gap before/inside the current range, and a seek target loading after the run/backend changes.
- Pause or a pace change during buffering, repeated Play clicks, manual navigation at a boundary, and replay explicitly requested after the last step.
- An unchanged live cursor while new occurrences arrive; changing filters while playing; focus leaving/backgrounding the view; reduced motion.
- Denied metadata versus denied payload versus an unavailable pinned diagram; missing captured values must not become configured/current values.

## Requirements *(mandatory)*

### Functional Requirements

#### Sequence and selection

- **FR-001**: Playback MUST be read-only inspection. Its controls MUST NOT execute, pause, resume, retry, alter or rewind the workflow, deliver a stimulus, or change recording/persistence policy. Pausing playback MUST be clearly distinct from existing live-update or runtime controls.
- **FR-002**: Each step MUST represent one recorded activity execution occurrence, not an authored node or a lifecycle event. Distinct loop/retry execution identities remain separate. Waiting/resuming within one execution MUST NOT create extra steps merely because multiple history events describe it.
- **FR-003**: Eligible occurrences MUST follow authoritative per-run recorded order at their first recorded execution position, with a stable documented tie-break where needed. Equal timestamps and concurrency MUST NOT imply causation or serial execution. If usable ordering cannot be established, playback for that range MUST report unavailable rather than manufacture an order.
- **FR-004**: The default run sequence MUST include retained authorized activity occurrences across nesting levels, including composites, with visible parent/child context. Existing explicit activity-scope filters MUST be reflected in the playback scope label and eligible sequence. Selecting a step MUST NOT silently narrow the timeline's scope.
- **FR-005**: Previous/Next MUST select exactly one adjacent eligible occurrence. Boundary directions MUST be disabled, with no implicit wrap. Opening playback MUST be paused and use the existing selected occurrence when eligible, otherwise the first available step with that choice visible.
- **FR-006**: Stepping, scrubbing and direct timeline selection MUST share one exact occurrence selection with the pinned diagram and explicit Activity details. Timeline MUST remain active during playback navigation; explicit user navigation to another view remains available and pauses playback. Reveal the selected activity's diagram scope when available without stealing keyboard focus from playback/timeline controls or repeatedly resetting timeline reading position. A manually selected diagram node with multiple occurrences MUST require an explicit occurrence choice rather than guessing a new playback cursor.
- **FR-007**: The interface MUST distinguish the playback cursor and its recorded timestamp from the run's latest status and current time. Ordinary latest-state badges MUST NOT become purported historical snapshots. Values shown for a selected occurrence MUST retain captured provenance and access rules; current values MUST NOT substitute for missing historical values.

#### Play, pause and speed

- **FR-008**: Play MUST advance forward from the current selection through every eligible step once. Pause MUST prevent all subsequent automatic cursor changes until an explicit Play action. Playback MUST stop at the last step; Play MUST then be disabled or explicitly relabeled Replay, with restarting at the first step requiring that explicit action.
- **FR-009**: Presentation pace MUST offer 0.5×, 1×, 2× and 4×, defaulting to 1×. At 1× the intended cadence is one step per second; the other cadences are defined in Story 2. Changing pace affects future progression without changing the selected identity, skipping steps or changing recorded timestamps/durations.
- **FR-010**: Long execution/wait durations MUST be compressed by the presentation cadence, with actual durations retained as recorded facts. Unavailable durations remain unknown. Playback speed MUST NOT be labeled as a guarantee of real-time execution replay.
- **FR-011**: Manual step, scrub, timeline-row selection, manual diagram navigation or explicitly opening Activity details MUST pause automatic progression immediately, preventing queued automatic advances before inspection continues. Activity details and return to Timeline MUST retain the chosen occurrence and stay paused. Backgrounding the playback view or leaving the run MUST pause it; restoring the view MUST NOT resume automatically or catch up by skipping steps.

#### Scrubbing and range

- **FR-012**: A scrubber MUST seek by ordinal activity position in the identified playback sequence. It MUST expose the selected position and timestamp, and a total only when known. An incomplete or loaded segment MUST be labeled as such rather than shown as the complete run or assigned a fabricated full-run percentage.
- **FR-013**: Pointer and keyboard scrubbing MUST reach the same occurrences. Rapid seeks MUST resolve to the latest user choice; delayed results from retired selections MUST NOT change its cursor, diagram, details or focus.
- **FR-014**: Seeking/stepping across retained page boundaries MUST preserve stable occurrence identity and order without duplicates or omissions. Loading a distant position MUST use bounded reads; users MUST NOT have to download the entire run history before playback becomes usable.
- **FR-015**: When a target is not yet available, the interface MUST distinguish pending navigation from the last confirmed position, suspend automatic advancement, and expose loading, retry or unavailability accurately. Normal next-page buffering MUST resume the prior playing session only after confirmation and only if no intervening pause, manual navigation or context change occurred. Failed loads and manual seeks MUST leave playback paused; successful retry MUST confirm the requested identity and remain paused.

#### Evidence, concurrency and live history

- **FR-016**: Traversed paths/edges MUST be highlighted only when retained evidence establishes the transition. Adjacency in playback, matching endpoint statuses or close timestamps MUST NOT establish a traversed edge. An unvisited-looking branch MUST be described as having no recorded visit unless the available coverage establishes that it was not taken.
- **FR-017**: Parallel activity occurrences MUST retain their branch/scope identities. Playback ordering is a browsing order over recorded facts; the interface MUST NOT imply that selecting the next parallel occurrence made the preceding one finish or caused the next to start.
- **FR-018**: Every session MUST identify its run, effective scope, coverage and playable upper bound. Live arrivals MUST be indicated separately and MUST NOT move selection or silently extend the end. Explicit inclusion of new records MUST happen paused and preserve the selected identity when it remains eligible.
- **FR-019**: A known retention/recording gap MUST break continuous playback. Playback MUST stop at that segment boundary with a visible reason; crossing into another available segment requires explicit paused navigation and MUST NOT draw an invented connecting path.
- **FR-020**: Eligibility MUST depend on actual available recording and ordering, not an assumption that a persistence-mode name guarantees full history. Coarse, disabled, legacy, retention-expired, unsupported and denied cases MUST remain distinguishable. This feature MUST NOT force Durable recording or add a new capture/store requirement by implication.
- **FR-021**: A missing/denied pinned diagram MUST be reported without substituting a current definition. Authorized recorded timeline occurrences MUST remain inspectable where possible, with diagram/path highlighting explicitly unavailable.
- **FR-022**: Changing run, backend, effective scope, available-history segment or access MUST pause progression and invalidate pending navigation from the former context. Within the same run, retain an eligible selected identity; otherwise explain that it is outside the new range before selecting an explicit new position.
- **FR-023**: Playback reads, counts, positions, labels and details MUST preserve existing tenant/environment and permission boundaries. Authorization loss MUST stop playback and clear inaccessible evidence; scrubbing MUST NOT reveal hidden occurrence counts, names, values or identifiers.

#### Accessibility and delivery

- **FR-024**: All controls MUST have accessible names, keyboard operation, visible focus and non-color selection/status cues. The scrubber MUST announce meaningful position/activity changes without announcing every animation frame. Reduced-motion preferences MUST retain full stepping/playback functionality without unnecessary motion.
- **FR-025**: The affected experience MUST remain usable at desktop and 320px widths in light/dark themes, with controls reachable and no page-level horizontal overflow. Loading, empty, one-step, populated, ended, partial/gap, failed/retry and denied/unavailable states MUST be distinguishable.
- **FR-026**: Planning MUST verify the delivered evidence capabilities and gaps against [spec 1211](../1211-run-history-timeline/spec.md) and existing confirmed-motion rules before implementation. Any missing evidence contract requires a separately reviewed dependency; a Draft history proposal or latest-state projection is not proof that recorded traversal is supported.
- **FR-027**: Delivery MUST include meaningful behavior regressions and [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md): current-head real composed-host journeys, downloadable screenshots/accessibility snapshots/traces and independent inspection. Required examples include branch choice, nested/loop/retry identity, parallel ordering, page-boundary scrubbing, a long wait, a history gap, live arrival, rapid-seek recovery and access loss. Illustrative concepts and fixture-only results MUST remain separate from product acceptance.

### Key Entities *(include if feature involves data)*

- **Playback sequence**: The authorized, ordered activity occurrences in an identified run/scope and available segment, with coverage, known bounds and gap/continuation information. It consumes recorded evidence rather than introducing another execution history.
- **Playback cursor**: The selected exact activity execution identity, its ordinal position in that sequence and any separately identified pending target. Position is presentation; identity remains stable across reads and live arrivals.
- **Presentation state**: Paused/playing/ended/buffering/unavailable, chosen cadence and current-session bounds. These are inspection states and confer no workflow execution authority.
- **Recorded path evidence**: Existing retained activity/scope/transition facts and their coverage/provenance that determine which highlights and durations are supportable. Their meanings remain owned by the existing runtime/history contracts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In branch, nested, loop and retry reference journeys, forward traversal visits 100% of the eligible recorded occurrences once in the documented order; reverse traversal selects the exact reverse sequence. Explicit Activity details identify the selected occurrence in every case.
- **SC-002**: Under a controlled presentation clock, all four pace options produce their specified cadence with no skipped/duplicate steps. Pause, manual navigation, backgrounding and range changes produce zero further automatic advances until explicit Play.
- **SC-003**: In a 200-occurrence reference run crossing retained page boundaries, seeking to the first, middle, final and an older repeated occurrence confirms each exact identity. Ten rapidly changed seek targets settle on the final choice without a stale-selection flash or loading the entire history first.
- **SC-004**: A reference run containing a 52-minute recorded wait advances according to the presentation cadence while preserving the original timestamp and wait duration. Parallel/gap examples show zero unsupported causal-edge or continuous-path claims.
- **SC-005**: New live records cause zero unsolicited cursor/end-bound changes. Gap, unavailable and revoked-access examples produce zero fabricated historical values or protected occurrence disclosures.
- **SC-006**: Every required control and recovery action is operable by keyboard and pointer at desktop/320px in both themes, with focus preserved, meaningful position announcements and zero page-level horizontal overflow. Independent review of current-head composed evidence finds no unresolved usability defect in these journeys.

## Assumptions

- All four requested control groups are required for this feature; story priority describes delivery value, not permission to omit controls.
- Playback advances through recorded activity occurrences at their recorded entry positions. It does not reconstruct the complete workflow state at a point in time or add wait/resume/fault event steps within one occurrence.
- The first scope is run inspection, including its explicit nested/activity scopes. Adding playback to attached editor Test Runs is a separate follow-up; existing editor/runtime navigation is preserved.
- One-step-per-second at 1×, the four pace options, paused initialization and manual navigation pausing are reasonable defaults adopted from the interaction discussion. Reverse traversal is manual; reverse autoplay is out of scope.
- Existing run-history work supplies or will qualify ordering, scope, coverage, stable navigation and permissions. Planning must establish actual readiness; this specification neither reopens the retired playback decision in #3398 without new evidence nor assumes every history slice is delivered.
- Live execution may continue while the user inspects a bounded recorded range. Playback state is local presentation state; it requires no cross-device saved session or persistent playback preferences.
- No runtime/domain/persistence contract or constitutional amendment is proposed here. Draft/provisional constitutional sections are not ratified or extended by this specification.

### Non-goals

Runtime debugging/breakpoints, engine pause/resume/rewind/re-execution, full historical variable snapshots, event-by-event lifecycle playback, reverse autoplay, a global order across runs, inferred causal replay, automatic workflow execution, new recording/persistence infrastructure, whole-run export, release or deployment.
