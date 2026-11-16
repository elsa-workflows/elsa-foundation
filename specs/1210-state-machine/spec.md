# Feature Specification: Elsa 4 StateMachine

**Feature Branch**: agent/codex/3820-statemachine-spec-01

**Created**: 2026-11-04

**Status**: Draft — provisional; open decisions below prevent specification approval and production implementation.

**Input**: Provide generic hierarchical and parallel state machines in Elsa 4, authored through normal workflow/activity definitions, with root and nested composition, a dedicated Studio designer, ordinary activities, durable events and understandable execution.

**Program**: [#3815](https://github.com/elsa-workflows/elsa-foundation/issues/3815). **M0 feature**: [#3820](https://github.com/elsa-workflows/elsa-foundation/issues/3820). **Goal**: [StateMachine](../../docs/program-goals/state-machine.md).

## Current delivery context — 12 November 2026

The 49 owner decisions remain the product requirements. This slice recovers their specification from
[closed PR #3840](https://github.com/elsa-workflows/elsa-foundation/pull/3840), which was closed unmerged for review size.
It introduces no execution capability and does not complete M0. The [delivery order](delivery.md) replaces the
large-PR publication plan.

The [12 November kernel direction](https://github.com/elsa-workflows/elsa-foundation/issues/4175#issuecomment-6823051291)
changes the architecture against which M0 must be qualified. The prior C2 and admission documents are historical
technical proposals; their current-core APIs, schema versions and provider wiring are not approved integration
requirements for the new kernel. Keep the input, identity, lifetime and recovery guarantees while reassessing the
mechanism. The kernel model is not shipped product code. This StateMachine slice does not approve that model or
change its owner's approval gate.

The technical history below records the decisions and evidence available on 6 November. Use the
[current status](README.md) when deciding what has landed or can be implemented next.

## Decision and approval boundary

The program's [OD1–OD49 register](https://github.com/elsa-workflows/elsa-foundation/issues/3815#accepted-owner-decisions-4-november-2026) records accepted owner direction. This draft translates it into user journeys and verifiable requirements. It does not approve an internal interpreter, schema, event-admission implementation or technical plan.

The requirements identified as accepted direction below are grounded in that register. Platform constraints reuse existing governance. The proposed completion rules and unresolved contracts have not been accepted; they remain visible in Q1–Q3.

Delegated engineering direction DE1 permits the reviewed M0 proposal to be used provisionally for a bounded native-first witness, with model hosting as fallback. Later supervisory authority covers necessary owner decisions and progress through the normal gates without waiting for Sipke's return. Record that provenance separately from OD1–OD49 and never infer another designated approver's agreement. This does not establish runtime sufficiency, final engine placement or a public schema; the [focused witness plan](m0-witness-plan.md) is design qualification work.

The [5 November delegated principles decision](https://github.com/elsa-workflows/elsa-foundation/issues/3815#issuecomment-6669211076) agrees the input/occurrence and owner-lifetime principles in the exact `036a88bf` proposal: preserve independent common-publication arbitration, distinguish readiness from retained provenance and close only exiting owners. It does not approve the actual fence/control/admission mechanism, native placement, Q1–Q3 or production plan. The [principles record](m0-input-profile-proposal.md) and its remaining gates are part of M0; no other person's agreement or draft Model X ratification is inferred.

The later [A1 policy decision](https://github.com/elsa-workflows/elsa-foundation/issues/3815#issuecomment-6670586138) agrees its explicitly listed admission/recovery behavior while leaving representation/defaults/mechanism and Q2 qualification open. The [direct full-delivery instruction](https://github.com/elsa-workflows/elsa-foundation/issues/3815#issuecomment-6670672472) authorizes resolving the remaining architecture and implementation through normal review and evidence gates. The implementing agent selected [C2](m0-runtime-mapping.md) as the engineering basis for the [reviewed M0 runtime-integration work unit](m0-runtime-extension-plan.md) after independent and source-owner co-review. Its R1 code is internal accounting/tests, not a StateMachine activity or physical capability proof. The full production plan/tasks and final native placement remain pending actual integration qualification; no other person's agreement or provisional-constitution ratification is inferred.

Hierarchy and parallel regions both belong to first-release acceptance. Delivery priority and independently testable stories do not reduce that scope. The authorized M0 integration work does not substitute for the complete production specification, plan, implementation and acceptance.

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
5. **Given** machine-level final states have explicit mappings to declared normal Elsa activity outcomes, **when** successful machine completion occurs, **then** the mapped outcome permits the enclosing workflow/Flowchart to branch under normal activity contracts. State names do not implicitly create outcome names, nested completion remains local, and data outputs follow normal Elsa rules.
6. **Given** the M0-agreed saga-style authored topology, **when** the lifecycle is authored in Studio and executed with existing explicit native compensation/recovery activities through a real durable run/restart, **then** the declared journal lifetime, fault propagation, uncertain-effect handling and reusable-activity boundaries are demonstrated. StateMachine and native compensation retain separate responsibilities; the example introduces no Saga type/engine and does not turn transitions or cancellation into implicit undo.

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
8. **Given** a compound state reaches its final child, **when** an authored completion-triggered transition is eligible, **then** that transition can define what follows the nested lifecycle. Nested completion does not by itself complete the whole machine; reaching its own top-level final state completes the StateMachine activity. Final-entry timing follows FR-042 and automatic/completion precedence follows FR-043; remaining completion conflicts and identity/representation and durable consumption remain Q1.
9. **Given** a transition defined on a compound parent with one active child targets a descendant, **when** the author chooses its parent-visit behavior, **then** preserving the visit changes the child without resetting parent-owned waits/deadlines, while deliberate re-entry runs parent exit/entry work and starts a fresh parent visit. The choice survives normal definition editing/publication and follows accepted phase, hierarchy and recovery rules.
10. **Given** an authored exit followed by re-entry, **when** the state is entered again, **then** explicit targets and initial-state rules determine its configuration without an initial-release history destination. A durable restart of an existing active invocation instead restores its current configuration and waits; hierarchy, parallel regions and explicit parent-to-descendant modes remain supported.
11. **Given** a compound or parallel state completes and no completion-triggered transition is eligible, **when** that completion opportunity is evaluated, **then** it is consumed for this visit and later value changes do not revive it. A new state visit can produce a new completion; ongoing rules use automatic conditions or new triggers. Condition errors remain distinct from false/ineligible evaluation under the fault/recovery contract.

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
8. **Given** one input selects compatible transitions in independent regions, **when** one region's phase work waits durably, **then** independent work in the same phase can progress in other regions without a guaranteed branch completion order. Hierarchy dependencies remain enforced; all selected exits finish before actions, and all actions finish before entries. Later business events wait until the selected work finishes.
9. **Given** a regional Action fails and normal Elsa fault handling leaves the machine awaiting recovery, **when** supported recovery retries that failed work, **then** completed sibling work is preserved and successful regions are not restarted. StateMachine adds no automatic sibling cancellation; still-running work follows normal incident policy. The phase and later chart-event evaluation remain blocked until required work succeeds, unless normal workflow cancellation ends the work.
10. **Given** every region of a parallel state is final, **when** an authored completion-triggered transition is eligible, **then** that transition can define what follows the completed parallel lifecycle without implicit whole-machine completion.
11. **Given** an attempted direct transition between states in independent regions of the same parallel container, **when** the definition is authored or validated, **then** the initial release rejects that edge with a clear diagnostic rather than silently treating it as shared-container exit/re-entry. Region-local transitions, compatible shared-event reactions and explicit exits from the parallel container remain supported.
12. **Given** one input enables conflicting transitions sourced in unrelated regions, such as a local Editorial transition and a Legal transition exiting the whole parallel container, **when** precedence is determined, **then** an explicit author-visible order chooses between the conflicting candidates. Child-over-parent precedence and compatible regional selection remain in force; container-exit candidates receive no implicit preference and moving shapes does not change priority.
13. **Given** a transition defined on a parallel parent targets a descendant, **when** that transition is authored and taken in the initial release, **then** it explicitly exits/re-enters the whole parent and all active regions, starts a fresh parent visit, follows the target in its region and starts other regions at their initial states. Parent-preserving mode for this shape is unsupported and diagnosed rather than silently reset; region-local transitions remain available when other regions should stay intact.

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
9. **Given** a final state has ordinary entry work that waits durably or fails, **when** its entry is pending or awaits normal recovery, **then** that state does not yet count as complete and cannot cause enclosing-state or whole-machine completion. Completion becomes possible only after the entry work succeeds, including across restart.
10. **Given** a machine's configured event capacity is full, **when** a new admission is attempted, **then** it is rejected immediately with a clear capacity result, without waiting for capacity or evicting accepted events. The rejected attempt is not reported as accepted and the sender decides when to retry.
11. **Given** an event was accepted and its sender lost the response, **when** the sender retries the same event ID and contents within the declared retention window for that machine invocation, **then** the existing receipt/current outcome is returned without another queue entry. Reusing the retained ID with different contents is rejected as a conflict; identical IDs in different machine invocations have distinct scopes.
12. **Given** several machine-addressed business events have been durably accepted, **when** queued events are evaluated after internal progress settles, **then** they follow durable admission order for that machine invocation, including after waits/restart. Concurrent admissions have an observable engine-assigned order; duplicate retries do not change an event's original position. Timer/activity-trigger competition remains governed by its accepted rules.
13. **Given** accepted business events remain queued and have not been selected, **when** the machine completes or is cancelled, **then** those events receive final not-processed outcomes with the explicit completion/cancellation reason and their receipts remain available under the declared retention policy. New admissions to that invocation are rejected and queued events do not automatically move to another invocation. These outcomes are distinct from evaluated-unhandled or already-selected work.
14. **Given** a machine remains active but awaits recovery after failed action work or detected excessive internal progress, **when** a new business event is admitted, **then** the same bounded durable acceptance, ordering and deduplication rules apply and the recovery blockage is visible. Events stay queued until recovery permits chart evaluation; admission cannot bypass recovery or automatically repeat a detected cycle. Full and ended invocations still reject under their agreed rules.

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
11. **Given** an eligible automatic path, a ready nested-state completion and queued business events, **when** the machine progresses, **then** automatic transitions are exhausted before still-applicable completion reactions. Automatic paths are checked again after each reaction; queued business events are evaluated only after this internal progress settles. Loop protection applies.
12. **Given** loop protection detects excessive automatic/completion progress, **when** protection takes effect, **then** further internal progress stops, committed progress is preserved and an actionable incident is visible through normal Elsa operations. Recovery is required; the detected cycle does not automatically repeat. The concrete limit, detection boundary and supported recovery actions remain technical design work.
13. **Given** a trigger-based action-without-exit reaction succeeds and its source visit remains active, **when** that reaction becomes available again, **then** only its own trigger restarts and other waits/deadlines remain intact. Event waits listen for new input without replaying the consumed event; composite trigger work can repeat. The trigger does not re-arm before action success or after the source visit ends.

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
7. **Given** nested states and parallel regions, **when** the author navigates their structure, **then** expandable/collapsible chart containers and a synchronized hierarchy outline support keyboard and non-canvas navigation while retaining parent context. Ordinary activity work still opens in its normal editor; navigation does not change initial selections or transition priority.
8. **Given** a root or nested machine execution, **when** the user inspects it through normal workflow execution inspection, **then** the state/region chart and outline, active configuration, pending phases and linked event/transition history connect to ordinary activity details, receipts/outcomes and normal incident/recovery tools against the exact executed version. Evidence remains connected to the surrounding workflow and activities under existing permissions.

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
- **FR-038**: For states a transition actually exits, child Exit work MUST finish before its exited parent runs Exit. For states it actually enters, parent Entry work MUST finish before its entered children run Entry. All applicable exits MUST complete before transition Action, and Action MUST complete before entries, including across durable waits. Complete boundary calculation remains Q1; independent regional work follows FR-039.
- **FR-039**: Compatible selected work in independent parallel regions MUST be allowed to progress concurrently within each phase, so a durable wait in one region MUST NOT by itself block independent same-phase work in another. Hierarchy dependencies MUST be preserved. All selected exits MUST finish before selected actions begin, and all selected actions MUST finish before entries begin. Independent branch completion order is not guaranteed; later business events MUST wait for the selected work to finish. FR-040 governs sibling preservation during regional Action recovery; other partial-failure paths remain Q2 and existing shared-value policies remain authoritative.
- **FR-040**: When a regional Action fails and normal Elsa fault handling leaves the machine awaiting recovery, StateMachine MUST preserve completed sibling work and MUST NOT add automatic sibling cancellation or restart successful regions. Still-running work MUST follow normal Elsa incident policy. The phase MUST NOT advance and later chart events MUST NOT be evaluated until required work succeeds. Normal workflow cancellation, supported recovery eligibility and external-effect guarantees remain authoritative; other fault paths and exact integration remain Q2.
- **FR-041**: Authors MUST be able to define completion-triggered transitions for a compound state reaching its final child and for a parallel state with every region final. Nested completion MUST remain local and MUST NOT by itself complete the whole machine. Reaching the machine's own top-level final state MUST complete the StateMachine activity. Final-entry timing follows FR-042 and automatic/completion precedence follows FR-043; remaining completion conflicts and exact identity/representation and durable consumption remain Q1; no external delivery authority is chosen by this requirement.
- **FR-042**: Final states MUST support ordinary entry work, including durable waits. A final state MUST count as complete only after its entry work succeeds. Pending waits or normal recovery MUST NOT cause premature region, enclosing-state or whole-machine completion. Existing Elsa waiting, fault and recovery behavior MUST apply, including across restart.
- **FR-043**: StateMachine MUST exhaust eligible automatic transitions before processing still-applicable completion reactions and MUST check automatic transitions again after each reaction. Queued business events MUST be evaluated only after that internal progress settles. Loop protection MUST apply. This rule MUST preserve accepted eligibility, hierarchy, explicit same-state transition order and compatible regional behavior; it does not change unrelated workflow scheduling.
- **FR-044**: When loop protection detects excessive automatic/completion progress, StateMachine MUST stop further internal progress, preserve committed progress and report an actionable incident through normal Elsa operations. Recovery MUST be required and the detected cycle MUST NOT automatically repeat. The technical design MUST specify the limit, detection scope/lifetime, diagnostics and supported recovery actions using existing runtime authorities; this requirement does not choose a numeric default or a new incident/recovery authority.
- **FR-045**: When configured machine-event capacity is full, new admission attempts MUST be rejected immediately with a clear capacity result. Admission MUST NOT wait for capacity or evict previously accepted events. A rejected attempt MUST NOT be reported as accepted; the sender decides when to retry. Exact capacity accounting and atomic concurrent admission remain Q2 design/proof work.
- **FR-046**: Accepted machine-addressed business events MUST support admission deduplication by a sender-provided event ID scoped to the target machine invocation. Within a declared retention window, the same ID and contents MUST return the existing receipt/current outcome without another queue entry, and the same ID with different contents MUST be rejected as a conflict. The technical design MUST define content comparison, retention boundaries, concurrent retry behavior and existing-runtime integration. Admission deduplication MUST NOT be represented as exactly-once activity/external effects; expiry and remaining terminal/retired details remain Q2, with FR-048 governing queued unselected events when the invocation ends.
- **FR-047**: Queued machine-addressed business events MUST be evaluated in durable admission order within each machine invocation, preserving that order across waits and restarts. Concurrent admissions MUST receive an observable engine-assigned order; duplicate retries MUST retain the original event's position. Accepted automatic/completion precedence and timer/activity-trigger competition rules MUST remain in force. This requirement does not imply external wall-clock ordering or select a separate ordering authority; atomic source integration remains Q2.
- **FR-048**: When a machine completes or is cancelled, accepted business events still queued and not yet selected MUST receive final not-processed outcomes with the explicit completion or cancellation reason. Receipts MUST be retained under the declared retention policy. New admissions to that invocation MUST be rejected and those events MUST NOT automatically move to another invocation. These outcomes MUST remain distinct from evaluated-unhandled and already-selected/partly executed work. Exact terminal/admission atomicity, retention details and in-flight cancellation outcomes remain Q2 design/proof work.
- **FR-049**: An active machine blocked awaiting recovery MUST continue durably accepting business events under the same configured capacity rules and MUST expose the recovery blockage. Accepted events MUST remain queued and chart evaluation MUST NOT resume until recovery permits it. Full/terminal rejection, retained receipts/deduplication and durable admission order MUST remain as agreed. New admissions MUST NOT bypass recovery or automatically repeat a detected cycle; exact recovery-gate/admission integration and atomic races remain Q2 design/proof work.
- **FR-050**: The initial release MUST disallow direct transition edges between states in independent regions of a shared parallel container. Definition validation and Studio MUST diagnose these edges clearly and MUST NOT silently reinterpret them as shared-container exit/re-entry. Region-local transitions, compatible shared-event regional reactions and explicit exits from the containing parallel state MUST remain supported. This restriction does not reduce the agreed hierarchy/parallel first-release scope or select unagreed exit/entry boundaries and conflict arbitration.
- **FR-051**: For one input enabling conflicting transition candidates sourced in unrelated parallel regions, precedence MUST use an explicit author-visible order. Accepted child-over-parent precedence and compatible regional selection MUST be preserved; diagram geometry MUST NOT determine priority and parallel-container exits MUST NOT receive implicit preference. Formal compatible-set selection, exact boundaries and losing-trigger disposition remain Q1 design/proof work.
- **FR-052**: For a transition defined on a compound parent with one active child and targeting its descendant, authors MUST be able to explicitly choose either to preserve the parent visit while changing the child, retaining parent-owned waits/deadlines, or to exit/re-enter the parent, running its exit/entry work and starting a fresh parent visit. Accepted phase, hierarchy, state-visit and recovery rules MUST apply. This requirement does not permit direct cross-region edges; parallel-parent targeting follows FR-060. Complete boundary calculation, validation, schema/defaults and source/Studio integration remain open.
- **FR-053**: A trigger-based action-without-exit reaction MUST re-arm only its own trigger after its action succeeds, provided the same source visit remains active. It MUST NOT re-arm before action success or after that visit ends. Other waits/deadlines MUST remain intact and an event wait MUST listen for new input without replaying the consumed event. Repeated composite trigger work retains existing external-effect guarantees. Automatic paths without an activity trigger retain their separate rules; exact scheduling/recovery integration and immediately completing trigger-loop protection remain open.
- **FR-054**: Studio MUST show nested states and parallel regions as expandable/collapsible chart containers paired with a synchronized hierarchy outline for keyboard and non-canvas navigation. Authors MUST be able to retain parent context while collapsing detail, and ordinary activity work MUST retain the normal-editor navigation agreed in FR-035. Navigation and expand/collapse MUST NOT change execution semantics, including initial selections and transition priority. Detailed interactions, view-state persistence under existing owners and accessible/read-only/responsive journeys remain Q3.
- **FR-055**: Normal workflow execution inspection MUST integrate the StateMachine chart and outline, active configuration, pending phases and linked event/transition history with ordinary activity details, receipts/outcomes and normal incident/recovery tools. This MUST support root and nested machines against the exact executed version, connected to their surrounding workflow/activities under existing identity, permission, inspection and recovery authorities. Accepted observable admission/selection order MUST use recorded causal evidence rather than timestamp ordering alone. Exact projection/integration, retention/availability and detailed Studio journeys remain open.
- **FR-056**: Machine-level final states MUST support explicit mappings to declared ordinary Elsa activity outcomes so the enclosing workflow/Flowchart can branch on StateMachine completion. State names MUST NOT implicitly become outcome names, and data outputs MUST follow normal Elsa rules. Existing outcome, binding, definition/publication and exact-version contracts, successful final-entry completion and local nested completion MUST remain authoritative. Mapping representation, validation, default/unmapped behavior and source/Studio integration remain open; this requirement MUST NOT silently approve pending core architecture decisions or introduce a new outcome authority.
- **FR-057**: History destinations MUST be deferred from the initial release. Authored re-entry MUST follow explicit targets and initial-state rules, preserving accepted parent-to-descendant visit choices. Normal durable restart MUST still restore the current active configuration and waits of the existing invocation. This boundary MUST NOT reduce hierarchy/parallel first-release acceptance or imply later history delivery; exact validation and qualification remain open.
- **FR-058**: Initial-release qualification MUST include at least one saga-style authored example combining StateMachine lifecycle modeling with existing explicit native compensation/recovery activities, demonstrated through Studio and a real durable run/restart. M0 MUST first settle its exact authored topology and journal-lifetime, fault-propagation, uncertain-effect and reusable-boundary constraints. Existing native compensation/recovery ownership and semantics MUST remain authoritative; the example MUST NOT introduce a Saga type/engine or make transitions/cancellation implicitly undo effects. This is an integration acceptance requirement, not current proof or a promise that every saga topology is supported.
- **FR-059**: A compound/parallel state's completion opportunity MUST be offered once per state visit and MUST be consumed when no completion-triggered transition is eligible. Later value changes MUST NOT revive that opportunity; a new visit may produce a new completion. Authors use automatic conditions or new triggers for ongoing rules. Accepted automatic/completion/business-event precedence and distinct condition-error handling MUST remain in force. Exact completion identity/representation and durable consumption/recovery integration remain open; this requirement does not introduce a business-event receipt authority.
- **FR-060**: Initially, a transition defined on a parallel parent and targeting a descendant MUST require explicit whole-parent re-entry: exit the parent and all active regions, start a fresh parent visit, follow the explicit target in its region and start other regions at their initial states. Parent-preserving mode for this shape MUST be diagnosed as unsupported rather than silently converted into re-entry. Region-local transitions and accepted compound-parent modes MUST remain available. Accepted hierarchy, phase, recovery and machine-invocation event contracts MUST remain in force; exact boundaries, validation and source/Studio integration remain open.

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
- History destinations are explicitly deferred from the initial release (OD46); this does not defer durable restart recovery, hierarchy or parallel regions.
- Scope does not imply full SCXML interchange/conformance, shared-trigger object identity, Elsa 3 import/migration, a new saga engine or implicit undo of external effects.
- Initial-release acceptance includes a qualified saga-style composition example using existing explicit native compensation/recovery mechanisms (OD47). Its topology and boundaries require M0 agreement; it does not create a new Saga type/engine.
- Existing native compensation/saga work retains its ownership. Any integration proposal identifies the actual contract and evidence needed rather than merging programs by terminology.
- No arbitrary throughput/latency target, wall-clock CI gate, release or deployment is introduced by this draft.
- Complete scoped completion, hierarchical phase/transition ordering, delivery/recovery and Studio contracts remain provisional until Q1–Q3 are resolved. Production implementation must wait for the applicable approved design/spec artifacts.

## Open decisions and proposed defaults

The following are review prompts, not accepted semantics. Interview the owner one consequential choice at a time; technical choices also require source-grounded analysis.

### Q1 — Complete statechart execution contract

[NEEDS CLARIFICATION: Confirm or amend the remaining scoped completion, complete hierarchical entry/exit/action, cross-state/region and automatic-candidate conflict, reevaluation and rearming/loop rules before declaring the behavior contract approved.]

The concrete engineering proposal for these remaining details is [research P2](research.md#p2--complete-selection-and-phase-rules). It is proposed for review, not accepted by reference or a new owner decision.

OD17 settles same-state selection order for one input (FR-028). OD40 requires explicit author-visible precedence between conflicting candidates sourced in unrelated regions, preserving child-over-parent precedence and compatible regional selection without implicit container-exit preference or diagram-layout priority (FR-051). Formal compatible-set selection and losing-trigger disposition remain open.

OD39 disallows direct cross-region transition edges in the initial release with clear definition/Studio validation, while retaining region-local transitions, compatible shared-event reactions and explicit parallel-container exits (FR-050). Allowed transition boundary calculation and formal compatible-set selection remain open; OD40 settles unrelated-region conflict precedence.

OD18 adds automatic transitions after entry work completes (FR-029). OD19 gives eligible automatic progress precedence over queued business events (FR-030). OD32 orders automatic progress before still-applicable completion reactions, rechecks automatic paths after each reaction and delays queued business events until this internal progress settles (FR-043). Complete automatic-candidate conflict arbitration, other reevaluation rules and cycle/protection/recovery rules still need agreement.

OD33 requires detected excessive automatic/completion progress to stop with committed progress preserved and an actionable incident in normal Elsa operations, requiring recovery without automatic repetition (FR-044). The concrete limit, detection scope/lifetime, diagnostics, supported recovery actions and integration proof remain open; no numeric default is accepted.

OD20 requires explicit final-state intent (FR-031). OD26 requires explicit, validated initial child states at each applicable level and starts all regions when entering a parallel state (FR-037). OD30 supports authored completion-triggered transitions for completed compound/parallel states, keeps nested completion local and completes the StateMachine at its own top-level final state (FR-041). OD31 allows ordinary final-state entry work and requires its success before completion can propagate (FR-042). OD32 settles automatic/completion precedence (FR-043); exact completion identity/representation and durable consumption and remaining cross-state/region conflicts remain open.

OD48 offers completion once per state visit, consuming the opportunity even when no completion-triggered transition is eligible, without revival after later value changes (FR-059). A new visit can complete again; ongoing rules use automatic conditions/new triggers. Exact completion identity/representation, durable consumption/recovery and source integration remain open.

OD22 settles source Exit → transition Action → target Entry for applicable authored phases (FR-033), with durable completion before the next phase starts. OD27 adds child-first Exit and parent-first Entry for states actually exited/entered (FR-038). OD28 allows independent regional work to progress concurrently within a phase, preserving hierarchy dependencies and selected-set phase barriers without a guaranteed branch completion order (FR-039). Complete exit/entry boundary calculation and fault/recovery details remain open.

OD41 explicitly supports parent-visit preservation or re-entry for a transition defined on a compound parent with one active child and targeting its descendant (FR-052). Parent waits/deadlines follow that choice. OD49 requires explicit whole-parent re-entry initially for the parallel-parent-to-descendant shape, following the target in its region and initial states in other regions (FR-060). Complete boundary calculation, validation and schema/defaults remain open; direct cross-region edges remain excluded.

OD42 keeps trigger-based action-without-exit reactions available by re-arming only their own trigger after successful action completion while the same visit remains active, preserving other waits/deadlines and requiring new event input (FR-053). Exact scheduling/recovery integration, losing-trigger disposition and immediately completing trigger-loop protection remain open. Automatic paths without an activity trigger retain their separate rules.

### Q2 — Durable admission, event outcomes and recovery

[NEEDS CLARIFICATION: Agree the implementable bounded event identity/order/admission/outcome and fault/retry/recovery contract, with a source-grounded witness of the existing runtime integration.]

The candidate comparison, admission/recovery policies and bounded witness are proposed in [research P1](research.md#p1--qualify-the-native-composite-first), [P3](research.md#p3--concrete-admission-and-recovery-contract-to-prove) and [P6](research.md#p6--bounded-integration-witness-and-decision-gate). They do not freeze placement or prove existing sufficiency.

Specify configured bounds and overflow/unavailable behavior; deduplication/conflicting identity; receipts and retention; terminal/retired execution behavior; input disposition around faults and restart; and recovery of partial transition work with honest external-effect uncertainty. Do not equate accepted admission with exactly-once external effects. Demonstrate current sufficient seams or propose a reviewed bounded extension before freezing placement.

OD34 requires immediate explicit capacity rejection without waiting or evicting accepted events (FR-045). OD35 deduplicates accepted events within a declared retention window by sender-provided ID scoped to the machine invocation, returns existing receipts/outcomes for identical retries and rejects conflicting contents (FR-046). Exact accounting, atomic concurrent admission/retries, content comparison/retention boundaries, expiry behavior and storage-unavailable behavior remain open.

OD36 requires per-invocation durable admission order for queued business events across waits/restarts, observable engine-assigned concurrent admission order and preserved positions on duplicate retry (FR-047). Automatic/completion precedence and timer/activity-trigger competition remain as agreed. Atomic ordering and source integration still require proof.

OD37 finalizes accepted, queued and not-yet-selected events as not processed when the machine completes or is cancelled, retaining receipts with the explicit terminal reason, rejecting new admissions and preventing automatic movement to another invocation (FR-048). Retained receipts remain subject to OD35. Atomic terminal/admission races, exact retention details, in-flight cancellation outcomes and integration remain open.

OD23 requires recovery at the normal failed-activity boundary with completed earlier phases preserved (FR-034). Complete propagation, blocked-input handling, exact supported recovery paths and source-grounded integration proof remain open.

OD38 requires continued bounded durable event acceptance while the machine remains active but awaits recovery, with visible blockage and no chart evaluation until recovery permits it (FR-049). Admission cannot bypass recovery or automatically repeat a detected cycle. Exact recovery-gate/admission integration and races remain open.

OD29 requires completed sibling work to be preserved when a regional Action fails and the machine awaits normal recovery, without automatic sibling cancellation or restarting successful regions (FR-040). Still-running work follows normal incident policy; phase advancement and later chart-event evaluation wait for required work to succeed. Other fault paths and exact integration remain open.

### Q3 — Studio contract and remaining capability boundaries

[NEEDS CLARIFICATION: Agree the concrete graph/outline/navigation, slots/conditions, diagnostics/inspection and accessibility/responsive journeys; classify remaining advanced capabilities and data/outcome/reuse details without silently expanding first-release scope.]

The concrete Studio/outcome defaults and saga witness topology are proposed in [research P4](research.md#p4--studio-and-outcome-defaults) and [P5](research.md#p5--proposed-first-saga-witness-topology), with qualification staged in P6. These proposals do not approve a public schema, core ADR, exact topology or production implementation.

The accepted chart/editor/navigation and inspection directions below still need concrete interaction, projection and qualification details. History-state destinations are deferred initially under OD46; full interchange, migration and shared-trigger identity remain uncommitted. Existing variable, expression and outcome contracts retain their authority; this program cannot approve pending core architecture decisions.

OD24 settles the focused chart, activity summaries and drill-in normal activity editing with breadcrumbs and preserved chart context (FR-035). OD43 chooses expandable/collapsible state/region containers with a synchronized hierarchy outline for keyboard and non-canvas navigation, preserving parent context without changing execution semantics (FR-054). Detailed interactions, view-state persistence, accessibility/read-only and responsive journeys remain open.

OD25 settles structured transition configuration and the normal activity/expression editors (FR-036). Exact valid combinations, diagnostics and the keyboard/read-only/responsive journeys remain open; the inspector choice does not expand transition semantics.

OD44 places root/nested StateMachine inspection in normal workflow execution inspection, linking chart/outline, active configuration, pending phases and event/transition history with activity details, receipts/outcomes and normal incident/recovery tools against the exact executed version (FR-055). Exact causal evidence projection, source integration, retention/availability and detailed Studio journeys remain open.

OD21 chooses machine-wide and state-visit-local value scopes (FR-032). Their concrete authoring/visibility/binding integration and outcome details remain to be specified against the existing platform contracts.

OD45 explicitly maps machine-level final states to declared normal activity outcomes without deriving names from states, preserving normal data-output and existing platform contracts (FR-056). Mapping representation, validation, default/unmapped behavior and source/Studio integration remain open; pending core architecture decisions retain their actual agreement boundaries.

OD46 defers history destinations initially, retaining explicit authored targets/initial-state entry and normal durable restart recovery without reducing hierarchy/parallel scope (FR-057). Exact validation and source/Studio qualification remain open.

OD47 requires an initial-release saga-style composition example qualified through Studio and a real durable run/restart using existing explicit native compensation/recovery mechanisms, retaining separate responsibilities and adding no Saga type/engine (FR-058). M0 must first settle its topology and journal-lifetime, fault, uncertainty and reusable-boundary constraints; composition is not yet proved.
