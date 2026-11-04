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
| OD8 — hierarchy and parallel first release | FR-004, FR-021 | A01–A16 together |
| OD9 — durable activity work and visible phase | FR-005 | A10 |
| OD10 — finish selected transition before later events | FR-015 | A10, A11 |
| OD11 — bounded durable event acceptance | FR-016, FR-017 | A11, A13 |
| OD12 — final unhandled outcome, no revival | FR-018 | A12 |
| OD13 — ordinary activities/composites as triggers | FR-005, FR-006, FR-020 | A05, A09, A14 |
| OD14 — restart only rejected trigger | FR-014 | A09 |
| OD15 — action-without-exit versus deliberate re-entry | FR-012 | A04 |
| OD16 — normal definition/version/publication/reuse | FR-002, FR-022 | A01, A02, A15 |

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

## Qualification rules

- Use the normal authoring, publication, event/wait and runtime paths for integration claims. A custom mock executor or a graph-only demonstration cannot establish delivery.
- Preserve existing hosts/stores. Restart and crash qualification uses owned temporary hosts and captured/owned data.
- Record exact source/build, authored definition and published identity, event/activation identities, provider/composition and relevant ordered history.
- Use semantic/unit tests for cheap decision coverage, actual persistence/restart/race tests for durable claims and browser/Studio witnesses for authoring claims.
- Include meaningful failure/negative controls where behavior is claimed. This draft adds no runtime tests and reports no product pass.
- Program acceptance requires reviewed implementation and resulting-main qualification after the design/spec gates; resolving Q1–Q3 or merging this document is not product completion.
