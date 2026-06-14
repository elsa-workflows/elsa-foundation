# Groundwork Host-Configurable Persistence Feasibility

Program goal state: [Groundwork Persistence Readiness](../program-goals/groundwork-persistence-readiness.md).

Status: draft feasibility report.

## Outcome

Groundwork is feasible as a provider-neutral persistence lane in `elsa-foundation`, with provider choice remaining host-owned via feature composition. A full all-in replacement for every runtime persistence surface is not yet proven.

## What the current codebase already gives us

- Runtime persistence seams already exist as replacement contracts in `Elsa.Workflows.Runtime.Core` (`IWorkflowExecutableStore`, `IWorkflowExecutionStateStore`, `IBookmarkStateStore`, `IDurableValueStateStore`, `IIncidentStateStore`, `IOperationalStateStore`, `ISchedulerStateStore`, `IRuntimePostCommitOutboxStore`).
- The current runtime API feature registers in-memory defaults for those seams (`WorkflowsRuntimeApiFeature`), which means host composition can replace them without changing runtime domain code.
- Design persistence is currently EF Core + SQLite feature composition (`WorkflowsDesignPersistenceEFCoreSqlite`, `ActivitiesDesignPersistenceEFCoreSqlite`) and is already host-composed through shell features.

## Groundwork fit by persistence category

| Category | Fit | Notes |
|---|---|---|
| Runtime low-risk artifact/document stores | High | Good first POC target for provider-neutral host switching. |
| Runtime continuation-state stores | Medium / benchmark-gated | Possible, but requires evidence for latency/concurrency/recovery behavior. |
| Runtime operational hot-path stores (outbox ordering, mailbox/ownership, locks/leases) | Unproven | Not rejected; requires explicit operational contracts beyond current document-store guarantees. |
| Design-definition persistence | Medium | Possible future lane, but out of current POC scope. |

## Answer to "can Groundwork support runtime hot-path stores?"

Potentially yes, but not by assumption.

Groundwork's current document-store contract is strong for document persistence and declared indexes. Hot-path operational stores need additional guarantees that must be explicit and tested:

- deterministic ordering behavior where required,
- ownership/lease semantics for agent-style processing,
- retry and idempotency contracts under partial failure,
- recovery behavior after restart/failure windows,
- operational observability (metrics/traces/diagnostics).

Until those guarantees are modeled and proven, operational stores stay specialized or benchmark-gated.

## Recommended implementation route

1. Implement an opt-in `Elsa.Persistence.Groundwork` bridge with provider adapters resolved at host composition time.
2. Implement one runtime low-risk POC store behind an existing replacement contract (`IWorkflowExecutableStore`).
3. Keep existing defaults intact when Groundwork is not enabled.
4. Add a hot-path viability matrix/checklist to prevent silent migration and prevent premature rejection.

## POC acceptance criteria

- Provider can be switched in host composition without runtime/domain code changes.
- Runtime consumes the same store contract independent of provider adapter.
- Existing non-Groundwork composition still runs unchanged.
- Reported operational hot-path candidates have explicit evidence gates before migration.

