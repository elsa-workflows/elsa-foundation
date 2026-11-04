# Feature Specification: Elsa 4 StateMachine

**Feature Branch**: agent/codex/3820-state-machine-contract

**Created**: 2026-11-04

**Status**: Draft — provisional; open decisions below prevent specification approval and production implementation.

**Input**: Provide generic hierarchical and parallel state machines in Elsa 4, authored through normal workflow/activity definitions, with root and nested composition, a dedicated Studio designer, ordinary activities, durable events and understandable execution.

**Program**: [#3815](https://github.com/elsa-workflows/elsa-foundation/issues/3815). **M0 feature**: [#3820](https://github.com/elsa-workflows/elsa-foundation/issues/3820). **Goal**: [StateMachine](../../docs/program-goals/state-machine.md).

## Decision and approval boundary

The program's [OD1–OD27 register](https://github.com/elsa-workflows/elsa-foundation/issues/3815#accepted-owner-decisions-4-november-2026) records accepted owner direction. This draft translates it into user journeys and verifiable requirements. It does not approve an internal interpreter, schema, event-admission implementation or technical plan.

The requirements identified as accepted direction below are grounded in that register. Platform constraints reuse existing governance. The proposed completion rules and unresolved contracts have not been accepted; they remain visible in Q1–Q3.

Hierarchy and parallel regions both belong to first-release acceptance. Delivery priority and independently testable stories do not reduce that scope. No implementation plan or implementation task list is authorized by this draft.

## User Scenarios & Testing

### User Story 1 — Author a root or nested machine (Priority: P1)

A workflow author creates a StateMachine through the normal workflow/activity authoring experience, defines its states and transitions, attaches ordinary Elsa activities and conditions, and publishes the result. The same capability can be the workflow root or a child of another activity.

**Why this priority**: StateMachine must compose with the rest of Elsa and have a usable authoring-to-execution path.

**Independent Test**: Author and execute one root machine and one machine nested inside a larger workflow; inspect the authored definition after save/reload.

**Acceptance Scenarios**:

1. **Given** a new workflow, **when** the author chooses StateMachine, creates a small lifecycle, saves, reloads and publishes, **then** its states, transitions, conditions and activity slots retain their intended meaning and the published definition executes.
2. **Given** a larger workflow with work before and after a nested machine, **when** that machine runs to its agreed completion, **then** the parent continues through the normal activity-composition contract.
3. **Given** a machine intended for reuse, **when** it is published through the existing reusable-activity mechanism, **then** callers use the ordinary definition/version/publication lifecycle rather than a separately managed StateMachine definition type.
4. **Given** a machine, compound state with sequential child-state selection, or parallel region, **when** the author configures startup, **then** an explicit initial child state is required and missing or invalid selections are diagnosed. Moving shapes does not change startup behavior.

### User Story 2 — Share behavior through state hierarchy (Priority: P1)

An author places substates inside a containing state and defines shared behavior at that parent. An operator can see the active child and its active ancestors.

**Why this priority**: Hierarchy avoids duplicating shared lifecycle transitions across every substate.

**Independent Test**: Execute a parent with two substates, a parent-level transition, and an overlapping child transition with a condition.

**Acceptance Scenarios**:

1. **Given** eligible child and parent transitions matching the same input, **when** selection occurs, **then** the eligible child transition takes precedence.
2. **Given** no eligible child transition, including a false child condition, **when** an eligible parent transition matches, **then** the parent fallback can be selected.
3. **Given** a state-owned deadline, **when** that state is exited and entered again, **then** the new visit gets a fresh deadline and a completion belonging to the old visit cannot affect it.
4. **Given** a parent remains active while only its child changes, **when** that child transition occurs, **then** parent-owned waits remain intact unless the agreed exit boundary actually includes the parent.
5. **Given** an authored transition exits a containing state, **when** that transition is selected, **then** waits and pending activity work in the exited scope are cancelled according to the agreed lifecycle contract.
6. **Given** a non-final state with no local outgoing edge, **when** it relies on ancestor behavior or remains waiting, **then** it is not inferred to be complete. Explicit final-state intent and accidental-dead-end diagnostics remain distinct; complete scoped completion propagation is Q1.
7. **Given** a transition exits or enters multiple nested states, **when** their authored work runs, **then** exited children finish Exit before their exited parents, and entered parents finish Entry before their entered children. All applicable exits finish before transition Action and Action finishes before entries, including across durable waits.

### User Story 3 — Model independent parallel regions (Priority: P1)

An author models several concurrent lifecycle regions without enumerating every combination as a separate state. Operators see all active regions and why each changed.

**Why this priority**: Parallel regions are explicitly part of the first release, following hierarchical delivery.

**Independent Test**: Run two regions under a shared parent, deliver one event relevant to both, progress one region independently, and finally leave or complete their parent.

**Acceptance Scenarios**:

1. **Given** one event enables compatible transitions in two regions, **when** the machine evaluates it, **then** both regions can react to that same logical event.
2. **Given** a transition affects only one region, **when** it completes, **then** independent waits in the other region remain active.
3. **Given** one region is final and another is still active, **when** completion is evaluated, **then** the parallel state is not yet complete.
4. **Given** every region has reached a final state, **when** completion is evaluated, **then** the parallel state is complete under the agreed enclosing-state completion contract.
5. **Given** an explicit authored transition leaves the shared parallel parent, **when** it is selected, **then** the relevant descendant work is cancelled; early exit is not an implicit any/threshold completion policy.
6. **Given** a host restart while several regions are active, **when** execution resumes, **then** their active configuration and outstanding obligations are recovered without converting local progress into cancellation of unrelated regions.
7. **Given** a parallel state whose regions each have an explicit initial state, **when** that parallel state is entered, **then** every region starts at its own initial state rather than one region being selected instead of the others.

### User Story 4 — Wait durably during selected transition work (Priority: P1)

An author uses an ordinary activity or composite in entry, exit or transition action work, including activities that suspend durably. The operator can distinguish that pending phase from a stable state waiting for another business event.

**Why this priority**: Activity composition must remain durable and its intermediate phases understandable.

**Independent Test**: Suspend selected transition work, accept business events while it is pending, restart the host, finish that work and evaluate the retained events.

**Acceptance Scenarios**:

1. **Given** entry, exit or transition action work suspends, **when** the operator inspects execution, **then** the pending work and its phase are visible.
2. **Given** a transition has been selected and its work is pending, **when** later business events arrive, **then** they do not pre-empt that selected transition and are not evaluated against an old or partially changing configuration while its work is incomplete.
3. **Given** the machine admits a new business event while transition work is pending, **when** the host restarts, **then** the accepted event remains available for later evaluation under the declared delivery contract.
4. **Given** admitted events are pending, **when** selected transition work finishes, **then** subsequent event evaluation follows the agreed ordering contract.
5. **Given** an event has no eligible transition against the configuration in which it is evaluated, **when** its evaluation finishes, **then** an observable unhandled outcome is recorded and later state changes do not revive that event.
6. **Given** a cancelled timer/task from an earlier activation completes late, **when** it is delivered, **then** it cannot be redirected to a later visit. A new machine-addressed business event is a distinct input category.
7. **Given** a selected transition with exit, action and entry work, **when** any applicable phase durably suspends, **then** the observed order remains source Exit → transition Action → target Entry and a later phase does not start before the previous one completes.
8. **Given** source Exit completed and later transition Action work failed, **when** supported normal Elsa recovery is requested, **then** the failed work is resumed/retried at its normal activity recovery boundary and completed earlier phases are preserved rather than restarting the entire transition.

### User Story 5 — Compose triggers and choose deliberate re-entry (Priority: P1)

An author uses ordinary activities/composites as transition triggers and distinguishes an action that preserves a state visit from deliberate exit/re-entry.

**Why this priority**: Reuse and predictable wait lifetimes are central to the accepted activity-based authoring model.

**Independent Test**: Use an event wait and timer as competing triggers, reject an input with a condition, and exercise both kinds of same-state reaction.

**Acceptance Scenarios**:

1. **Given** distinct competing triggers, **when** one is first processed with an eligible transition, **then** it wins within the conflicting scope and the selection order is observable. External wall-clock occurrence order is not implied.
2. **Given** a trigger completes with a false condition and its source remains active, **when** rejection is resolved, **then** only that trigger restarts; the existing deadline and other active waits remain intact.
3. **Given** the rejected trigger is an event wait, **when** it restarts, **then** it awaits a new event rather than replaying the rejected input.
4. **Given** a parent fallback or another selected transition exits the source, **when** rejection is resolved, **then** the rejected trigger is not restarted in that obsolete scope.
5. **Given** an authored action-without-exit reaction, **when** it runs, **then** the existing state visit, deadline and independent waits are preserved.
6. **Given** deliberate self-re-entry, **when** it runs, **then** exit/entry work occurs and a fresh state visit is created.
7. **Given** trigger work already performed an external effect, **when** another transition cancels it or rejection restarts it, **then** cancellation is not reported as undoing that effect. Restarting a composite can execute its work again.
8. **Given** one input makes several transitions on the same state eligible, **when** selection occurs, **then** the first eligible transition in the explicit authored order is selected; moving graph elements alone does not alter that order.
9. **Given** an authored automatic transition with no trigger, **when** its source state's entry work finishes, **then** its condition can be evaluated and an eligible automatic path can proceed without an artificial event. It is not evaluated before pending entry work finishes; complete automatic-candidate arbitration, reevaluation and protection remain Q1.
10. **Given** eligible automatic transitions and an already queued business event, **when** the machine progresses, **then** it runs eligible automatic transitions until none remain before evaluating that queued event against the resulting configuration. Automatic cycles cannot run indefinitely; the protection contract remains Q1.

### User Story 6 — Inspect, diagnose and operate the actual machine (Priority: P1)

Authors and operators understand the definition and execution they actually use, including hierarchy, regions, pending phases, event outcomes and failures.

**Why this priority**: A usable editor and honest causal evidence are acceptance requirements, not a substitute for execution.

**Independent Test**: Author, save/reload, publish, run and inspect a representative hierarchical/parallel machine in Studio, including a deliberately failing or unavailable dependency.

**Acceptance Scenarios**:

1. **Given** a running machine, **when** the operator inspects it, **then** the view identifies its exact published definition and active configuration, pending phase/work, selected transition and relevant input/outcome.
2. **Given** a malformed condition, unavailable activity or execution failure, **when** the author/operator encounters it, **then** the interface distinguishes it from a false condition or an unhandled event and supplies actionable context.
3. **Given** a read-only or narrow-width view, **when** the user navigates states, regions and activities, **then** the agreed inspection and non-canvas operations remain usable and mutation availability is clear.
4. **Given** an existing instance pinned to a published version, **when** a later version is edited/published, **then** that instance follows the existing platform version/pin contract rather than silently adopting the edited machine.
5. **Given** a state/transition/region chart with summaries of its entry, exit, trigger and action work, **when** the author opens ordinary activity work and returns, **then** the normal activity editor and breadcrumbs support the round trip and the chart context is preserved.
6. **Given** a selected transition, **when** the author configures it, **then** a structured inspector presents Trigger (or Automatic), Condition, Action and Destination together with explicit transition order and state-visit behavior. Activity work opens in the normal editor and conditions use Elsa's expression editor.

### User Story 7 — Use machine-wide and state-visit data (Priority: P1)

An author declares values that span the machine's state changes and values local to one state visit, using the existing Elsa value/binding mechanisms.

**Why this priority**: Re-entry must have predictable data lifetime without creating a separate variable authority.

**Independent Test**: Declare values at both scopes, change states and deliberately re-enter a state; inspect the values through supported ordinary activities/bindings.

**Acceptance Scenarios**:

1. **Given** a machine-wide value and a state-local value, **when** the state exits and is entered again, **then** the machine-wide value remains in its machine lifetime and the state-local value starts fresh for the new visit under normal initialization/binding rules.
2. **Given** a parent state remains active while its child changes, **when** that child transition occurs, **then** the parent-state local scope follows the continuing parent visit rather than being reset as a new parent visit.
3. **Given** any scoped value, **when** persistence, visibility, secrets or parallel writes are involved, **then** existing platform contracts continue to govern; this capability does not silently add competing policies.

### Edge Cases

- Simultaneous candidates in an exclusive scope versus compatible candidates in independent regions.
- An eligible child, an ineligible child and an eligible ancestor responding to the same input.
- A region-local reaction versus an exit whose boundary includes a shared parent.
- New events arriving during a durably pending action, including restart before action completion.
- Input admission at a configured capacity boundary, races for the last available capacity, and unavailable durable storage.
- Duplicate/conflicting input identity, unhandled evaluation, terminal execution and retained outcome visibility; Q2 must settle the concrete contract.
- Old timer/task completions after cancellation, self-re-entry, parent exit, retry or another state visit.
- Rejected composite triggers whose work can run again; immediately completing triggers that could loop.
- Action-without-exit work that suspends while existing state waits remain live.
- Faults at entry, exit, action, guard evaluation and trigger rearming; recovery must not be assumed equivalent to replaying all earlier external work.
- Invalid state targets, missing/ambiguous initial configuration, unreachable states, automatic cycles, final-state configuration and unavailable definition content; Q1/Q3 remain open.

## Requirements

### Accepted product requirements

- **FR-001**: The capability MUST remain generic. Approval, document, reviewer, AddComment and RestartReview concepts belong in user definitions and examples.
- **FR-002**: Authors MUST use normal workflow/activity definitions, versioning, publication and reusable-activity mechanisms; the initial release MUST NOT introduce a separately managed StateMachine definition/publication domain.
- **FR-003**: StateMachine MUST support both root and nested activity use and provide its own integrated Studio designer.
- **FR-004**: The first release MUST support both hierarchical states and parallel regions, delivered hierarchy first.
- **FR-005**: Entry, exit, transition action and trigger slots MUST support ordinary Elsa activities and composites. The capability MUST allow entry/exit/action work to suspend durably and MUST expose its pending phase.
- **FR-006**: A completed trigger MUST make its transition a candidate for condition/selection evaluation; completion alone MUST NOT bypass eligibility rules.
- **FR-007**: An eligible child transition MUST take precedence over a matching ancestor transition; absent an eligible child, an eligible parent fallback MUST remain available.
- **FR-008**: For distinct competing triggers, the first eligible trigger processed by the engine MUST win within the conflicting scope, with observable processing/selection order.
- **FR-009**: One logical event MUST be able to select compatible transitions in multiple active parallel regions.
- **FR-010**: Region-local transitions MUST preserve independent waits in other regions. Exiting a containing state MUST cancel the relevant exited descendant work under the agreed lifecycle boundary.
- **FR-011**: A parallel state MUST become complete when every region reaches a final state. Early exit MUST be an explicit authored transition, without implicit any/threshold completion.
- **FR-012**: An action-without-exit reaction MUST preserve the current state visit and its waits/deadline. Deliberate self-re-entry MUST run exit/entry work and create a fresh visit.
- **FR-013**: State-owned waits MUST follow their activation lifetime. Re-entry starts a fresh visit; changing a child while its parent remains active MUST NOT by itself reset parent-owned waits.
- **FR-014**: A completed trigger rejected by a false condition MUST restart only if its source remains active, without resetting existing deadlines or other active waits. An event wait MUST await a new event rather than replaying the rejected one.
- **FR-015**: The machine MUST finish the selected transition's work before evaluating later business events for that machine, including while selected work is durably suspended. Those business events MUST NOT pre-empt the selected work; workflow cancellation and operator recovery remain separate.
- **FR-016**: New business events addressed to a running machine MUST be durably accepted for later evaluation under an explicit bounded-capacity and delivery contract.
- **FR-017**: Event admission and successful handling MUST be distinct observable outcomes.
- **FR-018**: An event with no eligible transition when evaluated MUST finish with an observable unhandled outcome. Subsequent state changes MUST NOT revive it. Explicit event deferral has no implied first-release commitment.
- **FR-019**: Completions tied to cancelled/obsolete timer, task or activity activations MUST NOT be redirected to a later visit.
- **FR-020**: Cancellation/rearming MUST NOT be represented as compensating already-performed external effects. Composite trigger rearming can repeat its work.
- **FR-021**: Actual Studio authoring-to-runtime execution, durable wait/resume, restart/race/failure qualification and resulting-main evidence MUST establish first-release completion; planning artifacts or in-memory tests alone are insufficient.
- **FR-028**: When one input makes several transitions on the same state eligible, the machine MUST select the first eligible transition in an explicit author-visible order. Graph geometry or visual rearrangement MUST NOT silently determine or alter that priority. This does not change child/ancestor precedence or compatible regional selection.
- **FR-029**: Authors MUST be able to define automatic, condition-based transitions without a trigger. The machine MUST evaluate them after their source state's entry work completes. Complete automatic-candidate arbitration, reevaluation and loop protection MUST be explicit in the agreed execution contract.
- **FR-030**: The machine MUST run eligible automatic transitions until none remain before evaluating queued business events. Automatic cycles MUST be bounded by explicit loop protection so they cannot run indefinitely; this requirement does not choose an unagreed protection or recovery mechanism.
- **FR-031**: Completion MUST use explicit authored final-state intent. A non-final state MUST NOT be inferred complete merely because it lacks a local outgoing edge or currently eligible transition. The capability MUST distinguish waiting from completion and provide diagnostics for accidental dead ends; complete scoped completion propagation remains Q1.
- **FR-032**: Authors MUST be able to use machine-wide values spanning state changes and state-local values whose lifetime is one state visit and which start fresh on re-entry. Existing Elsa value, binding, durability, secret and parallel-write contracts MUST remain authoritative; this scope choice MUST NOT silently replace those policies.
- **FR-033**: After selection, applicable authored phases MUST run in the order source Exit → transition Action → target Entry. A later applicable phase MUST NOT start until the previous one completes, including across durable waits. Complete hierarchical boundaries and fault/recovery behavior remain to be agreed.
- **FR-034**: Recovery of failed transition work MUST use the normal Elsa activity recovery boundary and preserve completed earlier phases. StateMachine MUST NOT automatically rewind the whole transition. Retry retains existing platform/external-effect guarantees and MUST NOT be represented as exactly-once external execution or implicit compensation.
- **FR-035**: Studio MUST present a focused state/transition/region chart with summaries of entry, exit, trigger and action work. Authors MUST be able to drill into that ordinary work using the normal activity editor, with breadcrumbs back to the chart and chart context preserved on return. Detailed navigation and accessibility/responsive journeys remain Q3.
- **FR-036**: Selecting a transition MUST expose a structured inspector for Trigger (or Automatic), Condition, Action and Destination, explicit transition order, and whether the reaction preserves the state visit or exits/re-enters. Ordinary activity work MUST use the normal activity editor and conditions MUST use Elsa's expression editor. The inspector MUST represent the supported transition semantics; detailed valid choices and diagnostics remain Q3.
- **FR-037**: The machine, each compound state with sequential child-state selection, and each parallel region MUST have an explicit initial child state. Studio MUST validate missing or invalid selections. Entering a parallel state MUST activate every region at its own explicit initial state. Graph geometry MUST NOT determine startup behavior.
- **FR-038**: For states a transition actually exits, child Exit work MUST finish before its exited parent runs Exit. For states it actually enters, parent Entry work MUST finish before its entered children run Entry. All applicable exits MUST complete before transition Action, and Action MUST complete before entries, including across durable waits. This requirement does not select an unagreed boundary calculation or independent-region ordering/concurrency policy.

### Platform constraints and design obligations

These obligations apply existing repository/platform rules and the original delivery request. They do not resolve Q1–Q3 or assert that current seams already satisfy them.

- **FR-022**: Definition ownership, permissions, activity/expression composition, publication, exact executable/version pins and deployment shapes MUST follow the existing platform contracts. Changes governed by an actual initiative MUST follow that initiative's agreement rules.
- **FR-023**: StateMachine MUST reuse existing runtime scheduling, delivery, identity, correlation and persistence authorities. Its semantic state may be owned by the capability; it MUST NOT silently add competing authorities or a new authored execution domain.
- **FR-024**: Studio MUST support authoring and inspection of hierarchy, regions, ordinary activity slots and conditions, with clear loading/error/unavailable/read-only states. Supported normal work MUST have a semantic editing path; the detailed graph/outline/navigation contract is Q3.
- **FR-025**: Validation, keyboard/non-canvas use, narrow-width behavior and preservation/repair of unsupported content MUST have concrete acceptance scenarios under Q3. Unknown content MUST NOT be silently reinterpreted as known behavior.
- **FR-026**: Guard evaluation errors, runtime faults, stale completions, delivery failures, admission rejection and unhandled events MUST have distinguishable outcomes under Q2; this draft does not assign an invented recovery authority.
- **FR-027**: The accepted behavior MUST have risk-based qualification at the cheapest sufficient levels, plus actual native/Studio and durable-host witnesses for integration claims. Existing hosts and stores MUST be preserved when running qualification.

### Key entities

These are conceptual specification terms, not approved public types or a committed schema. Canonical platform meanings remain in the [Elsa glossary](../../docs/glossary/elsa.md).

- **Authored machine**: the state/transition/region structure and activity/condition configuration inside an ordinary workflow/activity definition.
- **Active configuration**: the states, ancestors and regional positions currently active in one machine invocation.
- **State visit**: one activation lifetime that distinguishes its waits from those of prior visits.
- **Transition candidate/selection**: an eligible authored reaction and the recorded choice or compatible set selected for processing.
- **Pending phase**: durably incomplete entry, exit or action work visible to the user.
- **Machine-addressed business event**: an input admitted for later chart evaluation, distinct from a completion tied to one exact activity/wait activation.
- **Event outcome**: admission/evaluation evidence, including unhandled behavior; concrete identity, ordering and retention are Q2.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A user can author, save, reload, publish, run and inspect both a root machine and a nested machine through the supported Studio experience without editing raw serialization for ordinary supported work.
- **SC-002**: The accepted hierarchy, compatible parallel reactions, regional/parent cancellation and all-region finality cases in the [acceptance matrix](acceptance-matrix.md) pass with actual definition/version and execution evidence.
- **SC-003**: A durably pending action and an admitted event survive a real host restart; after recovery, selected work settles before later event evaluation, and obsolete completions cannot affect a new visit.
- **SC-004**: Once Q2 is agreed, qualification at configured capacity C proves the declared bound and admission response under both ordinary and racing delivery. No acknowledged admission disappears because work was pending or the host restarted.
- **SC-005**: Guard rejection preserves independent waits/deadlines, while action-without-exit and deliberate self-re-entry exhibit their distinct activation behavior.
- **SC-006**: An unhandled event remains final even if a later state would have handled a newly sent event of the same kind.
- **SC-007**: An operator can distinguish admission, selection/handling, pending phases and failure/unhandled/stale outcomes for the exact published version. Q3 qualification covers keyboard/non-canvas, read-only and narrow-width journeys.
- **SC-008**: Every accepted owner decision has explicit requirement/scenario coverage, reviewed implementation evidence and the appropriate resulting-main qualification before Program #3815 is declared complete.

## Assumptions and boundaries

- The existing workflow/activity definition and publication lifecycle is the accepted ownership model (OD16); internal execution placement remains unchosen.
- The completed Elsa 3.8 program, WF4 and SCXML are references, not automatic Elsa 4 compatibility requirements. Departures must be justified in M0.
- Scope does not imply full SCXML interchange/conformance, history states, shared-trigger object identity, Elsa 3 import/migration, a new saga engine or compensation of external effects.
- Existing native compensation/saga work retains its ownership. Any integration proposal identifies the actual contract and evidence needed rather than merging programs by terminology.
- No arbitrary throughput/latency target, wall-clock CI gate, release or deployment is introduced by this draft.
- Complete scoped completion, hierarchical phase/transition ordering, delivery/recovery and Studio contracts remain provisional until Q1–Q3 are resolved. Production implementation must wait for the applicable approved design/spec artifacts.

## Open decisions and proposed defaults

The following are review prompts, not accepted semantics. Interview the owner one consequential choice at a time; technical choices also require source-grounded analysis.

### Q1 — Complete statechart execution contract

[NEEDS CLARIFICATION: Confirm or amend the remaining scoped completion, complete hierarchical entry/exit/action, cross-state/region and automatic-candidate conflict, reevaluation and rearming/loop rules before declaring the behavior contract approved.]

OD17 settles same-state selection order for one input (FR-028). Complete conflict arbitration across different states/regions remains open.

OD18 adds automatic transitions after entry work completes (FR-029). OD19 gives eligible automatic progress precedence over queued business events (FR-030). Complete automatic-candidate arbitration, reevaluation and cycle/protection/recovery rules still need agreement.

OD20 requires explicit final-state intent (FR-031). OD26 requires explicit, validated initial child states at each applicable level and starts all regions when entering a parallel state (FR-037). Final entry work and complete scoped completion propagation remain open.

OD22 settles source Exit → transition Action → target Entry for applicable authored phases (FR-033), with durable completion before the next phase starts. OD27 adds child-first Exit and parent-first Entry for states actually exited/entered (FR-038). Complete exit/entry boundary calculation, independent-region ordering/concurrency and fault/recovery details remain open.

Proposed reference baseline: a persistent authored reaction remains usable after an action-without-exit completes. This proposal has not been accepted merely because a reference engine uses it.

### Q2 — Durable admission, event outcomes and recovery

[NEEDS CLARIFICATION: Agree the implementable bounded event identity/order/admission/outcome and fault/retry/recovery contract, with a source-grounded witness of the existing runtime integration.]

Specify configured bounds and overflow/unavailable behavior; deduplication/conflicting identity; receipts and retention; terminal/retired execution behavior; input disposition around faults and restart; and recovery of partial transition work with honest external-effect uncertainty. Do not equate accepted admission with exactly-once external effects. Demonstrate current sufficient seams or propose a reviewed bounded extension before freezing placement.

OD23 requires recovery at the normal failed-activity boundary with completed earlier phases preserved (FR-034). Complete propagation, blocked-input handling, exact supported recovery paths and source-grounded integration proof remain open.

### Q3 — Studio contract and remaining capability boundaries

[NEEDS CLARIFICATION: Agree the concrete graph/outline/navigation, slots/conditions, diagnostics/inspection and accessibility/responsive journeys; classify remaining advanced capabilities and data/outcome/reuse details without silently expanding first-release scope.]

Remaining proposed authoring/inspection baseline: a non-canvas navigation path; clear current configuration, pending phases and causal outcomes; exact-version inspection. These details still need agreement alongside the accepted chart/editor choices below. History destinations, full interchange, migration and shared-trigger identity remain uncommitted. Existing variable, expression and outcome contracts retain their authority; this program cannot approve pending core architecture decisions.

OD24 settles the focused chart, activity summaries and drill-in normal activity editing with breadcrumbs and preserved chart context (FR-035). Detailed hierarchy/region navigation, accessibility and responsive journeys remain open.

OD25 settles structured transition configuration and the normal activity/expression editors (FR-036). Exact valid combinations, diagnostics and the keyboard/read-only/responsive journeys remain open; the inspector choice does not expand transition semantics.

OD21 chooses machine-wide and state-visit-local value scopes (FR-032). Their concrete authoring/visibility/binding integration and outcome details remain to be specified against the existing platform contracts.
