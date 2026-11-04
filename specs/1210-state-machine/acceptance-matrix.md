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
| OD8 — hierarchy and parallel first release | FR-004, FR-021 | A01–A30 together |
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
| A20 | Explicit finality versus non-final waiting | A state with no local outgoing edge is not implicitly complete; parent-level behavior and accidental-dead-end diagnostics are respected | Definition validation and actual hierarchy/region completion witnesses; complete final-entry/scoped propagation remains Q1 |
| A21 | Machine-wide and state-visit values | Machine-wide scope spans state changes; state-local values start fresh on re-entry; a continuing parent visit retains its local scope; normal platform policies remain authoritative | Ordinary activity/binding and Studio declaration witnesses; exact visibility/binding/outcome integration remains Q3 |
| A22 | Ordered applicable transition phases | Source Exit finishes before transition Action, which finishes before target Entry; a durably suspended phase does not permit a later phase to start early | Ordered semantic and actual-host/persistence witnesses; complete hierarchical boundaries and recovery remain Q1/Q2 |
| A23 | Recovery after a completed phase | A completed Exit is preserved when later Action work fails; supported recovery resumes/retries failed work at its ordinary boundary without whole-transition rewind or false exactly-once/compensation claims | Actual incident/recovery/restart witness with completed-phase history and controlled participant evidence; complete propagation/input/recovery integration remains Q2 |
| A24 | Chart/activity editing round trip | State/transition/region chart shows activity summaries; entry, exit, trigger and action work open in normal activity editors, with breadcrumbs and preserved chart context on return | Real Studio authoring/navigation and save/reload witness in root and nested use; detailed keyboard, read-only and responsive journeys remain Q3 |
| A25 | Configure a transition in its inspector | Trigger/Automatic, Condition, Action, Destination, authored order and state-visit behavior are understandable together; ordinary activity/expression editors work and saved rules round-trip without visual-layout priority changes | Real Studio authoring/save/reload and exact published-rule execution witness; detailed valid combinations, diagnostics, keyboard/read-only/responsive behavior remain Q3 |
| A26 | Explicit startup at each applicable level | Machine, sequential compound state and each parallel region use validated explicit initial child states; entering a parallel state starts every region; moving shapes does not change startup | Definition validation, save/reload and actual hierarchical/parallel Studio-to-runtime startup witnesses; scoped completion remains Q1 |
| A27 | Nested phase ordering across durable waits | Exited children finish Exit before exited parents; entered parents finish Entry before entered children; all applicable exits precede Action and Action precedes entries despite suspension/restart | Ordered semantic and actual host/persistence witnesses with deliberately suspended child Exit and parent Entry; complete boundary calculation remains Q1 |
| A28 | Independent regional progress and phase barriers | A durably waiting region does not prevent independent same-phase work in another; hierarchy dependencies and selected-set Exit/Action/Entry barriers hold; later business events wait for selected work; no independent completion order is promised | Controlled multi-region activity work, ordered phase history and actual host/persistence/restart witness; regional Action recovery is A29, other failure paths remain Q2 and runtime integration is unproved |
| A29 | Recover one failed regional Action | Completed sibling work survives failure/recovery/restart without being rerun; StateMachine adds no sibling cancellation; normal incident policy controls pending work and required-success barriers block phase advancement/later chart events | Controlled completed, faulted and durably pending regional participants plus actual incident/retry/restart history and cancellation control; exact recovery integration and other fault paths remain Q2 |
| A30 | Local nested completion and machine finality | Completed compound/parallel lifecycles enable authored completion-triggered transitions without implicitly finishing the whole machine; its own top-level final state completes the activity and permits normal enclosing workflow continuation | Semantic and actual Studio/host witnesses for compound, all-region parallel and root/nested-machine completion; final-entry timing, completion arbitration and exact representation remain Q1 |

## Qualification rules

- Use the normal authoring, publication, event/wait and runtime paths for integration claims. A custom mock executor or a graph-only demonstration cannot establish delivery.
- Preserve existing hosts/stores. Restart and crash qualification uses owned temporary hosts and captured/owned data.
- Record exact source/build, authored definition and published identity, event/activation identities, provider/composition and relevant ordered history.
- Use semantic/unit tests for cheap decision coverage, actual persistence/restart/race tests for durable claims and browser/Studio witnesses for authoring claims.
- Include meaningful failure/negative controls where behavior is claimed. This draft adds no runtime tests and reports no product pass.
- Program acceptance requires reviewed implementation and resulting-main qualification after the design/spec gates; resolving Q1–Q3 or merging this document is not product completion.
