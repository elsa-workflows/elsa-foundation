# Feature Specification: Timeline workspace and durable run history

**Feature Branch**: task-ordered `agent/codex/3849-history-slice-*` stack; see [delivery slices](slices.md).

**Created**: 2026-11-05

**Status**: In progress — resumed on 2026-11-12; the closed prototype has not landed and each replacement slice needs current-source qualification.

**Tracking**: [Feature #3849](https://github.com/elsa-workflows/elsa-foundation/issues/3849).

**Delivery authority**: The [5 November end-to-end instruction](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6645438505) remains in force. On [9 November](https://github.com/elsa-workflows/elsa-foundation/pull/3850#issuecomment-6739905865), the owner closed #3850 without merge and required stacked PRs of roughly 3,000 lines each, one specification slice per PR in task order. The [11 November decision](https://github.com/elsa-workflows/elsa-foundation/issues/3849#issuecomment-6794241516) requires the Data view to build on compact Inputs landed in #4108. The owner resumed remaining work on 12 November. Partial slices do not close #3849. Release/deployment and unrelated parked-program reactivation remain excluded.

**Program-goal state**: none/free-flow; direct owner-requested specification capture.

**Input**: The owner could not see bookmark resumption in Run inspection and requested a coherent chronological history of activity execution, stimuli, bookmarks, faults and related behavior, with an activity journal and bookmark inventory. After reviewing the exploration, the owner selected the **Timeline workspace** and instructed: “Please capture this in a GitHub issue and a Spec Kit spec, covering both the Studio UX and the durable runtime history needed to support it.”

**Design reference**: [Selected Timeline workspace](design/timeline-workspace.md). The retained image is illustrative design reference, not production evidence. The diagram-dominant alternative was not selected.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Explain the recorded run journey (Priority: P1)

As an operator, I want a chronological history alongside the workflow diagram so I can understand what happened, what the run is waiting for and why it continued or failed.

**Why this priority**: Activity start times and latest statuses cannot explain a long wait and its resumption.

**Independent Test**: Execute a purchase approval through a real user-task wait and completion. Follow delivery and bookmark events, switch to the pinned diagram and back, and reopen the run. The journey and identities remain consistent.

**Acceptance Scenarios**:

1. **Given** an activity created a bookmark and later continued, **When** I open the run, **Then** its history distinguishes start, bookmark creation, waiting, stimulus receipt, recorded admission/queueing, consumption, continuation and outcome with linked identities and truthful coverage.
2. **Given** the run is waiting, **When** I inspect its summary, **Then** I see waiting activities and expected stimuli without confusing an activity wait with the workflow aggregate's runtime status.
3. **Given** a populated history, **When** I switch Highlights/All events or apply scope, category or identifier filters, **Then** counts disclose the selection and every retained authorized event remains reachable.
4. **Given** a selected execution/event, **When** I switch Timeline/Diagram, **Then** exact execution selection is preserved, the diagram uses the relevant pinned executable, and its latest-state overlay is not implied to be historical replay.
5. **Given** a completed run with an earlier resolved incident, **When** I inspect it, **Then** the original fault and resolution remain visible despite no active incidents.

### User Story 2 - Inspect one activity execution (Priority: P1)

As a developer, I want an activity summary, journal, bookmarks and data scoped to one execution so I can diagnose iterations, retries and continuations without mixing occurrences.

**Why this priority**: An activity drill-down that combines executions can misrepresent an otherwise correct run history.

**Independent Test**: Execute repeated/parallel iterations, a retry lineage and a nested scope. Select an earlier execution and refresh while another progresses. Only the explicit scope and its related delivery observations appear.

**Acceptance Scenarios**:

1. **Given** several executions of a node, **When** I select one, **Then** occurrence, iteration and attempt/continuation context are explicit and siblings are excluded.
2. **Given** an explicitly selected earlier execution, **When** more events/executions arrive, **Then** selection does not silently move to the latest.
3. **Given** a composite, **When** I inspect its journal, **Then** descendant events are excluded by default and can be included through an explicit scope choice.
4. **Given** values captured at start and continuation, **When** I inspect Data or an event value, **Then** its phase and provenance remain visible; later/current values are not substituted for earlier observations.
5. **Given** an activity selected on the diagram, **When** its inspector opens, **Then** the run timeline retains its previous scope until I explicitly focus it on that execution.

### User Story 3 - Follow bookmark lifecycles (Priority: P1)

As an operator, I want all bookmarks an execution created, including historical handles, so I can see what was awaited, which delivery consumed a handle and why another became unusable.

**Why this priority**: Current-store bookmark state cannot establish past resumption or its cause.

**Independent Test**: Create multiple handles, consume one, retire a losing wait and create a subsequent wait. Inspect exact handles after completion/restart. Separately observe an expired unconsumed handle without delivering a timeout stimulus.

**Acceptance Scenarios**:

1. **Given** several bookmarks, **When** I open Bookmarks, **Then** active/lifetime counts and exact ownership are clear without inferring “any one” or “all required” semantics from their number.
2. **Given** a consumed handle, **When** I inspect its lifecycle, **Then** creation and consumption remain linked to delivery and continuation, with successful completion a separate fact.
3. **Given** cancellation, supersession, a competing winner or another recorded removal, **When** I inspect the handle, **Then** its actual reason/cause is visible and removal is not called successful resumption.
4. **Given** expiry passes without a runtime transition, **When** I inspect the handle, **Then** current unmatchability is evaluated at an identified observation time without a fabricated timeout, cancellation, consumption or wake-up event.
5. **Given** another wait after continuation, **When** I inspect bookmarks, **Then** successive handles retain separate identities/history and replay does not duplicate an existing handle's semantic creation.

### User Story 4 - Explain delivery and causation (Priority: P1)

As an operator or integrator, I want message arrival, accepted delivery, bookmark consumption and continuation results to remain distinct so I can explain why a message did or did not advance a run.

**Why this priority**: Arrival and successful execution can have different outcomes.

**Independent Test**: Deliver a valid stimulus, a duplicate, competing concurrent deliveries, an explicitly targeted unmatched stimulus and a stimulus whose continuation faults. Follow the known causal chains and verify only actual effects are claimed.

**Acceptance Scenarios**:

1. **Given** accepted delivery with pending continuation, **When** I inspect it, **Then** receipt/admission and pending work are distinct; consumption requires a committed consumption fact.
2. **Given** consumption followed by a continuation fault, **When** I follow history, **Then** consumption, fault and incident are separately linked.
3. **Given** redelivery or competing deliveries, **When** they settle, **Then** attempts/dispositions and actual effects are distinguishable without fabricated extra execution or consumption.
4. **Given** an authorized target run with no eligible matching handle, **When** the disposition is available, **Then** it is inspectable without an invented continuation. Unattributed stimuli belong to available delivery diagnostics rather than an arbitrary run.
5. **Given** a child run or broadcast recipients, **When** I follow causation, **Then** authorized targets are navigable and legitimate multiple recipients are not mistaken for duplicate delivery to one handle.

### User Story 5 - Trust durable history and its limits (Priority: P1)

As an operator, I want committed history to survive restart/failover with explicit coverage so I can distinguish missing facts from delayed, uncaptured or unavailable ones.

**Why this priority**: A complete-looking interface backed by volatile or best-effort information creates false confidence during failures.

**Independent Test**: With fresh production-composed processes and frozen copies of an owned durable store, exercise pre-commit failure, post-commit acknowledgement loss, delayed materialization and failover/redelivery. Compare identities, order, lifecycles and coverage after recovery.

**Acceptance Scenarios**:

1. **Given** required history cannot become durable with a transition, **When** commit is attempted, **Then** a history-enabled durable path cannot acknowledge that transition as committed while silently losing history responsibility.
2. **Given** commit succeeded but acknowledgement or materialization failed, **When** another process recovers/redelivers, **Then** the original fact remains once semantically under its original identity/order; materialization can recover without rolling back committed workflow state.
3. **Given** pending materialization or an unresolved gap, **When** I inspect the range, **Then** terminal workflow status, timeout or empty reads do not make it settled and complete.
4. **Given** Balanced/Ephemeral coarse inspection, **When** I inspect history, **Then** mode, grouping and granularity are truthful; history neither changes checkpoint cadence nor claims durability for uncommitted work.
5. **Given** late capture, retention loss, unsupported capability or a legacy uncaptured run, **When** I inspect it, **Then** its state/boundary is explicit rather than a verified empty full-run history.
6. **Given** two nodes recover/materialize the same responsibility, **When** they settle it, **Then** identities, atomic groups and workflow-local order remain stable without another semantic occurrence.

### User Story 6 - Investigate long histories (Priority: P2)

As a support engineer, I want bounded search, exact links and live catch-up so I can investigate long-running executions without downloading every event or losing my place.

**Why this priority**: Production histories outlive browser sessions and can contain many executions.

**Independent Test**: In a 10,000-event retained history, locate a bookmark beyond the first page, follow exact links, reconnect after appends and catch up from the previous position without a full client scan.

**Acceptance Scenarios**:

1. **Given** paged history, **When** I page/filter/seek while events append, **Then** bounded reads preserve stable order without skipping or duplicating retained events.
2. **Given** I am reading earlier history, **When** new events arrive, **Then** scroll/focus/selection remain stable with an explicit new-events indication.
3. **Given** an authorized retained event/bookmark/execution link, **When** I reopen it, **Then** it resolves the exact target/scope; missing/inaccessible targets are explicit and do not silently select another occurrence.
4. **Given** query/update transport failure, **When** loaded rows remain visible, **Then** stale freshness and retry are clear rather than “no events.”

### User Story 7 - Inspect safely and accessibly (Priority: P1)

As an authorized Studio user, I want readable history that respects my access, keyboard navigation and theme so I can diagnose runs without exposing protected information.

**Why this priority**: History increases the quantity and lifetime of diagnostic information.

**Independent Test**: Exercise the mounted Studio journey in both themes and wide/narrow layouts with keyboard input, metadata-only and payload-authorized identities, and another tenant/environment. Inspect rendering and direct reads/searches/links.

**Acceptance Scenarios**:

1. **Given** metadata access without payload access, **When** I inspect events/values, **Then** allowed metadata remains useful and withheld payloads have explicit availability.
2. **Given** another tenant/environment's identity, **When** it is searched, linked or requested, **Then** results, counts, payloads, attribution and related links disclose no unauthorized information.
3. **Given** null, not produced, omitted, redacted/withheld, truncated, expired or unavailable values, **When** I inspect them, **Then** their applicable states remain distinguishable.
4. **Given** the composed inspector, **When** I use its controls/drill-downs at desktop and 320px widths in both themes, **Then** keyboard focus, labels, selection, statuses and recovery remain usable without color or hover alone.

### Edge Cases

- Equal timestamps, clock skew and daylight-saving changes do not establish semantic order or global causation.
- Multiple facts in an atomic checkpoint are not separately committed intermediate states.
- An activity can wait while its workflow aggregate remains Running.
- Stale-handle consumption/cleanup for a terminal execution does not prove another activity continuation.
- A continuation can create another bookmark and wait again; timer and bookmark lifecycles are distinct.
- Cancellation, recovery, supersession and migration can retire handles/change pinned context; preserve identities and recorded reasons.
- No execution record does not itself prove an explicit skip.
- Passive expiry cannot stand in for an authored timeout/control path.
- Duplicate transport attempts, repeated business stimuli and broadcast recipients are different cases.
- A related run or executable graph can be missing/denied while authorized retained metadata remains inspectable.
- Capture changes, unknown kind versions and unavailable sources must not produce invented detail.
- Ephemeral with completion recording disabled can leave no history for a never-persisted run; history must preserve that no-record contract.

## Requirements *(mandatory)*

### Functional Requirements

#### Run timeline and selection

- **FR-001**: Studio MUST provide the selected full-workspace Timeline as a primary peer of Diagram. A directly opened run defaults to Timeline unless the user explicitly selected another supported view.
- **FR-002**: The summary MUST identify current progress/waiting, known waiting activities and expected stimuli without changing workflow/activity lifecycle semantics.
- **FR-003**: Timeline MUST be oldest first with readable labels, activity context, timestamps and compact wait intervals. Unsupported/uncaptured transitions MUST NOT be invented from latest state.
- **FR-004**: Highlights MUST retain bookmark, delivery-disposition, fault and recovery milestones while reducing routine detail. All events MUST expose all retained authorized events in scope; counts/filters MUST disclose presentation reduction.
- **FR-005**: Users MUST have explicit run/activity scope, category and recorded identifier/text filtering. Activity selection MUST NOT silently filter run history; queries/counts MUST enforce the same access boundary.
- **FR-006**: View changes, refresh and live updates MUST preserve exact execution/event selection and applicable reading context. Stable links MUST resolve exact retained targets and report missing/denied targets explicitly.
- **FR-007**: Details MUST expose known causal predecessors/successors and related activity/bookmark/delivery/run links. Filtered-out causal events MUST remain reachable with visible scope context.
- **FR-008**: Historical selection MUST NOT imply reconstructed historical canvas state. Diagram MUST label its state context and use relevant pinned executable identity rather than a later edited definition.

#### Activity journal and bookmarks

- **FR-009**: Activity inspection MUST name a concrete execution and its occurrence/iteration/attempt lineage. Descendants MUST be explicit and excluded by default; directly related delivery observations remain identifiable.
- **FR-010**: Each execution MUST provide a concise summary plus Journal, Bookmarks and Data. Journal MUST use the same event identities/order as run history and preserve existing execution browsing/stable selection.
- **FR-011**: Attributable delivery/causation observations MUST be distinguished from facts committed/produced by that activity. Matching timestamps or display names alone MUST NOT establish relationships.
- **FR-012**: Captured inputs, outputs, outcomes and values MUST retain phase/provenance; later/current values or configuration MUST NOT replace uncaptured historical observations.
- **FR-013**: Bookmarks MUST include every recorded handle created by the execution, including historical handles, with separate active/lifetime counts. Descendant ownership MUST remain distinct.
- **FR-014**: A bookmark MUST expose available safe purpose/stimulus/target, identity/owner, creation, lifecycle, consumption/removal time/reason, expiry, wait duration and causal links, subject to access.
- **FR-015**: Consumption, cancellation/removal, supersession and unknown disposition MUST remain distinguishable. Missing store state MUST NOT mean successful resumption; consumption MUST NOT imply successful continuation/completion.
- **FR-016**: Current eligibility MUST be separate from recorded lifecycle. Expiry MUST identify its observation time and MUST NOT fabricate timeout, wake-up, consumption or cancellation events.
- **FR-017**: Repeated waits MUST preserve successive handle identities/history. Recovery/redelivery MUST NOT duplicate an existing handle's semantic creation or overwrite it with a replacement.

#### Semantic coverage and causation

- **FR-018**: Receipt, validation/admission, dispatch/queueing, consumption, continuation and outcome MUST remain distinct when recorded. Acknowledged arrival/admission MUST NOT be shown as successful execution.
- **FR-019**: Recorded duplicate/rejected/unmatched/unavailable/retried deliveries MUST expose actual disposition and effect relationships. Unattributed stimuli MUST NOT be attached to arbitrary runs; available delivery diagnostics MAY be linked without claiming nonexistent capture.
- **FR-020**: Races MUST identify committed winners and known losing-wait retirement. Multiple recipients/handles MUST NOT imply duplicate effects, universal single-consumption semantics or unspecified wait-completion rules.
- **FR-021**: Supported semantic coverage MUST include workflow/activity lifecycle, bookmarks, stimuli/delivery, timer scheduling/firing/cancellation, incidents/resolution, retry/recovery, operator control/alteration, dispatch/children, compensation and captured state/value-change metadata. Runs/ranges MUST disclose applicable capture support; optional module facts require explicit support.
- **FR-022**: Historical faults/incidents/resolution MUST remain visible after active incident counts reach zero. Transient delivery/telemetry failures MUST NOT automatically become committed workflow faults.
- **FR-023**: Children, external sources and broadcast recipients MUST use explicit causation and independently authorized navigation. Actor/source attribution MUST use trusted recorded provenance or identify its unavailability.

#### Durable capture, ordering and coverage

- **FR-024**: History-enabled production composition MUST capture minimum semantic metadata for new eligible runs without QA sessions or a Studio connection. Payload capture remains separate; absent/disabled history MUST be discoverable and visible.
- **FR-025**: Each supported committed transition in a history-enabled durable path MUST durably retain its history or recoverable history responsibility with that transition. Required capture failure MUST prevent acknowledgement as committed; successful commit MUST NOT silently discard history responsibility.
- **FR-026**: Post-commit materialization/query failure MUST NOT roll back committed workflow state. Recovery MUST retain stable semantic identities and idempotent results across acknowledgement loss, replay, competing nodes and retry.
- **FR-027**: Events MUST retain stable identity, workflow-local monotonic ordering and stable ordinal/group within an atomic commit. Atomic groups MUST be inspectable; timestamps/cross-run arrival MUST NOT assert global semantic order.
- **FR-028**: History MUST preserve Durable/Balanced/Ephemeral cadence and the existing no-record exception. A committed segment MUST retain its supported observed semantic transitions, rather than only its final state snapshots, with available observation timing/phase and the shared commit boundary/actual inspection granularity. Rolled-back/uncommitted work MUST NOT become committed history; history MUST NOT force activity-level checkpoints or manufacture intermediate facts from a final projection.
- **FR-029**: Durable ingress/admission observations MUST identify their own recording/commit provenance and known target association. They MUST remain distinct from committed target execution and best-effort telemetry; cross-store atomicity and exactly-once external delivery are not implied.
- **FR-030**: Ranges MUST expose coverage and settled boundaries, including pending materialization and unresolved gaps. Terminal workflow status, timeout or an empty page MUST NOT alone establish completeness or absence of an event.
- **FR-031**: Capture/materialization failure, partial/late/disabled capture, unsupported capability and transient read failure MUST have distinguishable query/UI states. Readable rows MUST remain inspectable with honest freshness/coverage after update failure.
- **FR-032**: Capture/configuration changes MUST retain effective coverage boundaries. Older history MUST NOT be backfilled by inference from current state; unsupported families MUST remain visible in coverage.
- **FR-033**: Durable retention MUST be explicit, aligned by default with the retained run's lifetime. An explicit earlier purge MUST expose its boundary/unavailability; volatile buffers/storage pressure MUST NOT silently convert partial history to a complete range. Shorter payload retention MUST have explicit disposition.
- **FR-034**: Fresh-process restart and supported cross-node failover MUST preserve retained committed facts, handle lifecycles, order, causation and pending history responsibility. Recovery MUST NOT be shown as another occurrence of already committed business execution.
- **FR-035**: Kind/version evolution MUST retain safe readable common metadata for unknown supported kinds/versions. Invalid/unreadable ranges MUST expose limitations rather than silently drop records or fabricate detail.

#### Queries, live updates and durations

- **FR-036**: Browsing/filtering/exact navigation MUST use bounded ordered reads and stable continuation. Appends/reconnect/catch-up MUST not skip/duplicate retained events or require a whole-history browser download/sort.
- **FR-037**: Live updates MUST preserve explicit selection, focus and reading position. Following the end MUST be explicit; new events while reading history MUST have an accessible indication. Workflow completion and history settlement MUST remain distinct.
- **FR-038**: Elapsed, active, waiting and delivery/queue intervals MUST be distinguished when supported. Unknown intervals MUST remain unknown; concurrent durations MUST NOT be summed into false wall-clock totals, and waiting MUST NOT be called active execution.
- **FR-039**: Timestamps MUST provide understandable time-zone context and exact instants on inspection. Clock skew, equal timestamps and daylight-saving changes MUST NOT override local ordering or establish causation.

#### Authorization, modularity and acceptance

- **FR-040**: Discovery, reads, search, counts, direct/related links and any existing export MUST enforce runtime tenant/environment/partition and user access on the server. Payload access MUST remain independent; identifiers/links are not authority.
- **FR-041**: Existing sanitization/sensitive-value policy MUST apply before durable storage of history or recoverable responsibility. Secrets, credentials, signed resume capabilities and ungoverned exception/request content MUST NOT leak through labels, payloads, attribution or links.
- **FR-042**: Values MUST distinguish applicable null, not produced, not captured/omitted, redacted/withheld, truncated, retention-expired and unavailable states. History MUST NOT weaken existing capture/read permissions.
- **FR-043**: History MUST preserve runtime ownership, artifact-only execution and Design-only/Runtime-only/combined deployments. Planning MUST reuse supported commit/contribution seams and the existing first-party persistence family; this spec admits no new domain or concrete storage/query API design.
- **FR-044**: Inspection MUST be read-only. Existing recovery/control links MUST preserve command access and MUST NOT turn event selection into replay, manual consumption or another side effect.
- **FR-045**: Loading, complete/partial/disabled history, empty filtered results, waiting/completed/faulted, denied, stale and unavailable states MUST be supported. Desktop/320px, both themes, keyboard focus, accessible labels and non-color status cues MUST be qualified.
- **FR-046**: Acceptance MUST include current-head composed-host journeys, downloadable visual/interaction evidence and independent inspection under [Studio UX acceptance](../../docs/contributing/studio-ux-acceptance.md). Mockups and green assertions alone MUST NOT constitute product acceptance.
- **FR-047**: Planning MUST establish deterministic bounded capture/write/query work consistent with mode/checkpoint budgets and behavior/recovery controls. Timing benchmarks or new permanent measurement infrastructure MUST NOT replace existing correctness gates.

### Key Entities *(include if feature involves data)*

Use [canonical glossary meanings](../../docs/glossary/elsa.md) for executions, bookmarks, incidents, checkpoints and execution evidence. These are product information requirements, not new classes or storage schemas.

- **Run history event**: A retained semantic fact or classified delivery observation with stable identity, kind/version, workflow association, order/commit provenance, instants and authorized causal references. Their authorities remain distinct.
- **Activity journal scope**: One execution with occurrence/iteration/attempt lineage and explicit descendant inclusion. It selects the same history rather than creating another event source.
- **Bookmark lifecycle view**: History for an exact execution-owned handle plus separately identified current eligibility, safe purpose/target and related delivery/continuation.
- **Delivery observation**: Receipt/admission/dispatch/attempt/disposition with its own provenance and target association; its existence does not prove execution effects.
- **History range and coverage**: Authorized retained/captured range, support/configuration boundaries, continuation position, granularity and settled/materialization status.
- **Captured value reference**: A policy-governed observation linked to phase/event with explicit disposition and independently authorized payload access.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the composed approval journey, an operator can reach a bookmark's creation, consumption/removal and causal delivery within three navigation actions from its selected activity. All three scopes show identical event identities/order.
- **SC-002**: Repeated, parallel, nested and retried fixtures show zero unrelated execution events/values in default selected-execution scope, with stable selection during progress/reopening.
- **SC-003**: Pre-commit failure, post-commit acknowledgement loss, delayed materialization, restart and supported failover fixtures preserve every acknowledged retained committed fact exactly once semantically under original identity/order after recovery, with no uncommitted fact presented as committed.
- **SC-004**: All negative/partial states in stories 4–7 are distinguishable from complete empty history. No fixture reports completeness with a known unsettled gap/history responsibility.
- **SC-005**: In a 10,000-event history with pages of at most 100 events, operators can find a later bookmark, reopen its exact link and catch up after appends without omitted/duplicated events, lost selection or a whole-history download. Sizes are correctness fixtures, not timing benchmarks.
- **SC-006**: Capture/access-negative journeys expose zero protected payloads, secret/resume capabilities or cross-tenant/environment identifiers through history, counts, search and related navigation; allowed metadata remains useful.
- **SC-007**: Independent current-head composed inspection passes required states, both themes, keyboard use and 320px/desktop layouts; failed/missing/uninspected states remain incomplete.
- **SC-008**: Representative Durable/Balanced/Ephemeral journeys with supported history preserve outcomes, bookmark ownership and checkpoint/no-record semantics, exposing actual granularity/coverage instead of changing behavior to suggest completeness.

## Assumptions

- **Authority**: The UX selection and end-to-end delivery are owner-approved. In progress status records active implementation; it is not proof of accepted behavior or authorization to deliver unrelated parked work.
- **Production composition**: Within explicit history-enabled composition, minimum semantic metadata is captured for new eligible runs by default. Earlier runs/absent hosts retain honest unavailable states; payload capture stays separate. Configuration packaging/module placement belong to planning.
- **Retention**: Default history retention follows the retained run's lifetime, with explicit deployment purge/retention and separate value policy. No fixed numeric period or silent in-memory eviction is assumed.
- **Existing policy**: [ADR 0080 D2](../../docs/adr/0080-elsa-4-simplification-decisions.md#d2--persistence-modes-balanced-by-default) governs cadence and deterministic budgets. [Bookmark expiry](../../docs/bookmark-expiration.md) remains passive. This spec exposes those policies without amending them.
- **Persistence boundary**: [ADR 0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md) remains governing for first-party durable persistence; this feature does not revive the superseded provider direction in historical QA evidence work.
- **Related work**: [#3305](https://github.com/elsa-workflows/elsa-foundation/issues/3305) overlaps mode/granularity; [#3375](https://github.com/elsa-workflows/elsa-foundation/issues/3375)/[#3642](https://github.com/elsa-workflows/elsa-foundation/issues/3642) concern execution browsing/stability; [#3814](https://github.com/elsa-workflows/elsa-foundation/issues/3814)/[#3828](https://github.com/elsa-workflows/elsa-foundation/pull/3828) and [#3844](https://github.com/elsa-workflows/elsa-foundation/issues/3844)/[#3847](https://github.com/elsa-workflows/elsa-foundation/pull/3847) contain adjacent toolbar/User Tasks work. Reconcile live heads during planning, without implicit supersession.
- **QA boundary**: The [QA Execution Evidence program](../../docs/program-goals/runtime-execution-evidence.md) is parked and excludes a human-facing UI. Its [PRD](../../docs/plans/runtime-execution-evidence-prd.md), [#1132](https://github.com/elsa-workflows/elsa-foundation/issues/1132) and causation/durability work are references, not a reactivated program or mandatory evidence-session dependency. The [current collector](../../Essentials/src/Workflows/ExecutionEvidence/README.md) is a process-local MVP, not this production guarantee. No new domain is admitted here.
- **Architecture**: Both [framework v6.1.0](../../.specify/memory/constitution-framework.md) and [Elsa v6.3.0](../../.specify/memory/constitution.md), their Gate indexes and [review checklist](../../docs/contributing/review-checklist.md) apply. Framework §2.24 and Elsa §E2.8 versioning/Model X and §E2.9 remain draft/provisional and are not ratified or relied on to settle new implementation design.
- **Out of scope**: Whole-workflow historical state reconstruction, event-sourcing the engine, replay commands, exactly-once external effects, global cross-run ordering, arbitrary logs/traces as semantic facts, a new global delivery console, blanket values, a new export mechanism, QA/J-Test integration and release/deployment. Existing diagnostics/control/export links retain existing contracts/access.
- **Next phase**: Planning must reconcile current source/PRs, choose ownership and capture/disposition contracts, define supported delivery-diagnostics integration, persistence/query/retention and deterministic budgets, and design qualification slices. No plan/tasks or implementation design is frozen by this specification.
