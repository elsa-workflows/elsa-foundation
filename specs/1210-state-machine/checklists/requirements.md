# Specification Quality Checklist: Elsa 4 StateMachine

**Purpose**: Check this provisional specification before planning or implementation.

**Created**: 2026-11-04

**Feature**: [specification](../spec.md)

## Content Quality

- [x] User-facing specification describes outcomes and constraints without choosing an internal implementation.
- [x] Generic infrastructure is separated from sample approval-domain concepts.
- [x] Mandatory user scenarios, requirements, entities, success criteria and assumptions are present.
- [x] Accepted OD1–OD49 are separated from proposals and open choices.

## Requirement Completeness

- [ ] No critical clarification remains. Q1–Q3 are open.
- [x] Accepted direction has requirement/scenario coverage in the acceptance matrix.
- [ ] Complete execution/selection and phase rules are testable without unagreed defaults. Q1 remains open.
- [ ] Bounded admission/delivery/outcome and fault/recovery contracts are concrete. Q2 remains open.
- [ ] Exact Studio journeys and remaining advanced/data/outcome boundaries are agreed. Q3 remains open.
- [x] Scope includes both hierarchy and parallel regions; early slices do not count as full completion.
- [x] Dependencies, initiative boundaries and existing saga ownership are explicit.

## Feature Readiness

- [x] Primary author/operator journeys and required actual-host/Studio witnesses are identified.
- [x] The matrix distinguishes expected witnesses from evidence that has passed.
- [x] No source observation is called runtime qualification.
- [ ] Ready for technical planning or production implementation: blocked by Q1–Q3, the architecture decision/witness plan and applicable agreement.

## Validation notes

Current draft validation: accepted OD1–OD49 are traced to requirements/scenarios/witnesses. The complete execution, delivery/recovery and Studio contracts intentionally remain open in Q1–Q3; no checklist box substitutes for agreement.

Initial independent review found that non-pre-emption alone did not forbid premature evaluation against an old configuration. FR-015 and its scenario now explicitly require selected work to finish before later event evaluation, matching OD10.

Accepted extensions include authored same-state order, automatic progress before queued business events, explicit finality, machine/state-visit value scopes and exit/action/entry ordering. Their remaining detailed integration/protection/recovery boundaries stay visible in Q1–Q3.

OD23 requires normal failed-activity recovery with completed earlier phases preserved. Source integration, propagation, blocked inputs and supported recovery paths still require Q2 agreement and proof.

OD24 chooses a focused chart with activity summaries and normal activity-editor drill-in, breadcrumbs and preserved chart context. The remaining detailed Studio journeys stay open in Q3.

OD25 chooses a structured transition inspector with the normal activity and expression editors. Detailed valid choices, diagnostics and keyboard/read-only/responsive journeys remain Q3.

OD26 requires explicit, validated initial states at each applicable level and starts all regions on parallel entry. Complete entry/exit boundaries and scoped completion remain Q1.

OD27 requires child-first Exit and parent-first Entry with durable phase completion. Complete boundary calculation remains Q1.

OD28 allows independent regional work to progress concurrently within each phase, preserving hierarchy dependencies and selected-set phase barriers. OD29 settles sibling preservation during regional Action recovery; other partial-failure paths and runtime integration remain open. Independent branch completion order is not promised.

OD29 preserves completed sibling work during normal recovery from a regional Action failure and adds no automatic sibling cancellation. Normal incident policy controls pending work; phase advancement and later chart events remain blocked until required work succeeds. Other fault paths and exact integration remain Q2.

OD30 adds authored completion-triggered transitions for completed compound/parallel states, keeps nested completion local and completes the machine at its own top-level final state. Final-entry timing follows OD31; remaining completion conflicts and exact completion identity/representation and durable consumption remain Q1.

OD31 allows ordinary entry work in final states and requires success before completion propagates. Normal waiting, fault/recovery and restart behavior apply; remaining completion conflicts and exact integration remain open.

OD32 orders automatic progress before still-applicable completion reactions, rechecks automatic paths after each reaction and delays business events until internal progress settles. Concrete cycle protection/recovery, exact completion representation and remaining cross-state/region conflicts remain open.

OD33 stops detected excessive automatic/completion progress, preserves committed progress and requires recovery through normal operations without automatic repetition. Concrete limits, detection lifetime, diagnostics and supported recovery actions remain technical design work; no numeric default or runtime integration is claimed.

OD34 requires immediate explicit rejection when event capacity is full, preserving accepted events without waiting or eviction. Exact accounting, concurrent admission/deduplication integration, retention and unavailable-storage behavior remain Q2.

OD35 chooses accepted-event deduplication by sender ID scoped to a machine invocation within declared retention. Identical retries return existing receipts/outcomes without another queue entry; conflicting contents are rejected. Exact comparison/retention boundaries, concurrent retry and runtime integration remain Q2; exactly-once effects are not promised.

OD36 preserves per-invocation durable admission order across waits/restarts, assigns observable concurrent order and retains original positions on duplicate retry. Existing internal-progress precedence and timer/trigger competition remain in force; atomic ordering and source integration are unproved.

OD37 records final not-processed completion/cancellation outcomes for accepted queued unselected events, retains receipts under policy, rejects new admissions and prevents automatic movement to another invocation. Terminal/admission atomicity, exact retention and selected-work cancellation outcomes remain Q2.

OD38 continues bounded durable event acceptance while an active machine awaits recovery, with visible blockage and no evaluation before recovery permits it. Accepted capacity, terminal, order and deduplication rules remain in force; recovery-gate/admission integration and races remain Q2.

OD39 rejects direct cross-region edges initially with clear definition/Studio diagnostics while preserving local transitions, shared-event reactions and explicit parallel-container exits. Complete allowed boundaries, formal compatible-set selection and editor/runtime integration remain open; hierarchy/parallel first-release scope remains intact.

OD40 resolves unrelated-region conflicts through explicit authored order, preserving child-over-parent precedence and compatible reactions without implicit container-exit preference or graph-layout priority. Formal compatible-set selection, exact boundaries, losing-trigger disposition and source/Studio proof remain open.

OD41 supports explicit parent-visit preservation or re-entry for compound-parent transitions to descendants, retaining parent waits/deadlines or starting a fresh parent visit respectively. Complete boundaries, validation, schema/defaults and source/Studio integration remain open; parallel-parent behavior follows OD49 and direct cross-region edges remain excluded.

OD42 re-arms only a successful trigger-based action-without-exit's own trigger while the same visit remains active, preserving independent waits/deadlines and requiring new event input. It replaces the prior persistent-reaction proposal; scheduling/recovery, losing-trigger disposition and immediate-completion loop protection remain open.

OD43 chooses collapsible state/region chart containers with a synchronized hierarchy outline for keyboard and non-canvas navigation. Preserve parent context and normal activity-editor drill-in without changing execution semantics; detailed interactions, view-state persistence and accessible/read-only/responsive journeys remain Q3.

OD44 integrates root/nested machine inspection into normal workflow execution inspection against the exact executed version, linking chart/configuration/phases/history with activity details, receipts/outcomes and normal recovery tools. Causal projection, integration, retention/availability and detailed Studio qualification remain open.

OD45 explicitly maps machine-level final states to declared normal activity outcomes, preserving normal data-output contracts and avoiding names inferred from states. Mapping representation/validation/defaults and source/Studio proof remain open; pending core architecture decisions are not approved by this feature choice.

OD46 defers history destinations initially while retaining explicit authored re-entry, durable restoration of active configuration/waits, accepted descendant-target modes and hierarchy/parallel scope. Exact validation and source/Studio qualification remain open; no later history delivery is promised.

OD47 requires an initial-release qualified saga-style composition example through Studio and a real durable run/restart, using existing explicit native compensation/recovery without a Saga type/engine. Exact topology and lifetime/fault/uncertainty/reusable-boundary behavior must be agreed in M0 and proved; existing compensation evidence is not StateMachine proof.

OD48 offers completion once per visit and consumes it when no transition is eligible, without revival after later value changes. A new visit can complete again; ongoing rules use automatic conditions/new triggers. Exact completion identity/representation and durable consumption/recovery remain open.

OD49 initially requires explicit whole-parent re-entry for parallel-parent-to-descendant transitions, resetting all regions and starting the explicit target in its region and initial states in others. Unsupported parent-preserving mode is diagnosed; compound-parent modes and local-region alternatives remain available. Complete boundaries, validation and source/Studio proof remain open.

The [M0 engineering proposal](../research.md#m0-engineering-proposal-for-review) now supplies concrete candidate integration, selection/phase, admission/retention/recovery, Studio/outcome and saga-topology proposals, with a bounded witness sequence. Q1–Q3 remain open pending review/agreement and source integration proof. No implementation readiness or approved specification is claimed.
