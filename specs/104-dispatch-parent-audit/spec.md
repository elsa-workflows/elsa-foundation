# Feature Specification: DispatchWorkflow Parent Audit Remediation

**Feature Branch**: `codex/dispatch-674-audit`  
**Created**: 2026-07-17  
**Input**: GitHub issue #674, "Add a transport-neutral DispatchWorkflow activity", plus the completed `674b7125..f8fcd063` program review.

## User Story 1 - Preserve dispatch work across crashes (Priority: P1)

As a runtime operator, I need dispatch final-failure, redrive, resume, admission, cancellation, and retention transitions to converge after crashes and concurrent work so that no child or parent execution is stranded or misclassified.

**Independent Test**: Inject failures at every persistence boundary, replay the affected operation, and verify one durable lifecycle outcome, one child execution, and one parent continuation or safe failure.

## User Story 2 - Keep bounded operations complete (Priority: P1)

As a runtime operator, I need cleanup, retention, inspection, and outbox claiming to remain bounded while still making progress through every eligible record.

**Independent Test**: Seed more than one page of mixed eligible and retained records, run repeated bounded sweeps or claims, and verify every eligible record is eventually processed without loading unbounded history.

## User Story 3 - Expose safe, contract-correct inspection and redrive (Priority: P1)

As an authorized operator, I need list/detail/redrive APIs to return complete allowlisted lifecycle evidence and the documented error shapes without leaking persisted metadata.

**Independent Test**: Exercise successful and rejected redrive, failed-dispatch list/detail inspection, corrupted incident metadata, and permission boundaries through the Runtime API.

## User Story 4 - Restore auditable acceptance evidence (Priority: P1)

As the parent-program owner, I need the checked task ledgers and parent audit to name tests that actually exist and prove the required in-memory, Groundwork, TestRun, and two-node scenarios.

**Independent Test**: Run every command in the quickstart and confirm every named test filter matches tests, all task claims are supported, and the audit accurately records the generated-map exception.

## Functional Requirements

- **FR-001**: Final child-start delivery failure MUST remain durably replayable until the dispatch failure projection, safe incident, and required waited-parent resume work all exist.
- **FR-002**: Redrive MUST classify an active dispatch without durable redrive evidence as rejected, and MUST converge after crashes or concurrent requests without exposing `Pending` unless deliverable redrive work exists.
- **FR-003**: Parent-resume and child-cancellation post-commit work MUST use a bounded retry/backoff policy and remain idempotent.
- **FR-004**: Distributed forwarding MUST expose a durable admitted lifecycle state, and cancellation racing with child admission MUST converge on either no child or a cancelled admitted child.
- **FR-005**: Retention deletion MUST be conditional on the exact terminal snapshot inspected and MUST make progress beyond retained pages.
- **FR-006**: TestRun cleanup MUST make bounded progress through every matching child, including more than one query page.
- **FR-007**: Redrive rejection MUST use the documented Runtime API error shape; list and detail inspection MUST expose the same safe failure evidence.
- **FR-008**: Failure classification and identifiers MUST be projected from known values or deterministic identities rather than arbitrary incident metadata.
- **FR-009**: Groundwork dispatch queries and outbox claims MUST apply stable ordering and limits in the provider query rather than after materializing all matches.
- **FR-010**: Safe retry scheduling and attempt evidence required by #681 MUST be produced by runtime code and exposed without payload leakage.
- **FR-011**: Groundwork convergence, waited TestRun terminal outcomes, child run-kind inspection, published-child selection, and integrated two-node DispatchWorkflow behavior MUST have executable acceptance tests.
- **FR-012**: The parent audit and task ledgers MUST state only evidence supported by existing tests and commits.
- **FR-013**: No generated-map refresh MUST be run; pre-existing generated-map deltas and manifest wording MUST be reconciled without regeneration.
- **FR-014**: The work MUST NOT add `WorkflowDefinitionActivity` or Studio UI support.

## Success Criteria

- **SC-001**: Every injected failure boundary converges after replay with no stranded parent, orphaned child, or active dispatch lacking deliverable work.
- **SC-002**: Repeated cleanup and retention sweeps process all eligible records in data sets larger than the configured page size.
- **SC-003**: Runtime API contract tests cover successful and rejected redrive plus safe failed-dispatch list/detail projections.
- **SC-004**: Provider-backed queries demonstrate bounded retrieval for dispatch lists and outbox claims.
- **SC-005**: Every test class named by specs 101–104 exists and every quickstart filter matches at least one test.
- **SC-006**: Focused, provider, distributed, architecture, and full solution verification pass.
- **SC-007**: A self-review loop reaches a complete pass with no actionable findings.
- **SC-008**: The parent audit remains the durable report under `docs/reports/`, accurately records the final evidence, and confirms the forbidden scope and skipped generated-map refresh.

## Edge Cases

- The process stops after an original outbox item reaches final delivery failure but before any or all failure projections are written.
- The process stops between creation of deterministic redrive evidence and lifecycle transition, or concurrent redrive requests race.
- Parent cancellation or TestRun cleanup races with local or remote child admission.
- A terminal retention candidate is redriven after inspection but before deletion.
- The first bounded page contains only retained or previously processed records.
- Incident metadata contains unknown classifications or forged identifiers.
- Distributed forwarding is durable but remote admission acknowledgment is delayed or duplicated.

## Assumptions

- Existing public contracts remain additive; provider contracts may gain additive default members or request fields where needed for bounded execution.
- Deterministic dispatch, incident, outbox, redrive, and cancellation identities remain the convergence keys.
- Generated maps are not regenerated in this work unit.
