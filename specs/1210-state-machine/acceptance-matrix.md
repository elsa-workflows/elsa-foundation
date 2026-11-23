# StateMachine acceptance matrix

**Status:** Draft test design. No StateMachine implementation or execution evidence exists from this work.

This matrix connects accepted [owner decisions](https://github.com/elsa-workflows/elsa-foundation/issues/3815#accepted-owner-decisions-4-november-2026) to [requirements](spec.md). It describes the required witnesses; it is not a receipt that they passed.

## Owner decision coverage

| Decision | Requirement coverage | Witness |
|---|---|---|
| OD1 — generic infrastructure; saga boundary | FR-001, FR-020, FR-023 | A01, A14 |
| OD2 — fresh per-visit deadline | FR-013, FR-019 | A04, A12 |
| OD3 — first eligible conflicting trigger processed | FR-008 | A05 |
| OD4 — root/nested, own designer, ordinary work | FR-003, FR-005, FR-024 | A01, A02, A10 |
| OD5 — child-first with eligible ancestor fallback | FR-007 | A03 |
| OD6 — compatible same-event regions | FR-009, FR-010 | A06, A07 |
| OD7 — all-region finality, explicit early exit | FR-011 | A08 |
| OD8 — hierarchy and parallel first release | FR-004, FR-021 | A01–A49 together |
| OD9 — durable activity work and visible phase | FR-005 | A10 |
| OD10 — finish selected transition before later events | FR-015 | A10, A11 |
| OD11 — bounded durable event acceptance | FR-016, FR-017 | A11, A13 |
| OD12 — final unhandled outcome, no revival | FR-018 | A12 |
| OD13 — ordinary activities/composites as triggers | FR-005, FR-006, FR-020 | A05, A09, A14 |
| OD14 — restart only rejected trigger | FR-014 | A09 |
| OD15 — action-without-exit versus deliberate re-entry | FR-012 | A04 |
| OD16 — normal definition/version/publication/reuse | FR-002, FR-022 | A01, A02, A15 |
| OD17 — explicit same-state transition order | FR-028 | A17 |
| OD18 — automatic transitions after entry work | FR-029 | A18 |
| OD19 — automatic progress before queued events | FR-030 | A19 |
| OD20 — explicit final-state intent | FR-031 | A20 |
| OD21 — machine and state-visit value scopes | FR-032 | A21 |
| OD22 — exit/action/entry phase ordering | FR-033 | A22 |
| OD23 — recover failed work, preserve completed phases | FR-034 | A23 |
| OD24 — focused chart and ordinary activity drill-in | FR-035 | A24 |
| OD25 — structured transition inspector | FR-036 | A25 |
| OD26 — explicit initial states and all-region startup | FR-037 | A26 |
| OD27 — child-first Exit and parent-first Entry | FR-038 | A27 |
| OD28 — concurrent regional work within phase barriers | FR-039 | A28 |
| OD29 — preserve siblings during regional Action recovery | FR-040 | A29 |
| OD30 — completion-triggered transitions and scoped completion | FR-041 | A30 |
| OD31 — final entry succeeds before completion | FR-042 | A31 |
| OD32 — automatic, completion, then business-event processing | FR-043 | A32 |
| OD33 — stop excessive internal progress and require recovery | FR-044 | A33 |
| OD34 — immediate full-capacity rejection | FR-045 | A34 |
| OD35 — scoped accepted-event deduplication | FR-046 | A35 |
| OD36 — durable per-invocation business-event order | FR-047 | A36 |
| OD37 — final outcomes for queued events when invocation ends | FR-048 | A37 |
| OD38 — bounded acceptance while active and awaiting recovery | FR-049 | A38 |
| OD39 — reject direct cross-region edges initially | FR-050 | A39 |
| OD40 — explicit unrelated-region conflict precedence | FR-051 | A40 |
| OD41 — explicit parent-to-descendant visit behavior | FR-052 | A41 |
| OD42 — re-arm a successful action-without-exit reaction | FR-053 | A42 |
| OD43 — collapsible nested chart and synchronized outline | FR-054 | A43 |
| OD44 — integrated workflow execution inspection | FR-055 | A44 |
| OD45 — explicit final-state to activity-outcome mapping | FR-056 | A45 |
| OD46 — defer history destinations; retain durable recovery | FR-057 | A46 |
| OD47 — qualify explicit native saga-style composition | FR-058 | A47 |
| OD48 — once-per-visit completion opportunity | FR-059 | A48 |
| OD49 — explicit parallel-parent whole re-entry | FR-060 | A49 |

## Witness definitions

| ID | Scenario | Required observation | Appropriate evidence and current readiness |
|---|---|---|---|
| A01 | Author a root machine in Studio | Supported state/transition/region and activity/condition content survives save/reload; exact published definition runs | Actual Studio + native host journey; implementation pending; detailed authoring/finality depends on Q1/Q3 |
| A02 | Nest and reuse a machine | Parent work precedes/follows the machine under normal activity contracts; reuse/publication stays in existing mechanisms | Actual Studio + parent/child execution and exact pins; implementation pending |
| A03 | Child/ancestor matching | Eligible child wins; ineligible child permits eligible parent fallback; failure is not silently false | Semantic tests + native hierarchy witness; complete guard/fault contract depends on Q1/Q2 |
| A04 | Preserve visit or re-enter | Action-without-exit preserves waits/deadline; deliberate re-entry runs exit/entry and creates fresh activation | Ordered history, timer and stale-completion tests + actual host; detailed order depends on Q1 |
| A05 | Competing distinct triggers | First eligible processed candidate wins within the conflicting scope; obsolete competitor cannot later cause another exit | Deterministic race tests + observable native history; full arbitration/commit details depend on Q1/Q2 |
| A06 | Same event in parallel regions | One logical event can produce compatible regional reactions without inventing independent business-event deliveries | Semantic selection tests + actual parallel host/Studio witness; conflict/ordering details depend on Q1/Q2 |
| A07 | Local transition and parent exit | Local reaction preserves independent region waits; parent exit cancels the relevant descendant scope | Child-cancellation and real wait/resume witnesses; implementation pending |
| A08 | Parallel completion | One final region is insufficient; all final regions complete; authored early exit remains distinct | Semantic tests + actual Studio/runtime evidence; enclosing completion contract depends on Q1 |
| A09 | False guard and local rearm | Only rejected trigger restarts while source stays active; deadline/other waits unchanged; old input not replayed | Event/timer/composite trigger tests with negative controls; immediate-completion loop contract depends on Q1 |
| A10 | Durable pending phase | Pending entry/exit/action is inspectable; new business events do not pre-empt selected work | Studio inspection + actual host/persistence restart at selected checkpoints; full phase contract depends on Q1/Q2 |
| A11 | Accepted event survives restart | Admission distinct from handling; pending phase and retained event recover and later settle under declared ordering | Actual host restart with owned store and input/outcome evidence; canonical admission seam and guarantees depend on Q2 |
| A12 | Unhandled and stale are distinct | Unhandled event is not revived by later state changes; retired task/timer completion cannot target a new visit | Re-entry/restart/stale-delivery tests + recorded outcomes; outcome representation depends on Q2 |
| A13 | Configured capacity boundary | With a declared C, admission obeys the bound including concurrent last-slot attempts, overflow and unavailable storage | Provider/integration tests and actual ingress witness; policy/identity/receipts depend on Q2; C is a test configuration, not a guessed production default |
| A14 | External work and failures | Cancellation is not compensation; composite rearm may repeat work; faults/retry do not falsely report previous effects undone | Meaningful controlled participant/side-effect evidence and normal recovery integration; contract depends on Q2 |
| A15 | Existing instance and new publication | Waiting instance resumes against its exact pinned version while a newer edited definition exists | Studio publish/edit plus actual retained/pinned runtime evidence; data/reuse detail depends on Q3 |
| A16 | Understandable authoring/inspection | Graph and non-canvas paths, conditions/slots, current configuration/phases/causality, error/read-only and narrow-width behavior work together | Real representative Studio hosts; accessibility and responsive journeys depend on Q3 |
| A17 | Same input, several eligible transitions on one state | First eligible in explicit authored order wins; visual graph rearrangement does not change priority; child/ancestor and compatible-region rules remain intact | Definition round-trip, semantic selection and Studio ordering witnesses; implementation pending |
| A18 | Automatic transition after entry | An eligible condition-based path can proceed without a trigger/artificial event after entry work completes, including after durable entry work resumes | Entry/automatic selection and actual-host witness; complete automatic-candidate arbitration, reevaluation and loop rules remain Q1 |
| A19 | Automatic chain and queued event | Eligible automatic transitions run until none remain before the queued business event is evaluated against the resulting configuration; cycles cannot run indefinitely | Ordered semantic/actual-host witnesses and protection boundary cases; the concrete loop/protection/recovery mechanism remains Q1 |
| A20 | Explicit finality versus non-final waiting | A state with no local outgoing edge is not implicitly complete; parent-level behavior and accidental-dead-end diagnostics are respected | Definition validation and actual hierarchy/region completion witnesses; remaining completion conflicts and exact completion identity/representation and durable consumption remain Q1 |
| A21 | Machine-wide and state-visit values | Machine-wide scope spans state changes; state-local values start fresh on re-entry; a continuing parent visit retains its local scope; normal platform policies remain authoritative | Ordinary activity/binding and Studio declaration witnesses; exact visibility/binding/outcome integration remains Q3 |
| A22 | Ordered applicable transition phases | Source Exit finishes before transition Action, which finishes before target Entry; a durably suspended phase does not permit a later phase to start early | Ordered semantic and actual-host/persistence witnesses; complete hierarchical boundaries and recovery remain Q1/Q2 |
| A23 | Recovery after a completed phase | A completed Exit is preserved when later Action work fails; supported recovery resumes/retries failed work at its ordinary boundary without whole-transition rewind or false exactly-once/compensation claims | Actual incident/recovery/restart witness with completed-phase history and controlled participant evidence; complete propagation/input/recovery integration remains Q2 |
| A24 | Chart/activity editing round trip | State/transition/region chart shows activity summaries; entry, exit, trigger and action work open in normal activity editors, with breadcrumbs and preserved chart context on return | Real Studio authoring/navigation and save/reload witness in root and nested use; detailed keyboard, read-only and responsive journeys remain Q3 |
| A25 | Configure a transition in its inspector | Trigger/Automatic, Condition, Action, Destination, authored order and state-visit behavior are understandable together; ordinary activity/expression editors work and saved rules round-trip without visual-layout priority changes | Real Studio authoring/save/reload and exact published-rule execution witness; detailed valid combinations, diagnostics, keyboard/read-only/responsive behavior remain Q3 |
| A26 | Explicit startup at each applicable level | Machine, sequential compound state and each parallel region use validated explicit initial child states; entering a parallel state starts every region; moving shapes does not change startup | Definition validation, save/reload and actual hierarchical/parallel Studio-to-runtime startup witnesses; scoped completion remains Q1 |
| A27 | Nested phase ordering across durable waits | Exited children finish Exit before exited parents; entered parents finish Entry before entered children; all applicable exits precede Action and Action precedes entries despite suspension/restart | Ordered semantic and actual host/persistence witnesses with deliberately suspended child Exit and parent Entry; complete boundary calculation remains Q1 |
| A28 | Independent regional progress and phase barriers | A durably waiting region does not prevent independent same-phase work in another; hierarchy dependencies and selected-set Exit/Action/Entry barriers hold; later business events wait for selected work; no independent completion order is promised | Controlled multi-region activity work, ordered phase history and actual host/persistence/restart witness; regional Action recovery is A29, other failure paths remain Q2 and runtime integration is unproved |
| A29 | Recover one failed regional Action | Completed sibling work survives failure/recovery/restart without being rerun; StateMachine adds no sibling cancellation; normal incident policy controls pending work and required-success barriers block phase advancement/later chart events | Controlled completed, faulted and durably pending regional participants plus actual incident/retry/restart history and cancellation control; exact recovery integration and other fault paths remain Q2 |
| A30 | Local nested completion and machine finality | Completed compound/parallel lifecycles enable authored completion-triggered transitions without implicitly finishing the whole machine; its own top-level final state completes the activity and permits normal enclosing workflow continuation | Semantic and actual Studio/host witnesses for compound, all-region parallel and root/nested-machine completion; final-entry timing is A31 and automatic/completion precedence is A32; remaining completion conflicts and exact representation remain Q1 |
| A31 | Final entry waits, fails and recovers | Ordinary final-state entry work may wait durably or fail; no region/container/machine completion occurs before entry succeeds, including after restart and normal recovery | Actual Studio/host/persistence witness with controlled pending and faulted final entry, normal recovery and ordered completion evidence; exact completion integration remains unproved |
| A32 | Internal progress before queued business events | Automatic paths precede still-applicable completion reactions and are checked again after each reaction; queued business events wait until internal progress settles; accepted selection rules and loop protection apply | Ordered semantic and actual host/restart witness with both kinds of internal progress and queued input; exact completion representation and concrete cycle protection remain open |
| A33 | Loop protection stops without automatic repetition | Excessive automatic/completion progress stops; committed progress and actionable causal evidence survive; normal operations expose an incident requiring recovery and the detected cycle does not automatically repeat | Protection-boundary tests plus actual host/incident/restart witness with controlled effects; concrete limits, detection lifetime and supported recovery actions remain technical design work and integration is unproved |
| A34 | Full capacity rejects new admissions | At declared capacity C, new admissions receive an immediate explicit capacity rejection; accepted events remain intact, rejected attempts are not accepted and admission does not wait for space | Actual ingress and controlled full-queue/concurrent last-slot witnesses with retained accepted-event evidence; exact accounting, deduplication/retention integration and source integration remain Q2 |
| A35 | Retry accepted admission after a lost response | Identical retained event ID/contents for one machine invocation return its existing receipt/current outcome without another queue entry; conflicting contents are rejected and invocation scopes remain distinct | Actual ingress/lost-response/concurrent-retry/restart witnesses and identity/conflict controls; declared retention/comparison boundaries and source integration remain Q2; no exactly-once external-effect claim |
| A36 | Ordered business-event evaluation across waits/restart | Queued events follow per-invocation durable admission order after internal progress settles; concurrent order is observable and duplicate retries preserve position; accepted timer/activity-trigger competition remains intact | Actual ingress/host/persistence witness with deliberately competing admissions, duplicate retries and a suspended phase/restart; atomic ordering and exact source integration remain unproved |
| A37 | Complete/cancel with accepted unselected events | Queued unselected events receive final not-processed outcomes with completion/cancellation reasons; retained receipts remain queryable under policy, new admissions are rejected and events never automatically move to a new invocation | Actual complete/cancel/restart and terminal/admission race witnesses, retained receipt/duplicate-retry checks and a new-invocation negative control; exact atomicity/retention and selected-work cancellation remain Q2 |
| A38 | Admit while recovery blocks evaluation | Active machines awaiting action/loop recovery still accept durably within bounds with visible blockage; queued events obey ordering/deduplication and never bypass recovery or automatically repeat the detected cycle | Actual incident/ingress/host/restart/recovery witness plus full/terminal rejection and concurrent recovery/admission controls; exact recovery-gate integration remains unproved |
| A39 | Direct cross-region edge validation | Definition validation and Studio reject direct edges between independent regions with a clear diagnostic; valid local transitions, shared-event reactions and explicit container exits still work | Definition-validation and actual Studio author/save/reload/publish controls with valid parallel runtime witnesses; complete allowed boundaries/conflict arbitration remain Q1 and editor integration is unproved |
| A40 | Local regional path competes with a container exit | Explicit authored order resolves conflicting candidates sourced in unrelated regions without implicit container-exit preference; child-over-parent precedence and compatible selections remain intact and shape movement does not alter priority | Semantic conflict-set controls and actual Studio order/save/reload/published execution witness; formal compatible-set algorithm, exact boundaries and losing-trigger disposition remain Q1 |
| A41 | Parent-to-descendant preservation versus re-entry | A compound parent's authored transition to a descendant preserves parent visit/waits/deadline or deliberately exits/re-enters with a fresh visit according to explicit choice; accepted child cancellation, phase/hierarchy and recovery rules hold | Actual Studio choice/save/reload/publish and host/timer/phase/restart witnesses for both modes, including obsolete-child completion controls; complete boundaries, schema/defaults and integration remain open |
| A42 | Repeated reaction within one visit | After successful trigger-based action without exit, only that trigger re-arms while the same visit remains active; other waits/deadlines persist, consumed input is not replayed, and pending/failed action or ended visit does not re-arm it | Actual event/timer/composite-trigger and host/restart/recovery witnesses with repeated new inputs and controlled trigger-work counts; exact scheduling/recovery and immediate-completion loop protection remain open |
| A43 | Navigate nested chart and outline | Expand/collapse retains parent context; hierarchy outline and chart remain synchronized for keyboard/non-canvas navigation; activity drill-in remains normal and navigation does not alter startup/priority semantics | Actual Studio navigation/selection controls for root/nested hierarchy and parallel regions with authored-rule checks; view-state persistence, accessible/read-only/responsive details and editor integration remain Q3 |
| A44 | Inspect the machine in its workflow execution | Exact-version root/nested chart/outline, configuration, pending phases and linked event/transition history lead to ordinary activity evidence, receipts/outcomes and normal recovery tools; surrounding workflow context and permissions remain intact | Actual Studio/host inspection with newer published-definition negative control, durable phase/recovery/event evidence and recorded causal-order controls; projection/integration/retention/availability and detailed Studio qualification remain open |
| A45 | Complete a nested machine through normal outcomes | Explicit machine-level final-state mapping emits its declared normal activity outcome after successful final entry, enabling the enclosing Flowchart branch; state renaming does not implicitly rename outcomes and nested completion/data outputs follow existing contracts | Actual Studio mapping/save/reload/publish and parent-machine host execution witnesses with renamed-state, final-entry wait and nested-completion controls; mapping validation/defaults and source integration remain open |
| A46 | Authored re-entry differs from durable restart | Initial-release re-entry uses explicit targets/initial states without history destinations; explicit descendant targeting/visit modes remain valid, while restart restores the existing active configuration and waits | Definition/Studio scope validation and actual hierarchy/parallel host witnesses contrasting deliberate exit/re-entry with restart of a non-initial active configuration; no later history feature or runtime pass is claimed |
| A47 | Saga-style composition through existing mechanisms | The agreed authored lifecycle uses StateMachine and explicit native compensation/recovery with separate responsibilities and no Saga type/engine; its declared journal lifetime, fault/uncertainty handling and reusable boundaries hold through restart; transitions/cancellation do not imply undo | Actual Studio author/save/reload/publish and controlled participant/host/store run-recovery-restart witness with occurrence/operation-key/journal evidence and boundary/cancellation controls; exact topology must be agreed in M0 and no StateMachine composition proof exists yet |
| A48 | Consume ineligible completion without later revival | A valid completion evaluation with no eligible transition consumes the opportunity for that visit; later value changes do not revive it, while a new visit can complete again; errors are not treated as false and processing precedence remains intact | Semantic and actual host/persistence/restart witness around consumption, later value changes and explicit re-entry, with ongoing-rule and condition-error controls; exact completion identity/representation and recovery integration remain unproved |
| A49 | Parallel parent targets a descendant through whole re-entry | Explicit re-entry exits/resets parent and all old regions, follows the target in its region and initial states in others; unsupported parent-preserving mode is diagnosed, old completions cannot affect new visits and machine-invocation event guarantees remain intact | Actual Studio validation/save/reload/publish plus host/timer/phase/restart witnesses, stale-completion and queued-business-event controls, and valid local-region alternatives; exact boundaries and source/Studio integration remain unproved |

## Qualification rules

- Use the normal authoring, publication, event/wait and runtime paths for integration claims. A custom mock executor or a graph-only demonstration cannot establish delivery.
- Preserve existing hosts/stores. Restart and crash qualification uses owned temporary hosts and captured/owned data.
- Record exact source/build, authored definition and published identity, event/activation identities, provider/composition and relevant ordered history.
- Use semantic/unit tests for cheap decision coverage, actual persistence/restart/race tests for durable claims and browser/Studio witnesses for authoring claims.
- Include meaningful failure/negative controls where behavior is claimed. This draft adds no runtime tests and reports no product pass.
- Program acceptance requires reviewed implementation and resulting-main qualification after the design/spec gates; resolving Q1–Q3 or merging this document is not product completion.
