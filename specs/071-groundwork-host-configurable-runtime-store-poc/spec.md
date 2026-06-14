# Feature Specification: Groundwork Host-Configurable Runtime Store POC

**Feature Branch**: `sfmskywalker/groundwork-persistence-feasibility`
**Created**: 2026-06-14
**Status**: Draft
**Input**: Investigate Groundwork as the persistence story for Elsa with provider-agnostic runtime contracts and host-owned provider configuration.

## Scenarios & Tests

1. Given a runtime composition using `IWorkflowExecutableStore`, when the host enables Groundwork with provider A, then runtime resolves and uses the Groundwork-backed store implementation.
2. Given the same runtime composition, when the host switches to provider B, then runtime behavior remains contract-equivalent without runtime/domain code changes.
3. Given Groundwork is not enabled, when runtime is composed, then existing in-memory defaults still work.
4. Given a future proposal to migrate an operational hot-path store, when the proposal is reviewed, then explicit evidence gates (ordering/lease/retry/recovery/observability) must be satisfied before migration.

## Requirements

- **FR-001**: Add an opt-in Elsa Groundwork bridge surface under `src/Elsa/Persistence/Groundwork`.
- **FR-002**: Provider choice MUST be host-owned and configured through host/shell composition, not hard-coded in runtime core.
- **FR-003**: Add one low-risk runtime Groundwork-backed store implementation for `IWorkflowExecutableStore`.
- **FR-004**: Keep existing default runtime composition behavior when Groundwork is disabled.
- **FR-005**: Add tests proving provider-neutral contract behavior for the selected runtime store.
- **FR-006**: Add tests proving non-Groundwork composition remains unchanged.
- **FR-007**: Publish a hot-path viability gate matrix that must be satisfied before operational-store migrations.
- **FR-008**: This slice MUST NOT migrate operational hot-path stores (`IRuntimePostCommitOutboxStore`, lock/lease ownership, mailbox/agent ownership) to Groundwork.

## Non-Goals

- Migrating runtime operational hot-path stores in this slice.
- Migrating design-definition persistence in this slice.
- Replacing all runtime persistence seams in one step.

## Acceptance Criteria

- Host-level provider switch is demonstrated for the selected runtime store.
- Runtime/domain code remains unchanged when provider changes.
- Existing runtime defaults still run when bridge is disabled.
- Operational hot-path migration gates are documented and linked.

