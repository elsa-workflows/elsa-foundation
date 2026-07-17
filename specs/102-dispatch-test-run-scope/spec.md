# Feature Specification: Preserve Dispatch Test-Run Scope

**Feature Branch**: `codex/dispatch-workflow-program`

**Created**: 2026-07-16

**Status**: Draft

**Input**: GitHub issue #682, "Preserve test-run dispatch scope", including its complete current body and zero comments, under parent #674.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Test Parents Run Published Children (Priority: P1)

As a workflow author testing a draft parent, I want every DispatchWorkflow child to execute the exact Published child artifact selected by the parent so that testing a parent draft never substitutes an unpublished child draft.

**Why this priority**: Artifact identity is the safety boundary between parent draft testing and independently published child behavior.

**Independent Test**: Compile a draft parent whose DispatchWorkflow target has distinct draft and Published child behavior, run the parent as a test run, and verify that the child uses the retained Published artifact and that both parent and child are classified as test runs.

**Acceptance Scenarios**:

1. **Given** a draft parent test run whose selected child has different draft and Published artifacts, **when** DispatchWorkflow starts the child, **then** the child executes the exact retained Published artifact rather than the child draft.
2. **Given** a test-run parent, **when** its child is admitted, **then** the child inherits `TestRun` run kind and that classification is visible through runtime lifecycle inspection.
3. **Given** a production parent, **when** its child is admitted, **then** the child inherits the parent's production run kind with no test-scope association.

---

### User Story 2 - Detached Children Live Until Scope Teardown (Priority: P2)

As a workflow author using detached dispatch in a test run, I want the child to remain independent of ordinary parent completion but to be cancelled when the enclosing test-run scope expires or is explicitly torn down so that background test work cannot leak beyond the test session.

**Why this priority**: Detached behavior must remain genuinely detached while still being bounded by the test-run lifetime.

**Independent Test**: Complete a test-run parent after it starts a detached child, verify the child remains live, then close the test-run scope and verify exactly one logical cancellation converges before and after child materialization.

**Acceptance Scenarios**:

1. **Given** a detached test child, **when** the parent test workflow completes normally, **then** the child is not cancelled merely because the parent completed.
2. **Given** a still-live detached child, **when** its enclosing test-run scope expires, **then** cleanup idempotently cancels the child.
3. **Given** a still-live detached child, **when** its enclosing test-run scope is explicitly torn down, **then** cleanup has the same durable cancellation outcome as expiry.
4. **Given** repeated or concurrent cleanup attempts for one scope, **when** they race before or after child admission, **then** they converge on one logical cancellation outcome without reviving or duplicating the child.

---

### User Story 3 - Waited Test Dispatch Preserves Production Semantics and Isolation (Priority: P3)

As a workflow author testing waited child dispatch, I want completion, fault, cancellation, and delivery-failure behavior to match production while scope cleanup remains strictly isolated so that test results are trustworthy and safe.

**Why this priority**: Test runs are useful only when their observable child outcomes match production and cleanup cannot cross security or execution boundaries.

**Independent Test**: Exercise waited test children through success, fault, cancellation, and delivery failure, then run hostile cleanup requests across tenant, partition, scope, and production boundaries and verify only eligible children in the selected scope are affected.

**Acceptance Scenarios**:

1. **Given** a waited child in a test run, **when** it completes, faults, is cancelled, or cannot be delivered, **then** the parent observes the same normal outcome, result, diagnostics, and resume behavior as a production parent.
2. **Given** an unrelated production child, **when** test-scope cleanup runs, **then** the production child remains unchanged.
3. **Given** a test child from another tenant, partition, or test-run scope, **when** cleanup targets one scope, **then** the unrelated child remains unchanged.
4. **Given** a durable provider restart during scope teardown before or after child materialization, **when** cleanup resumes, **then** every eligible live child converges to cancellation and terminal children remain unchanged.

### Edge Cases

- Scope teardown races with a child-start claim before durable child admission.
- Scope teardown races after admission but before child execution state becomes visible.
- The parent completes before the detached child starts, while the scope remains open.
- A child reaches any terminal state while cleanup is selecting or cancelling it.
- Cleanup is retried after commit but before acknowledgement, or two cleaners claim the same scope concurrently.
- A test-run scope contains multiple generations of nested DispatchWorkflow children.
- A legacy execution has `TestRun` run kind but no durable test-scope identity.
- A cleanup request supplies the right scope ID with the wrong tenant or partition.
- The selected scope has no children or only terminal children.
- The scope expires at exactly the cleanup observation time.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: A DispatchWorkflow executed by a draft parent test run MUST use the exact retained Published child artifact and provenance selected when the parent artifact was compiled; it MUST NOT resolve or execute a child draft.
- **FR-002**: A child dispatch MUST inherit the parent's exact `WorkflowRunKind` value unchanged: `TestRun` for test execution and `PublishedRun` for production execution, while `BackgroundWeaverRun` and legacy `Unknown` remain compatible regressions.
- **FR-003**: Parent and child run kind MUST remain durable across checkpoint replay, restart, and inspection.
- **FR-004**: Every newly started root test run MUST have one nonblank durable test-scope identity and a finite expiry time.
- **FR-005**: Every DispatchWorkflow child created beneath a test-run execution MUST inherit the same immutable test-scope identity and expiry, including nested descendants.
- **FR-006**: Production and non-test executions MUST NOT acquire a test-scope identity.
- **FR-007**: A detached test child MUST remain live after ordinary parent completion while its test-run scope remains open.
- **FR-008**: Ordinary parent completion MUST NOT be interpreted as test-scope teardown.
- **FR-009**: Scope expiry and explicit teardown MUST use the same durable, idempotent closing transition.
- **FR-010**: Closing a test-run scope MUST select every still-live detached DispatchWorkflow child belonging to that exact scope, including detached descendants whose immediate parent is already terminal.
- **FR-011**: Scope cleanup and child admission MUST be mutually exclusive provider-atomic transitions: cleanup winning a Pending detached dispatch MUST make every already-claimed or replayed start return without materializing a child; admission winning MUST expose Started state and atomically receive durable cancellation responsibility.
- **FR-012**: Scope cleanup MUST leave completed, faulted, cancelled, and final delivery-failed children terminal and unchanged.
- **FR-013**: Direct scope cleanup cancellation MUST apply only to detached test children. Waited test children MUST retain the production parent-cancellation policy selected by `CancelChildOnParentCancellation` and MUST NOT be reclassified as detached cleanup targets.
- **FR-014**: Repeated, concurrent, and post-commit/pre-ack cleanup attempts MUST converge without duplicate logical cancellation, state regression, or child revival.
- **FR-015**: Cleanup MUST be scoped by immutable test-scope identity, tenant, and partition and MUST fail closed on conflicting scope context.
- **FR-016**: Cleanup MUST NOT cancel production children, test children in another scope, tenant, or partition, or executions lacking authoritative test-scope membership.
- **FR-017**: Waited test children MUST retain the exact completion, fault, cancellation, delivery-failure, safe-output, resume, and parent outcome behavior established for production dispatch.
- **FR-018**: Test-scope membership and cleanup state MUST survive provider restart and MUST be queryable without loading unrelated tenants, partitions, or complete execution history.
- **FR-019**: Durable provider operations MUST cover scope closing, child selection, pre-admission cancellation, admitted-child cancellation responsibility, fencing or equivalent ownership, and replay-safe progress.
- **FR-020**: In-memory behavior MUST preserve the same logical outcomes while remaining explicitly non-process-crash-durable.
- **FR-021**: Groundwork restart and race tests MUST cover teardown before child materialization, teardown after child materialization, retry after response loss, concurrent cleaners, and terminal-child races.
- **FR-022**: Runtime lifecycle inspection MUST expose inherited run kind for both parent and child without exposing sensitive scope or authority data through unauthorized surfaces.
- **FR-023**: Legacy test-run executions without authoritative scope identity MUST fail closed for cleanup and MUST NOT be assigned to a scope by inference from run kind alone.
- **FR-024**: Root test-run admission MUST validate the authoritative scope as open in the durable start/checkpoint transaction so teardown winning before delayed or replayed root materialization prevents that root from starting.
- **FR-025**: The work unit MUST preserve artifact pinning, input validation, depth limits, authority/tenant/partition inheritance, retention, cancellation/fault semantics, delivery recovery, and safe diagnostics from #677–#681.
- **FR-026**: The work unit MUST NOT add broker-specific contracts, Studio UI, distributed two-node placement or transport (#683), activity-authored scope controls, or WorkflowDefinitionActivity changes.

### Key Entities

- **Test-Run Scope**: Immutable scope identity, tenant, partition, expiry, lifecycle state, and replay-safe cleanup progress shared by one root test run and all of its descendants.
- **Scoped Workflow Execution**: A test execution carrying authoritative membership in exactly one test-run scope in addition to its run kind and ordinary execution context.
- **Scoped Workflow Dispatch**: A retained DispatchWorkflow record whose immutable context includes the enclosing test-run scope and exact Published child pin.
- **Scope Cleanup Request**: An idempotent expiry or explicit-teardown request bound to one scope and its tenant/partition context.
- **Scope Cleanup Result**: Safe aggregate counts and lifecycle disposition without child payloads, authority claims, or provider diagnostics.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In all draft-parent test cases with divergent child draft and Published content, 100% of child starts use the retained Published artifact identity and provenance.
- **SC-002**: Parent and child inspection reports the same exact run kind in 100% of `TestRun`, `PublishedRun`, `BackgroundWeaverRun`, and legacy `Unknown` regression cases before and after restart.
- **SC-003**: A detached test child remains live through 100% of ordinary parent-completion cases while its scope remains open.
- **SC-004**: Scope expiry and explicit teardown cancel 100% of eligible live children and affect zero production, cross-scope, cross-tenant, or cross-partition children.
- **SC-005**: At least 100 concurrent duplicate cleanup attempts converge on one scope-closing generation and no duplicate logical child cancellation.
- **SC-006**: Three replay attempts at each before-admission and after-admission crash boundary converge to the same terminal state, with no child revival or state regression.
- **SC-007**: Waited test children pass the same success, fault, cancellation, and delivery-failure outcome matrix as production children.
- **SC-008**: Full Runtime, DispatchWorkflow, Publishing API, Groundwork, Resumption, and Architecture suites remain green with no #683, broker, Studio, or WorkflowDefinitionActivity expansion.

## Assumptions

- One root test-run request creates one enclosing scope; nested DispatchWorkflow descendants inherit it transitively.
- Scope expiry is reached when the current time is equal to or later than the recorded expiry.
- Explicit teardown is an internal/application capability in this slice; no new public HTTP route or Studio workflow is required.
- The Runtime scope aggregate owns scope identity, expiry, and lifecycle. The existing Publishing `WorkflowTestRun` record is a projection using the same ID and expiry; its cleanup closes the Runtime scope before removing projection/source artifacts.
- Ordinary root or intermediate parent completion does not close the scope.
- Scope cleanup is a control-plane cancellation reason distinct from parent cancellation, so it applies to detached children and does not change the public activity inputs.
- Existing runtime inspection authorization and tenant selection remain authoritative; no scope identifier is accepted as sufficient authorization by itself.
- Distributed execution mechanics remain #683, but provider-neutral state and cancellation responsibilities introduced here must be usable by that later slice.

## Dependencies

- #677 provides exact Published child pins, bounded inputs/depth, and inherited execution context.
- #680 provides child-terminal projection and parent/child cancellation semantics.
- #678 and #681 provide durable inspection, restart-safe dispatch state, and recovery boundaries reused by cleanup.

## Out of Scope

- Distributed two-node placement, forwarding, and transport (#683).
- Broker or service-bus integration.
- Studio lifecycle or cleanup UI.
- WorkflowDefinitionActivity.
- New DispatchWorkflow inputs for scope, expiry, or cleanup policy.
- Changing the default fire-and-forget or parent-cancellation options.
