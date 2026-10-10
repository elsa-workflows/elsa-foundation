# Feature Specification: In-process workflow-execution actor terminal eviction / passivation trigger

**Feature Branch**: `claude/542-actor-terminal-eviction`

**Created**: 2026-07-22

**Status**: Implemented — merged in PR #983

**Spec number note**: `specs/` was scanned before allocation — 128 was free (specs ran to 127; siblings hold 129/130).
Allocated 128 per the unit brief.

**Input**: GitHub issue [#542](https://github.com/elsa-workflows/elsa-foundation/issues/542) — *"the in-process agent
registry grows unboundedly; nothing ever calls `PassivateAsync`."* ADR
[0031](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md)'s follow-up
names this unit the owner of agent lifetime/eviction; ratification resolution #2 (single-writer-per-execution) is
inviolable. This unit is the #542 follow-up that spec 113 (terminal resumption purge) explicitly deferred.

## Context

`InProcessWorkflowExecutionActorProvider` holds two `ConcurrentDictionary` registries — `_agents` (the live mailboxes)
and `_lifecycleLocks` — pruned only by `PassivateAsync`. Before this unit no production path called `PassivateAsync`, so
the provider retained one mailbox per workflow execution ever activated. Each idle mailbox is inert heap (two dictionary
entries, two `SemaphoreSlim`, and a bounded ≤4096-entry idempotency cache) with no timers or loops, but at N runs the
registries grow without bound.

PR #894 (spec 113) removed the *perpetual re-activation* that kept terminal executions' mailboxes being re-created every
resumption sweep, but the original leak — the mailbox realized by a workflow's first run lingering forever — remained.

The terminal signal already exists in the engine and was being dropped: `WorkflowSchedulerDrainer` computes
`RuntimeSchedulerDrainResult.StoppedOnTerminalStatus` (stop reason `WorkflowTerminated`), but
`WorkflowExecutionCommandProcessResult.FromDrain` did not propagate it and the mailbox `EnqueueAsync` dispatch result
carried no terminal flag. Wiring that fact through to a passivation trigger is this unit's core addition.

## Scope boundary

- **In scope**: **terminal eviction only** — passivate an execution's mailbox when a dispatched command's drain committed
  a terminal workflow status. Plus a straggler reaper in the resumption sweep and a live-mailbox gauge.
- **Explicitly not in scope**: LRU / idle-timeout eviction. A non-terminal (suspended) execution keeps its mailbox
  because its idempotency cache is still a live dedup layer for redelivered resume/scheduler work. Evicting a
  non-terminal mailbox is a correctness hazard, not a cleanup.
- **Idempotency-cache loss on eviction is benign for terminal executions only**: the drainer's terminal-status guard
  no-ops post-terminal work and ADR 0031 resolution #1 (queue enqueue-by-identity) is the primary dedup.

## Requirements

- **FR-001**: `WorkflowExecutionCommandProcessResult` MUST carry a `WorkflowTerminated` flag, set in `FromDrain` from
  `RuntimeSchedulerDrainResult.StoppedOnTerminalStatus`. Default false; kill switch and non-drain paths are
  byte-identical to before.
- **FR-002**: The mailbox `EnqueueAsync` dispatch result MUST surface the terminal fact as metadata key
  `runtime.dispatch.workflowTerminated = "true"` on both the `Accepted` and `AcceptedButFaulted` outcomes (a metadata
  entry, matching the existing fault metadata — lower blast radius than a new result field).
- **FR-003**: `InProcessWorkflowExecutionActorProvider.GetAgentAsync` MUST return a self-passivating handle that
  delegates dispatch to the inner actor and, when a dispatch result signals terminal, `await`s `PassivateAsync`
  **after** `EnqueueAsync` has released the mailbox — never inside the critical section. The handle is cached per
  execution (one per live mailbox), so repeated activations return the same instance (unchanged single-mailbox
  contract). All six `GetAgentAsync` call sites stay unchanged; the policy is centralized in the provider.
- **FR-004**: Terminal eviction MUST be gated on `RuntimeActorEvictionOptions.PassivateOnTerminal` (default **true**).
  With the switch **off** the provider is byte-identical to the pre-#542 unbounded-growth behavior (kill-switch A/B).
- **FR-005**: The resumption sweep (`RuntimeResumptionService.SweepAsync`) MUST, in its existing terminal branch (the
  #894 purge site), also call `agentProvider.PassivateAsync` after purging residual work — a straggler reaper for a
  mailbox that outlived its execution (eviction disabled, dispatch token cancelled, or terminal status reached by a
  post-commit intent / sibling fork). Idempotent no-op when no mailbox exists.
- **FR-006**: The provider MUST expose a `System.Diagnostics.Metrics` `ObservableGauge` (meter `Elsa.Workflows.Runtime`,
  instrument `elsa.runtime.actor.live_mailboxes`) reporting the live mailbox registry size. Zero cost until a listener
  attaches.
- **FR-007 (distributed parity)**: `DistributedWorkflowExecutionActorProvider` MUST inherit the trigger through its
  composed `_localProvider.GetAgentAsync` (no new distributed code): a terminal drain on the owning node evicts the
  **local mailbox**. The placement **lease** is released when the resumption reaper passivates the terminal execution
  through the distributed provider (its `PassivateAsync` already owner-checks and releases). Leases are best-effort and
  additionally expire on the placement clock.

## Invariants and races

- **Single-writer-per-execution at all times** (ADR 0031 resolution #2). Passivation runs only outside the mailbox
  critical section and rides `AcquireLifecycleLockAsync`'s canonical-instance recheck; no new locking is added.
- **Race (a)** redelivery `EnqueueAsync` vs terminal passivation on the same id → Deferred, Duplicate, or a no-op-drain
  Accepted; never two live mailboxes, never an exception.
- **Race (b)** passivation vs an in-flight non-terminal drain holding the mailbox → blocks until release.
- **Race (c)** redelivery mid-eviction across the canonical-lock retry loop → serialized, no deadlock, no lock leak.
- Bounded activate/passivate churn on repeated terminal redelivery is acceptable; the resumption sweep's terminal purge
  (spec 113) stops recurrence.

## Relationship to prior specs

- **Spec 021 FR-006** ("Provider passivation MUST remove the active mailbox and mark the old agent unavailable for new
  work") described passivation behavior but had **no production trigger**. This unit supplies that trigger: terminal
  drain completion is now the event that fires spec 021's passivation.
- **Spec 113** stopped the perpetual terminal re-drive and named #542 (this unit) as its explicit follow-up.

## Success criteria

- Both registries (`_agents` and `_lifecycleLocks`) return to 0 after N distinct terminal runs through the real dispatch
  path (verified at N = 5000).
- Activation-after-eviction mints a fresh active agent for every `WorkflowExecutionActorActivationReason`.
- `PassivateOnTerminal = false` reproduces the pre-#542 growth (registries grow one-per-run).
- The three races hold. Distributed: owner drops mailbox on terminal; reaper passivation releases the lease; redelivery
  re-claims + re-activates safely.
- The gauge drops after terminal eviction.
