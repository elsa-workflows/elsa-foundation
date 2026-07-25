# Feature Specification: Extensible Incident Strategies

**Feature Branch**: `codex/1015-incident-strategies`

**Created**: 2026-07-24

**Status**: Approved

**Input**: User description: "Fix GitHub issue #1015 end to end with Elsa 3 parity for automatic incident handling, while replacing finite resolution actions with extensible decision objects."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Choose automatic fault handling (Priority: P1)

As a workflow author, I can select a versioned incident strategy or inherit the publishing host's default so that activity faults are handled automatically and predictably by the published workflow.

**Why this priority**: This closes the issue's central gap: the authored incident-strategy option currently has no publication or runtime effect.

**Independent Test**: Publish equivalent workflows with `Fault/1` and `ContinueWithIncidents/1`, execute an activity fault in each, and verify their distinct workflow and incident results.

**Acceptance Scenarios**:

1. **Given** a workflow explicitly selecting `Fault/1`, **when** an unabsorbed activity fault remains blocking, **then** the workflow faults and the incident records a `FaultWorkflow` outcome.
2. **Given** a workflow explicitly selecting `ContinueWithIncidents/1`, **when** an unabsorbed activity fault remains blocking, **then** the failed activity remains faulted, its incident becomes open, fault propagation stops, and already-scheduled independent work may continue.
3. **Given** a workflow with no authored selection, **when** it is published, **then** the publishing host's exact default strategy reference is pinned into the executable.
4. **Given** an authored strategy reference that is unknown, **when** publication is attempted, **then** publication fails rather than silently selecting another strategy.

---

### User Story 2 - Contribute custom strategies and decisions (Priority: P2)

As an extension developer, I can register a discoverable strategy that returns an executable incident-resolution action so that packages can add behavior without extending a framework-owned action enum.

**Why this priority**: Third-party extensibility is a required architectural property of the replacement model.

**Independent Test**: Register an attributed custom strategy and namespaced custom action, discover it without constructing the strategy, publish a workflow selecting it, and verify its guarded state changes and post-commit intent.

**Acceptance Scenarios**:

1. **Given** a custom strategy registered with an explicit descriptor, **when** strategy discovery is requested, **then** its stable alias, exact version, display name, and description are returned.
2. **Given** an attributed custom strategy registered through the reflection overload, **when** the host activates, **then** the same descriptor and implementation are contributed atomically.
3. **Given** a custom strategy action, **when** it executes successfully, **then** only its target incident, containing workflow, permitted outcome metadata, and registered post-commit intents can be staged.
4. **Given** an attempted custom use of a reserved Elsa action kind or structural absorption/suppression operation, **when** the action is validated, **then** it is rejected.

---

### User Story 3 - Inspect trustworthy incident outcomes (Priority: P3)

As an operator, I can distinguish the execution fault from the durable incident and see which strategy or system flow handled the incident without exposing executable types or sensitive exception material.

**Why this priority**: Reliable provenance and safe failure behavior make automatic handling diagnosable in production.

**Independent Test**: Exercise strategy handling, structural absorption, subtree suppression, activation failure, missing deployment, and strategy failure; verify each incident's status, immutable outcome, source, and safe metadata.

**Acceptance Scenarios**:

1. **Given** a structural parent that absorbs a child fault, **when** propagation completes, **then** the incident is resolved with system absorption provenance while the child remains faulted.
2. **Given** cancellation that makes an incident's execution scope irrelevant, **when** subtree cleanup completes, **then** the incident is suppressed with cancellation provenance rather than described as handled.
3. **Given** a selected strategy or action that fails, **when** resolution is attempted, **then** staged changes are discarded and a fresh runtime-owned `FaultWorkflow` fallback records safe provenance.
4. **Given** a pinned strategy implementation missing at activation, **when** the workflow is prepared to start, **then** it remains pending with a blocking `WaitForIntervention` incident and no automatic retry.

### Edge Cases

- Multiple eligible blocking incidents exist at the same end-of-drain boundary.
- A parent absorbs a fault before ordinary strategy evaluation.
- Cancellation suppresses an incident before ordinary strategy evaluation.
- A strategy returns null despite its non-null contract.
- A strategy throws, an action throws, or the emergency fallback itself cannot commit.
- Host shutdown or fencing cancellation interrupts strategy evaluation.
- A checkpoint fails after strategy/action code ran but before staged resolution became durable.
- A post-commit intent delivery is retried after the resolution checkpoint committed.
- An explicit authored reference or configured host default identifies an unknown strategy.
- A published executable is deployed to a runtime missing its exact pinned strategy version.
- Two strategy registrations differ only by alias casing.
- A third-party action attempts to use a reserved bare action kind.
- An activity-activation failure or poisoned scheduler item produces an incident outside ordinary activity-fault strategy handling.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The authored workflow strategy options MUST replace the incident strategy type string with an optional exact incident strategy reference containing a stable alias and opaque version.
- **FR-002**: An absent authored reference MUST mean "inherit the publishing host default" and MUST remain absent in Design state.
- **FR-003**: Publication MUST resolve authored reference, then host default, then `Fault/1`, and pin the exact effective reference into the executable.
- **FR-004**: The effective strategy reference MUST participate in executable behavioral identity so behaviorally distinct selections cannot share an identity.
- **FR-005**: An explicit unknown authored reference MUST fail publication, and an unknown configured host default MUST fail host activation.
- **FR-006**: Strategy aliases MUST compare ordinal case-insensitively while preserving canonical descriptor spelling; built-in bare aliases are reserved and third-party aliases MUST be dotted and namespaced.
- **FR-007**: Strategy versions MUST be nonblank opaque tokens selected exactly, with no automatic ordering or latest-version inference.
- **FR-008**: The foundation MUST always contribute `Fault/1` and `ContinueWithIncidents/1`; the default MUST be `Fault/1` when the host configures nothing.
- **FR-009**: At runtime, ordinary strategy evaluation MUST resolve only the executable's exact pinned strategy reference, never the authored options or the publishing host's current default, and MUST occur only for activity-fault incidents that remain blocking and have no resolution outcome after structural parent propagation and absorption have completed.
- **FR-010**: `Fault/1` MUST keep the incident blocking, record `FaultWorkflow`, and place the workflow in its terminal faulted state.
- **FR-011**: `ContinueWithIncidents/1` MUST preserve the activity fault, make the incident open, stop that fault's propagation, avoid synthesizing successful outcomes, and allow already-scheduled independent work to continue.
- **FR-012**: Activation failures, poisoned scheduler work, structural absorption, subtree suppression, and missing pinned strategy implementations MUST bypass ordinary strategy selection and record their system resolution source; specifically, a missing pinned implementation before workflow start MUST create or retain a blocking incident with an immutable `WaitForIntervention` outcome sourced from `MissingStrategyImplementation`, leave the workflow pending, and schedule no retry.
- **FR-013**: A strategy MUST receive only a durable policy-safe incident, activity, workflow, executable, and pinned-strategy snapshot; it MUST NOT receive raw exceptions, unrestricted messages, variables, payloads, private state, or mutable checkpoint services.
- **FR-014**: A strategy MUST expose the cancellable contract `ResolveAsync(IncidentStrategyContext, CancellationToken)` and return exactly one non-null incident-resolution action; deliberate deferral MUST use `WaitForIntervention`.
- **FR-015**: Incident-resolution actions MUST be runtime objects executed directly by the runtime and MUST expose a stable action kind used only for durable classification.
- **FR-016**: The runtime MUST NOT persist executable action objects, CLR type names, or use action kind as an executable dispatch discriminator.
- **FR-017**: Public custom action kinds MUST be dotted and namespaced; Elsa's built-in bare action kinds MUST be reserved; action kinds MUST be validated and persisted using exact ordinal spelling, with no action registry or alias canonicalization step.
- **FR-018**: The public action context MUST allow only target-incident blocking/open/resolved transitions, workflow fault requests, bounded safe outcome metadata, and explicitly registered strategy-safe post-commit intents; core scheduler, dispatch, retry, and other runtime-control intent kinds MUST NOT be available through this context.
- **FR-019**: The public action context MUST NOT allow activity-state mutation, workflow completion/cancellation/suspension/retry, structural absorption, structural suppression, arbitrary checkpoint access, or mutation of another incident.
- **FR-020**: Structural absorption and suppression actions and their staging operations MUST remain runtime-internal, while custom actions may resolve an incident under their own namespaced semantics.
- **FR-021**: Strategy and action authors MUST honor an externally side-effect-free and replay-safe extension contract; the runtime MUST enforce the available mutation and intent capabilities, and permitted external work MUST be recorded through the existing durable post-commit intent mechanism.
- **FR-022**: The action context MUST generate deterministic intent identity, idempotency, timestamp, and workflow/activity correlation from the resolution context rather than accepting those values from custom code.
- **FR-023**: One resolution batch MUST run once per successful outer workflow drain, after scheduler work and in-drain post-commit deliveries reach causal quiescence, and consist of every durable incident for that workflow that is blocking, has ordinary activity-fault origin, and has no outcome at that boundary, including eligible blockers retained from an earlier aborted pass; the batch MUST be evaluated sequentially in stable incident-ID order, all successful or fallback action effects MUST be staged, and exactly one checkpoint MUST commit the batch atomically.
- **FR-024**: A strategy null return, strategy exception, or action exception MUST discard that action's staging and execute a fresh runtime-owned `FaultWorkflow` fallback without recursively invoking strategy handling.
- **FR-025**: Runtime-requested cancellation MUST abort the resolution checkpoint without recording a strategy failure; a cancellation exception without a cancelled supplied token MUST follow ordinary failure fallback.
- **FR-026**: If the emergency fallback or its checkpoint fails, the durable blocking incident with absent outcome MUST remain authoritative and normal scheduler retry/poisoning behavior MUST apply.
- **FR-027**: A successful action effect and incident resolution outcome MUST commit atomically; evaluation may repeat before commit, but a committed non-null outcome MUST prevent reevaluation.
- **FR-028**: The old finite incident resolution action enum and its `None`, `Continue`, `Retry`, `SuspendWorkflow`, `FaultWorkflow`, and `WaitForIntervention` values MUST be removed without a migration or dual-read path.
- **FR-029**: Incident state and inspection projections MUST replace the old enum property with an optional immutable resolution outcome containing action kind, application time, optional strategy reference, optional system source, and safe metadata.
- **FR-030**: Strategy and system provenance MAY coexist in one outcome when the runtime substitutes a fallback for a selected strategy.
- **FR-031**: Initial stable system sources MUST cover structural fault absorption, subtree cancellation, activity activation failure, poisoned scheduler work, missing strategy implementation, and incident strategy failure.
- **FR-032**: Incident status MUST remain the closed lifecycle `Open`, `Blocking`, `Resolved`, and `Suppressed`; custom actions MUST NOT add statuses.
- **FR-033**: `Resolved` and `Suppressed` MUST be terminal incident states with `ResolvedAt`; resolution outcomes MUST be immutable after first commit.
- **FR-034**: `WaitForIntervention` MUST keep the incident blocking, preserve the workflow's existing lifecycle state, stop causally dependent work, and schedule no retry.
- **FR-035**: The system MUST expose a permission-protected read-only Workflow Publishing API that lists descriptors and the exact effective publishing default strategy reference, returning `Fault/1` when no host default is configured.
- **FR-036**: Strategy discovery MUST be advertised through the publishing API capability surface and MUST construct no strategy implementations.
- **FR-037**: Discovery MUST return exact descriptor alias/version, display name, and optional description in deterministic order.
- **FR-038**: Strategy registration MUST contribute descriptor and implementation atomically, support an explicit-descriptor form and an attributed reflection form, and fail deterministically on duplicate identity.
- **FR-039**: Reflected registration MUST require explicit alias/version metadata, MUST NOT derive durable identity from the CLR type name, and MAY derive presentation metadata from type metadata or a humanized type name.
- **FR-040**: The incident strategy reference MUST be a dependency-free workflow primitive reusable by Design and Runtime without introducing a Runtime-to-Design dependency.
- **FR-041**: Strategy interfaces, actions, contexts, registry contracts, and durable outcome models MUST be public extension contracts in the Runtime domain; built-in implementations and orchestration MUST remain in the default Runtime implementation.
- **FR-042**: Existing fault absorption and subtree suppression behavior MUST retain its execution semantics while adopting the new system action/outcome representation.
- **FR-043**: No incident resolution, retry, or mutation REST endpoint and no built-in retry or suspension strategy/action MUST be added in this work unit.
- **FR-044**: The clean-break persistence and API shape MUST be reflected in all supported persistence providers, inspection endpoints, fixtures, and end-to-end assertions.
- **FR-045**: The ordinary incident-resolution batch MUST run after the outer drain's structural propagation and absorption hops and before terminal blocking-incident fault policy; the existing terminal observer MUST be replaced or made outcome-aware so `ContinueWithIncidents/1` can open the incident, `WaitForIntervention` can remain blocking without terminalizing the workflow, and already-scheduled independent work can finish draining.

### Key Entities

- **Incident strategy reference**: Exact immutable identity `{ alias, version }` selected by authors or host policy and pinned into an executable.
- **Incident strategy descriptor**: Safe discovery metadata `{ alias, version, display name, optional description }` contributed atomically with an implementation.
- **Incident strategy**: Host-contributed policy that chooses one executable incident-resolution action from a policy-safe snapshot.
- **Incident-resolution action**: Runtime-only extensible result object that stages permitted resolution effects and exposes a stable durable classification kind.
- **Incident-resolution outcome**: Immutable durable evidence of an applied action, including action kind, application time, strategy and/or system provenance, and safe metadata.
- **Incident**: Durable operational issue record with a closed status lifecycle, distinct from the activity fault or operational anomaly that caused it.
- **Post-commit intent**: Existing durable, idempotently deliverable external-work request committed atomically with runtime state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Automated end-to-end tests demonstrate both built-in strategies producing their specified distinct results from the same activity-fault scenario.
- **SC-002**: A third-party attributed strategy and custom action can be registered, discovered, published, executed, and inspected without adding or modifying a framework enum.
- **SC-003**: Every accepted failure branch—unknown publication reference, missing deployment, null return, strategy exception, action exception, cancellation, fallback failure, activation failure, and poisoned work—has a deterministic automated assertion.
- **SC-004**: For a batch containing at least two eligible incidents, automated tests prove stable evaluation order and one atomic resolution checkpoint.
- **SC-005**: Crash/replay tests prove that a committed outcome and its post-commit intent are not duplicated.
- **SC-006**: Contract and fixture searches contain no remaining use of the removed incident resolution action enum or its obsolete retry/suspension values.
- **SC-007**: Runtime project-reference checks prove that no Runtime project gains a dependency on a Design project.
- **SC-008**: Discovery endpoint tests prove read permission enforcement, capability advertisement, deterministic output, exact default reference, and zero strategy construction.
- **SC-009**: Existing structural child-fault absorption, cancellation suppression, ordinary fault handling, persistence querying, and supported full-solution test suites remain green.

## Assumptions

- Elsa Foundation is unreleased, so the agreed persistence and API clean break requires no compatibility migration or dual-read period.
- Elsa 3 parity means only the `Fault` and `ContinueWithIncidents` strategy behaviors, automatic post-fault application, default precedence, and read-only discovery; Elsa 3 does not define retry/suspension incident strategies or an incident mutation endpoint.
- Strategy-specific per-workflow option payloads are outside this work unit; host configuration remains available through constructor dependencies.
- Operator recovery and incident mutation are separate future work; this work records `WaitForIntervention` truthfully without inventing that surface.
- The current constitutions are draft/provisional; this work applies their gates without ratifying or amending them.
