# Feature Specification: Native durable background activities

**Feature Branch**: `agent/codex/2914-native-durable-offload`

**Created**: 2026-10-17

**Status**: Approved

**Program**: [Unified execution foundation #2892](https://github.com/elsa-workflows/elsa-foundation/issues/2892) → [Production adoption #2905](https://github.com/elsa-workflows/elsa-foundation/issues/2905) → [Native proof #2914](https://github.com/elsa-workflows/elsa-foundation/issues/2914).

**Input**: Specify and prove one production-native workflow with parallel background activities, durable recovery, explicit cancellation and exactly one logical parent continuation, using Elsa's existing EF persistence.

## User Scenarios & Testing

### User Story 1 - Run independent activities concurrently (Priority: P1)

A workflow author configures two ordinary typed leaf activities for background execution inside a native Flowchart, itself inside a Sequence. Their activity implementations remain unaware of scheduling infrastructure. Flowchart owns its join; Sequence owns the next step. Independent workers perform the two calls concurrently while the workflow coordinator remains available to process events and cancellation.

**Why this priority**: This proves the production boundary between serialized orchestration and concurrent work without replacing native composition semantics.

**Independent Test**: Publish and run a real native Sequence → Flowchart → final activity. Two independently executing workers report entry to a barrier before either may finish. Release them in each order; the final activity executes once after both results.

**Acceptance Scenarios**:

1. **Given** an eligible activity configured for background execution, **when** it becomes runnable, **then** the runtime records owed work durably before executing its body, yields the workflow drain and preserves the pinned input/contract.
2. **Given** two parallel branches, **when** separate workers claim them, **then** both bodies may execute concurrently and neither may write coordinator state directly.
3. **Given** successful results arriving in either order or repeatedly, **when** the coordinator applies them, **then** each result becomes visible once to native completion semantics and the join advances the Sequence exactly once.
4. **Given** an inline or previously published activity, **when** this feature is installed, **then** its existing behavior is unchanged unless it was explicitly configured for background execution.
5. **Given** an unsupported background activity, **when** a workflow is published or an invalid pinned artifact reaches runtime, **then** it is rejected with a diagnostic before its body runs. Unsupported transitions also become explicit faults, never silent completion.

### User Story 2 - Recover admitted work and committed results (Priority: P1)

An operator restarts a coordinator or loses a worker without losing admitted activity work or replaying an already applied result into the parent.

**Why this priority**: Background work is a durable promise; a happy-path thread-pool example is insufficient.

**Independent Test**: Recreate providers and kill owned processes against the same file-backed EF database at admission, claim, result-commit and result-application boundaries.

**Acceptance Scenarios**:

1. **Given** a failure before the admission checkpoint commits, **when** storage is reopened, **then** neither a parked activity nor orphaned owed work remains committed.
2. **Given** a committed admission and a lost wakeup or process crash, **when** a worker scans durable work, **then** it can execute the admitted item without the original coordinator process.
3. **Given** an expired worker claim, **when** a successor reclaims the item, **then** the predecessor cannot renew, complete or overwrite the successor's result with its old claim token.
4. **Given** a committed worker result and a crash before notification or parent application, **when** the coordinator recovers, **then** it applies that same result without rerunning the activity body.
5. **Given** repeated or conflicting result submissions, **when** storage handles them, **then** exact retries are idempotent and conflicting or stale submissions cannot change the accepted result.

### User Story 3 - Cancel work explicitly (Priority: P1)

An operator cancels a workflow or an activity subtree while background work is pending or executing. The cancellation decision becomes durable and dominates later worker delivery; stopping a host is not itself a workflow cancellation.

**Why this priority**: Cancellation and worker loss have different business meanings and must not be inferred from a process token.

**Independent Test**: Cancel before claim, during held work, between the two results, after result persistence and after workflow completion; replay late results across restart.

**Acceptance Scenarios**:

1. **Given** a pending item, **when** its workflow or activity scope is cancelled, **then** it cannot subsequently be claimed for execution.
2. **Given** an active worker, **when** cancellation commits, **then** cooperative cancellation is signalled; an uncooperative worker cannot reopen the activity or make the cancelled parent advance.
3. **Given** successful completion racing cancellation, **when** the durable operations serialize, **then** the committed order determines the outcome and subsequent duplicates cannot revise it.
4. **Given** an already completed workflow, **when** cancellation arrives, **then** it does not rewrite successful history.
5. **Given** a host shutdown or worker loss without explicit cancellation, **when** work is reclaimed, **then** it remains owed rather than becoming a business cancellation.

### User Story 4 - Diagnose and constrain execution (Priority: P2)

An operator can identify pending, claimed, completed, cancelled and failed work, its owning invocation, its pinned artifact and its attempts. Workers cannot cross persistence/tenant boundaries or substitute incompatible handlers.

**Independent Test**: Inspect persisted rows and native runtime state for completion, returned fault, exception, cancellation and invalid scope/pin cases. Exercise the production host and document the operational controls.

**Acceptance Scenarios**:

1. **Given** a mismatched tenant/shell, artifact, activity contract, invocation or claim token, **when** a claim/result is attempted, **then** it is rejected without executing or advancing the wrong invocation.
2. **Given** an activity fault or disposal failure, **when** its result is accepted, **then** native incident and parent fault handling apply, with sensitive input masking preserved.
3. **Given** outstanding work, **when** workers are disabled or a host is rolled back, **then** the work remains inspectable and owed. Removing support for a pinned background artifact requires draining/cancelling its runs or retaining compatible workers.

### Edge Cases

- Lost transaction responses, duplicate queue delivery and worker lease expiry during a body call.
- Cancellation between read and claim, between result commit and parent application, and concurrent with subtree completion.
- Worker death after an external effect but before the result commit: invocation can repeat and must use a stable effect idempotency key.
- Non-cooperative activities, oversized/unserializable results, unavailable pinned types and unsupported suspension/structural transitions.
- Two coordinator attempts applying the same receipt, including late redelivery after the scheduler queue item was consumed.
- Explicit cancellation of one subtree must not cancel unrelated branches or another workflow with a similar activity identifier.

## Requirements

### Functional Requirements

- **FR-001**: Authors MUST opt an eligible ordinary typed leaf activity into background execution without writing scheduling code in that activity. Inline MUST remain the default.
- **FR-002**: Native composition components MUST retain ordering, routing and join semantics. The workflow instance MUST retain a single serialized state writer.
- **FR-003**: Admission MUST atomically persist the waiting/running invocation state and owed work using the existing EF checkpoint transaction and workflow fence.
- **FR-004**: Independent workers MUST claim durable items with expiring ownership and fencing. Lease expiry alone MUST reject renewal and completion, even before a successor claims. Each concurrent call MUST have an independent dependency-injection/persistence scope.
- **FR-005**: Each item MUST bind the workflow, activity invocation, attempt, immutable input snapshot, artifact identity and contract pin. Workers MUST validate these before execution.
- **FR-006**: A worker MUST durably record its terminal result under its current claim fence before signalling the coordinator. Notification loss MUST be recoverable from durable state.
- **FR-007**: Only the serialized coordinator MAY apply results to workflow/activity state, output captures, incidents and native continuation. The accepted receipt identity, activity/output/incident changes and continuation intent MUST commit atomically in the existing fenced checkpoint. Repeated/conflicting deliveries MUST NOT cause multiple logical completions or continuations, including downstream late completion-message replay.
- **FR-008**: Explicit workflow/subtree cancellation MUST retire pending work and invalidate running/late results within the same durable cancellation boundary. Worker cancellation signalling MUST be cooperative, while logical cancellation MUST not depend on cooperation.
- **FR-009**: Shutdown cancellation MUST NOT manufacture a business Cancel transition; abandoned claims MUST be reclaimable.
- **FR-010**: Complete, Fault, thrown exceptions and activity-returned Cancel MUST have documented native behavior. Unsupported activity forms/transitions MUST fail visibly.
- **FR-011**: Existing inline artifacts and schemas MUST retain compatibility. The opt-in feature MUST provide registration, diagnostics and a documented migration/rollback procedure.
- **FR-012**: Implementation MUST include real EF provider tests and a rebuilt Workbench REST journey with process restart. Deterministic barriers MUST prove overlap; elapsed-time thresholds MUST NOT stand in for concurrency proof.
- **FR-013**: Meaningful negative controls MUST detect disabled receipt deduplication/fencing, cancellation suppression and pin/scope validation and downstream native completion replay guards. Competing coordinator fences MUST prevent two owners from applying a result.
- **FR-014**: The implementation MUST use provider-neutral contracts and the existing first-party EF persistence family. A test-only raw SQLite implementation, child-workflow substitution, or standalone simulation MUST NOT count as this production proof.

### Key Entities

- **Background work item**: One durable promise to execute a pinned activity invocation; logical identity survives retries.
- **Worker claim**: Temporary authority to execute and commit a result, identified by owner, fencing token and expiry.
- **Result receipt**: Immutable successful/faulted/cancelled outcome accepted for an authorized claim and correlated to one native invocation.
- **Coordinator application**: Durable recording of that receipt into native state with any resulting continuation in the same checkpoint boundary.

## Success Criteria

- **SC-001**: The same published native workflow passes both completion orders with two bodies simultaneously held before completion; its final step has one logical execution.
- **SC-002**: Every specified crash boundary recovers from the same EF database with no lost admitted work or duplicate accepted result/continuation.
- **SC-003**: Every cancellation race leaves a stable terminal decision after restart and late delivery; no cancelled scope is resurrected.
- **SC-004**: Wrong scope/pin, stale claims and conflicting receipts fail without changing the legitimate invocation, and relevant mutations make these tests fail.
- **SC-005**: Focused existing runtime/composition/EF suites, architecture and map checks are recorded against the implementation, with unrelated baseline failures distinguished. A real Workbench journey and independent review are recorded in #2914.

## Assumptions and Scope

- All P1 stories are required before this goal is complete; the first happy-path increment is not the final deliverable.
- Physical invocation is at-least-once after worker loss. Arbitrary external effects are not exactly-once unless the activity's external system honors its stable idempotency key. The guarantee is one accepted result and one logical parent continuation.
- Initial eligibility requires an explicitly declared, fingerprinted background-safe capability, with ordinary typed leaf execution with immutable inputs and a serializable terminal transition. Structural/stateful/checkpoint-participating/intrinsic activities and runtime-staging-dependent activities need explicit supported contracts before offload.
- No new actor framework, universal execution-model API, ETL product, BPMN migration, live agent service, designer redesign or deployment is part of this slice.
- Existing [ADR 0031](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) must distinguish serialized orchestration from parallel background compute. [ADR 0073](../../docs/adr/0073-ef-core-is-the-only-first-party-persistence-family.md) and [ADR 0078](../../docs/adr/0078-workflow-executions-are-virtual-actors-and-cluster-membership-is-a-foundation-contract.md) remain applicable. Constitution §E2.9 is provisional and is not ratified by this feature.
