# Feature Specification: Stop the perpetual post-completion resumption re-drive of terminal workflows

**Feature Branch**: `worktree-agent-aa3e72f831f70d93f`

**Created**: 2026-07-20

**Status**: Implemented — merged in PR #894

**Input**: Engine-performance phase-3, Unit B — *"the 10.1s post-completion passivation drain."* Profiling a
container run (`elsaworkflows/elsa-server`, OTel engine-span bridge live) showed that after a workflow run
completes, a **second** `elsa.runtime.drain` trace appears every ~10s. The premise was a ~10s passivation/idle
timeout. Characterization (below) refutes the "idle timer" framing and re-aims the unit at the real cause.

## Context and characterization outcome

The ~10.1s cadence is **not** a passivation/idle timer. It is the `RuntimeResumptionPumpTask` sweep interval
(`RuntimeResumptionOptions.SweepInterval`, default `TimeSpan.FromSeconds(10)`). The second drain trace is the
resumption pump **re-driving an already-terminal workflow execution**, and it repeats forever. The mechanism:

1. **Residual queue item (the seed).** When a workflow reaches a terminal status (`Completed`/`Faulted`/
   `Cancelled`), `WorkflowSchedulerDrainer.DrainAsync`'s terminal-status guard (#293) stops draining and leaves
   any not-yet-dispatched scheduler work item **queued** — items are removed only on dispatch. A sibling item
   from a parallel fork, or a post-commit `EnqueueSchedulerWork` intent delivered after the terminal checkpoint,
   is stranded in the durable queue.
2. **Discovery has no terminal filter.** `RuntimeResumptionService.DiscoverExecutionIdsAsync` discovers backlog
   via `IWorkflowSchedulerWorkQueue.ListPendingWorkflowExecutionIdsAsync`, which returns **any** execution with a
   non-empty queue — with no join to workflow status. So the completed execution is surfaced every ~10s.
   (The recovery scanner is *not* the source: leases + heartbeats are released on every completing drain, and
   its default 5-minute timeouts would give a minutes-scale, not a 10s, cadence.)
3. **Re-drive strands another item.** `RedriveAsync` sends a `RunSchedulerWork` envelope. The command router
   (`WorkflowSchedulerCommandRouter.ProcessAsync`) **unconditionally enqueues** that envelope as a work item
   before draining; the drainer immediately reads terminal-on-entry and skips the loop, so the new item is also
   never dispatched. Net effect per sweep: **+1 stranded durable row, a fresh lease acquire/release, and a fresh
   `elsa.runtime.drain` span** — forever, and growing without bound.

### Cost (what the "10.1s drain" actually pins)

Nothing is busy for 10.1s; the drain itself is fast (terminal → zero items dispatched). The ~10.1s is the sweep
interval. The steady-state cost, **per completed execution that stranded a residual item**, is permanent churn:
per tick, one workflow-execution-state read, one lease acquire + one release (two operational-state writes), one
`RunSchedulerWork` enqueue (one durable write), **one net-new stranded durable queue row (unbounded growth)**, one
emitted drain span, and — because the execution keeps being re-driven — the in-process actor mailbox is
repeatedly re-activated and **never** evictable. At N such executions the churn and the durable-row leak scale
linearly and never converge. This is write-amplification and unbounded storage growth, not a harmless idle timer.

## Scope boundary

- **In scope**: make the resumption sweep **terminal-aware**. A discovered execution that is already in a
  terminal status is never re-driven; instead its residual scheduler work is **purged**, so backlog discovery
  stops resurfacing it and the per-tick drain span ends. Terminal status is monotonic and the drainer already
  refuses to run post-terminal work, so removing the residue is exactly the outcome the terminal guard intends.
- **Out of scope (recommended follow-ups)**:
  - **#542 (in-process actor eviction).** This unit stops the *re-driving* that keeps re-activating the actor,
    but the actor realized by the workflow's original run still lingers in `InProcessWorkflowExecutionActorProvider`'s
    registry. A general terminal-state passivation trigger (the #542 ask) remains a separate design.
  - **Configurable resumption `SweepInterval`.** It is already configurable via `RuntimeResumptionOptions`; no
    new knob is required for this fix. (The 10s cadence was never the defect — the perpetual re-drive was.)

## Requirements

- **FR-1** A resumption sweep MUST NOT re-drive a workflow execution whose persisted status is terminal
  (`IsTerminal()`), regardless of which discovery source surfaced it (backlog or recovery scanner).
- **FR-2** For each discovered terminal execution, the sweep MUST purge its residual scheduler work items so the
  backlog source no longer resurfaces it on subsequent ticks.
- **FR-3** Non-terminal executions with genuine backlog MUST still be re-driven unchanged (Window C recovery,
  crash convergence, late deliveries to suspended workflows are all preserved).
- **FR-4** The purge MUST be idempotent and provider-agnostic: a concurrent completion yielding a no-op delete
  is not an error, and the operation MUST work over the in-memory, Groundwork durable, and coalescing queues.
- **FR-5** The sweep result MUST surface how many terminal executions were purged and how many residual items
  were removed, so the pump can log it and tests can assert it.

## Redelivery-safety analysis

Purging only ever targets executions confirmed **terminal in durable state**. A terminal status is monotonic and
the drainer's own guard already refuses to dispatch such work, so no legitimate dispatch can race the purge, and
no genuinely-suspended workflow (which is non-terminal) is ever touched — their passivation grace and late-delivery
routing are unchanged. A resume delivered to a completed workflow creates its own new work item and is refused by
the same terminal guard; purging pre-existing residue does not affect that path.
