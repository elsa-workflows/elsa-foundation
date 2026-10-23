# Research: Actor terminal eviction (#542 / spec 128)

## Established evidence (verified against the worktree branch)

- `InProcessWorkflowExecutionActorProvider._agents` + `_lifecycleLocks` are pruned **only** in `PassivateAsync`. An idle
  mailbox pins two dictionary entries, two `SemaphoreSlim`, and a ≤4096-entry idempotency cache — inert heap, no timers
  or loops. Scope is therefore **terminal eviction + gauge**, not LRU/idle eviction (a suspended workflow's idempotency
  cache is a live dedup layer; evicting it is a correctness hazard).
- The terminal signal exists and was dropped: `WorkflowSchedulerDrainer.DrainAsync` sets stop reason
  `WorkflowTerminated` (`RuntimeSchedulerDrainResult.StoppedOnTerminalStatus`, `RuntimeSchedulerDrain.cs:61`) after a
  Finish/terminal checkpoint, but `WorkflowExecutionCommandProcessResult.FromDrain` did not propagate it, and the mailbox
  `EnqueueAsync` result carried no terminal flag. That plumbing is the core addition.
- The single dispatch→drain projection site is `WorkflowSchedulerCommandRouter.ProcessAsync`, which calls
  `WorkflowExecutionCommandProcessResult.FromDrain(drainResult)` on both the burst and non-burst paths — one place to
  propagate the flag.
- **All six** `GetAgentAsync` call sites activate on demand (WorkflowStartDispatcher, BookmarkResumeDispatcher,
  RuntimeResumptionService, ExecutionPlacementPumpTask, ActivityDraftTestRunService, ChildCancelExecutor), so eviction is
  safe by construction — a later command simply re-activates. (Issue #542 text says four; it is six.)
- `AcquireLifecycleLockAsync` already performs a canonical-identity recheck after acquiring; the eviction path rides it
  and adds no new locking.

## Design decisions

- **Handle over self-passivating actor.** A wrapper handle keeps the inner actor pure (no back-reference to the
  provider) and centralizes eviction policy. Caching the handle per key preserves the existing "same id → same agent"
  contract (`Assert.Same`) and keeps `_agents.Count` an exact live-mailbox count for the gauge.
- **Metadata, not a new dispatch-result field.** The dispatch result already transports fault detail as metadata; the
  terminal flag rides the same channel (`runtime.dispatch.workflowTerminated`) for the lowest blast radius.
- **Passivate outside the critical section.** The handle awaits the inner `EnqueueAsync` (which releases the mailbox in
  its `finally`) before calling `provider.PassivateAsync`. Never re-enters the mailbox while holding it. Uses
  `CancellationToken.None`: eviction is cleanup; if it were skipped on a cancelled token the reaper collects it.
- **Distributed lease vs local mailbox.** The eager trigger fires on the node that owns the mailbox (the composed
  in-process provider) and drops the **local mailbox** — which is exactly the #542 leak. The placement **lease** is a
  separate, already-bounded resource (best-effort, clock-expiring); it is released eagerly by the resumption reaper
  calling the distributed provider's `PassivateAsync`. This keeps the distributed provider free of new eviction code
  (it inherits the trigger through `_localProvider.GetAgentAsync`), matching the unit brief's "test, not new code".

## Kill switch

`RuntimeActorEvictionOptions.PassivateOnTerminal` defaults to **true** (fixes the leak by default). Setting it false is
byte-identical to the pre-#542 provider — the growth-vs-bounded contrast is A/B testable, and a host retains an escape
hatch. Neither setting weakens single-writer-per-execution.

## References

- Issue [#542](https://github.com/elsa-workflows/elsa-foundation/issues/542) (2026-07-21 status comment).
- ADR [0031](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) —
  follow-up assigns agent lifetime/eviction to this unit; resolution #2 = single-writer inviolable.
- Spec [113](../113-terminal-resumption-purge/spec.md) — deferred #542 as its explicit follow-up.
- Spec [021](../021-runtime-inprocess-agent-provider/spec.md) — FR-006 passivation behavior now has a production trigger.
