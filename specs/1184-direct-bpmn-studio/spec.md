# Feature Specification: Direct BPMN journeys in Studio

**Feature Branch**: `agent/codex/2907-bpmn-studio-recovery`

**Created**: 2026-11-01

**Status**: In progress — authored publication, durable discovery/inspection, actual Runtime-only accepted-work recovery, typed Studio client, direct-human editor/publication and process/run navigation controls verified; actual direct-human Studio P1 recovery/replay verified on source11437c0bd; existing saved-Studio/native/Runtime-only compatibility verified on sources5618ed377/358ec420e; real ETL/claim/interruption/final delivery gates remain open.

**Input**: Deliver Program #2892 through working, recoverable BPMN, native, ETL and Studio journeys. Start with direct BPMN start → wait → restart → resume → complete, then real ETL failure/recovery and BPMN-to-ETL integration. Preserve authoring and saved-state compatibility; let demonstrated journey needs drive architecture.

**Tracking**: [#2907](https://github.com/elsa-workflows/elsa-foundation/issues/2907), under [#2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892). Existing hosting prerequisite [#2898 is delivered](https://github.com/elsa-workflows/elsa-foundation/issues/2898#issuecomment-6556163599). This specification covers remaining first-class integration and operational product evidence; it does not reopen that implementation.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Run and recover a BPMN process in Studio (Priority: P1)

An author opens or imports a supported BPMN process, configures its work, saves and publishes it, then starts a direct BPMN execution. When execution waits for a person, the author can leave Studio, lose the host and return to the same execution. They find the outstanding work, complete it and inspect the completed process through ordinary navigation.

**Why this priority**: The host already demonstrates this journey through supported commands. Studio must make it a first-class user journey.

**Independent Test**: Perform the journey in real Studio with an owned host. Restart the host while human work is outstanding, then reopen the execution from a fresh browser session without pasting an identity obtained from a script.

**Acceptance Scenarios**:

1. **Given** a supported process with a human wait, **When** its author saves, publishes and starts it directly, **Then** Studio shows that published process and its durable outstanding work; a native wrapper run is not presented as direct BPMN execution.
2. **Given** the execution is waiting, **When** the host restarts against the same saved state and the author opens Studio again, **Then** ordinary process/run navigation finds the same execution, published version and outstanding work.
3. **Given** an authorized participant can complete the current work, **When** they complete it and repeat the action after another host restart, **Then** the process completes once and the repeated action shows its retained outcome without advancing execution again.
4. **Given** a process outside the declared executable profile, **When** its author imports or publishes it, **Then** Studio identifies unsupported or meaning-changing constructs before admitting execution and preserves the source for correction.

### User Story 2 - Recover a real ETL failure from a BPMN service (Priority: P2)

An author selects an existing published ETL pipeline for a BPMN service. An operator follows that service into its native worker and ETL run, inspects a held failure, restarts the host and explicitly retries the exact failed service after restoring its dependency. The original failure remains inspectable alongside the replacement and completed parent.

**Why this priority**: This exercises the delivered data-processing path before adding connector families or general management abstractions.

**Independent Test**: Use the existing finite HTTP/file pipeline and owned fixture. Cause a retained dependency failure after external effects, restart the host while the failure is held, restore the identical dependency, retry through Studio and inspect the physical destination and BPMN/native/ETL histories.

**Acceptance Scenarios**:

1. **Given** a published ETL selection, **When** an author binds and publishes a BPMN service, **Then** the selected immutable pipeline version and native worker remain pinned when a newer pipeline version is published.
2. **Given** a technical failure held for retry, **When** the host restarts, **Then** Studio still shows the original failed attempt, incident and available supported actions.
3. **Given** the dependency is restored, **When** the operator retries that exact incident, **Then** a distinct physical attempt runs within the retained budget, the original failure is preserved and success advances the parent once.
4. **Given** a retry was accepted but its response was lost, **When** the operator repeats it or reloads the page, **Then** Studio finds the same operation without starting an additional attempt.

### User Story 3 - Operate exact work and understand interruption (Priority: P3)

An authorized participant claims and completes human work. An operator understands when a timer, cancellation or accepted result makes that work obsolete. Processes invoke existing external/native work, and inspection shows what remains owed after semantic completion.

**Why this priority**: #2907 requires task claim/completion, external work and truthful operational boundaries. Completion alone does not prove claim ownership or recovery.

**Independent Test**: Race two sessions for the same authorized work; interrupt human/service work in both arrival orders; repeat completion, retry and cancellation after host loss. Inspect accepted outcomes, refused stale actions and remaining settlement.

**Acceptance Scenarios**:

1. **Given** two sessions contend for one claimable item, **When** they claim it, **Then** at most one current claim authorizes completion; a stale or unrelated claim cannot complete another attempt.
2. **Given** a timer or cancellation wins, **When** a former participant submits completion, **Then** the action is obsolete and does not resurrect work or replace the terminal result.
3. **Given** semantics have finished but native cancellation or result settlement remains owed, **When** an operator inspects/reloads the execution, **Then** Studio distinguishes the remaining responsibility from fully settled completion.
4. **Given** an unavailable capability or insufficient permission, **When** a participant inspects the process, **Then** useful permitted reading remains available and the unavailable action has an accurate explanation.

### User Story 4 - Keep existing workflows usable (Priority: P1)

Existing native/embedded BPMN authors retain their editor, authoring documents, published versions and saved executions. Operators still inspect accepted work when authoring facilities or new admission are unavailable.

**Why this priority**: Compatibility is part of the requested outcome.

**Independent Test**: Repeat both frozen saved-Studio families: open/edit/export/save/publish, Published Run, Test Run, original-run inspection and restart. Use the established native composite and legacy recovery controls for affected behavior.

**Acceptance Scenarios**:

1. **Given** either captured saved workflow/layout, **When** it opens and exports before an intentional edit, **Then** its authoring fingerprint, supported configuration and exported BPMN remain unchanged.
2. **Given** embedded BPMN under a native composite, **When** it completes, faults or is cancelled, **Then** that composite retains its declared outcome and one continuation/settlement.
3. **Given** accepted work and unavailable authoring/new admission, **When** an operator inspects or resumes it after restart in the qualified runtime composition, **Then** recovery needs neither the authoring document nor republished dependencies.

### Edge Cases

- Imported constructs can be retained or drawn but are outside the executable profile.
- A selected worker/pipeline publication changes, disappears or becomes unavailable before publication or after admission.
- A command commits but its response is lost; a browser refreshes during pending admission.
- A claim/attempt becomes stale, or a timer/retry/cancellation wins concurrently.
- Semantic completion precedes downstream cancellation, delivery or acknowledgement.
- Backend, tenant, identity or selection changes while a request is in flight.
- A model/run list spans pages, is empty or receives an obsolete continuation after scope/filter changes.
- A backend supplies runtime inspection without authoring, ETL authoring, new admission or hosting.
- Native source-owned/read-only content, undo/redo, layout, nested bindings and original saved executions remain present.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The supported BPMN profile MUST name executable constructs, human/native/ETL work, timers/correlation, business errors, technical failures/incidents and XML/BPMNDI retention. Unsupported or meaning-changing publication MUST be refused before work is accepted.
- **FR-002**: Authors MUST open/import, edit, save/reopen and publish supported processes in Studio with stable process/element identities, bindings, default/conditional routes and relevant layout.
- **FR-003**: Direct BPMN publication/execution MUST be identifiable and distinct from native BpmnProcess publication, without requiring a native wrapper execution.
- **FR-004**: Process, published-version and execution discovery MUST be scoped to backend and authorized identity/tenant, use bounded pages and support navigation after browser/host restart. Script-supplied identities or browser-local lists alone MUST NOT be the recovery mechanism.
- **FR-005**: Inspection MUST show the pinned process/version, lifecycle, current work, service/ETL relationships, incidents and accepted operation outcomes. Transient browser state is not execution authority.
- **FR-006**: Claim-enabled human work MUST authorize completion only for the exact current claim and attempt. Concurrent claims cannot produce two current owners; obsolete completion cannot advance the process. Existing unclaimed profiles and their retained completion requests MUST remain compatible; claim-required work MUST use an explicit versioned profile that older readers refuse.
- **FR-007**: Repeating an accepted claim, completion, retry or cancellation after an uncertain response/restart MUST recover its retained outcome. The same operation identity with different content MUST be refused.
- **FR-008**: Native/external work MUST retain its immutable executable and physical attempt identity under existing shared ownership/settlement rules. Studio integration MUST NOT introduce another scheduler or interpreter.
- **FR-009**: Authors MUST select an existing published ETL pipeline through available authoring choices. Validation MUST identify unavailable/obsolete selections before publication while preserving already accepted immutable work.
- **FR-010**: Studio MUST show held service failure, original attempt, retry eligibility and remaining budget. Retry MUST address that exact incident/attempt and preserve earlier failures.
- **FR-011**: Operators MUST navigate between a BPMN service, native worker and ETL run/partitions using authorized views. Business error, technical fault, cancellation and success remain distinct.
- **FR-012**: Interruption, cancellation, late results and recovery MUST preserve accepted outcomes. Studio MUST distinguish semantic completion, pending downstream settlement and settled completion.
- **FR-013**: Actions MUST reflect current authorization, current work and backend capabilities. Read-only/source-owned content and absent capabilities MUST not expose an effective write action; refusals/unavailability need an understandable explanation.
- **FR-014**: Changing backend, identity, tenant or selection MUST prevent an old request/result from being applied to the new scope. Links/back navigation MUST preserve the intended backend and exact run.
- **FR-015**: Inspection/recovery of accepted work MUST remain usable in the qualified runtime-only/admission-off composition. Unavailable authoring is distinct from execution failure.
- **FR-016**: Existing native/embedded authoring, exported XML/BPMNDI, saved state, immutable selections and composite outcomes MUST remain compatible. Any persisted-writer change MUST follow actual checkpoints from its prior writer.
- **FR-017**: Direct and embedded paths MUST retain the same released semantics engine and equivalent normalized traces for the supported shared profile. UI affordances MUST NOT implement competing semantics.
- **FR-018**: Studio MUST preserve coherent dirty-state/save and undo/redo, useful validation/selection, keyboard/focus access and readable themes/responsive layouts. The current editor stays available; alternative-editor adoption is outside this feature.
- **FR-019**: Real host/browser failure/recovery evidence MUST prove journeys at their declared source, with physical destinations and whole saved stores where claimed. Library tests, synthetic state, baseline passes or rendering alone are insufficient.
- **FR-020**: Issue/PR/Project62 statuses MUST follow their own evidence. This feature MUST NOT close wider ETL connector/provider, agent or program acceptance.
- **FR-021**: Operational documentation MUST declare authorization, claim/attempt ownership, replay/conflict behavior, interruption and external work. Full task-management/operator-console functionality, arbitrary token alteration and live migration remain deferred.

### Key Entities

- **BPMN process**: Authored identity, supported structure, work bindings and presentation.
- **Published process**: Immutable executable version and exact dependency selections.
- **Execution**: Durable run, accepted operations, lifecycle and remaining settlement responsibility.
- **Human work/claim**: Current invocation/attempt and authorized completion ownership.
- **Service attempt/incident**: Physical native/ETL attempt, logical invocation, retained failure and bounded retry eligibility.
- **ETL selection/run**: Published pipeline and its partitions, progress, physical effects and outcomes.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A real Studio user completes open/import → configure → save/reopen → publish → direct start → human wait → host restart → discover/reopen → complete → inspect completion. Repetition after a second restart gives one completion/terminal result, without a script-supplied execution identity. Explicit claim contention is separately proved by SC-003.
- **SC-002**: A real BPMN-to-ETL journey using the existing two-partition finite pipeline survives a held dependency failure/restart, retries through Studio and completes. Retain the old failure, one replacement per accepted retry, exact pipeline selection, physical destination observations and one parent continuation; repeated external effects remain explicitly at least once.
- **SC-003**: Human-claim, completion/interruption and retry/cancellation races each demonstrate both relevant arrival orders. No stale/unauthorized action advances execution; repeated accepted operations create no additional attempt or continuation.
- **SC-004**: Both actual frozen saved-Studio families complete their compatibility journey; all original authoring/checkpoint assets stay unchanged. Affected composite outcomes and direct/embedded shared-profile traces retain established behavior.
- **SC-005**: Actual unavailable-feature, unauthorized/cross-scope, read-only/source-owned and runtime-only cases show the declared visible behavior, including backend switching during a pending operation. No old-scope result appears as successful new-scope work.
- **SC-006**: Every #2907 criterion maps to a demonstrated result or existing owner deferral before closure. Exact reviewed-head gates and resulting-main verification pass; statuses distinguish passed, failed, skipped, cancelled and incomplete work.

## Assumptions

- Delivered #2735/#2898 semantics/hosting and finite file/HTTP ETL are prerequisites, not work to reimplement. Their evidence retains its source and limits.
- Existing identity/tenant/resource policies govern access. No team-assignment policy, general inbox/product, new remote-worker transport or durable task runner is selected. Exact human claiming still needs implementation or demonstration; completion alone does not prove it.
- The first external-work journey uses the existing native worker/RunEtl path. Wider connectors, continuous ETL and all-provider production recovery remain #2908 obligations.
- The initial accepted host composition is #2898's qualified built-in profile. Provider contract tests do not establish new distributed placement or all-provider Studio recovery.
- [#3597](https://github.com/elsa-workflows/elsa-foundation/issues/3597) owns optional bpmn-js feasibility/same-process switching. This feature retains process identity/configuration without selecting that editor.
- Native authoring and saved execution compatibility remain required. Unratified constitution sections are not ratified by this feature.
- This draft adds no owner decision or permission. Standing delivery/review authority applies; an actual product-policy choice is presented individually if source inspection finds no reasonable existing rule.

## Acceptance traceability

| #2907 obligation | Delivered prerequisite | Remaining acceptance here |
|---|---|---|
| Existing released semantics | #2735 / PR3335,3338,3409 | Preserve the released engine/frozen contracts. |
| Supported profile, XML/BPMNDI and operations | #2898 / spec1017 profile and real hosts | User-visible profile, direct authoring/publication, exact claims/commands and current documentation. |
| Service/human/interruption/recovery | #2898 real command/race/restart witnesses | Integrated Studio SC-001–003, including held ETL failure. |
| Direct/embedded equivalence and native outcomes | #2898 paired traces/composites/prior-writer recovery | Preserve/requalify FR-016/017; no wrapper substitute for SC-001. |
| Claims, external work, correlation/timers, incidents/retry/cancel/inspection | Durable native/ETL work and #2898 operations | Close exact human-claim gap; expose/verify supported authorization, replay and settlement. |
| Real integration followed by Studio | #2898 plus existing embedded Studio compatibility | Direct-model SC-001–005 and resulting-main verification; no neutral-contract ratification. |
