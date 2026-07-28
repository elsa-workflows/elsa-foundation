# Research — spec 113: the second post-completion `elsa.runtime.drain` trace

## Question

A container run (`elsaworkflows/elsa-server`) shows, ~10.1s after each workflow run completes, a second 2-span
`elsa.runtime.drain` trace. Is this a passivation/idle timer pinning resources, or something else?

## Method

Static trace of the runtime scheduler/resumption/ownership seams (code is the source of truth), cross-checked with
the existing terminal-guard and durable-resumption tests, then reproduced deterministically with a focused unit
test over the in-memory queue + state store and confirmed over the Groundwork durable + coalescing path.

## Findings

### It is the resumption sweep, not a passivation timer

- The drain span is emitted per drain cycle by `ActivitySourceWorkflowEngineTracer.StartDrainCycle`
  (`src/Elsa/Workflows/Runtime/Diagnostics/ActivitySourceWorkflowEngineTracer.cs:48`). A drain span exists only
  when a drain runs — i.e. when a command envelope is processed.
- The ~10.1s cadence equals `RuntimeResumptionOptions.SweepInterval` (default `TimeSpan.FromSeconds(10)`,
  `src/Elsa/Workflows/Runtime/Resumption/Options/RuntimeResumptionOptions.cs:12`).
- Issue #542 confirms no production code path calls `IWorkflowExecutionActorProvider.PassivateAsync`; there is no
  idle-timeout/eviction mechanism. So the "passivation drain" framing is wrong.

### The seed: a residual scheduler work item stranded by the terminal guard

- `WorkflowSchedulerDrainer.DrainAsync` reads terminal status on entry and after each dispatched item
  (`src/Elsa/Workflows/Runtime/Services/WorkflowSchedulerDrainer.cs:92,125`) and, once terminal, exits the loop.
  Items are removed only on dispatch (`AckAsync`/`CompleteClaimAsync`), so any queued-but-undispatched item stays.
  Covered by `RuntimeSchedulerDrainTests.DrainAsync_StopsBeforeDequeuingWorkOnceWorkflowReachesTerminalStatus`
  (asserts the residual items **remain queued**).
- Leases/heartbeats are **not** the seed: `WorkflowDrainOrchestrator.DrainAsync` acquires a lease per drain and
  releases it in a `finally` (`.../WorkflowDrainOrchestrator.cs:130,166`); `RuntimeExecutionOwnershipService.ReleaseAsync`
  clears both lease and heartbeat (`.../RuntimeExecutionOwnershipService.cs:149-151`). So a cleanly-completed
  execution is invisible to the recovery scanner (`RuntimeRecoveryCandidateSelector` needs a *present* lease/
  heartbeat or a Detected interruption). The recovery scanner's default timeouts are 5 minutes — the wrong scale.

### The loop

`RuntimeResumptionService.DiscoverExecutionIdsAsync` surfaces the completed execution via
`ListPendingWorkflowExecutionIdsAsync` (no terminal filter). `RedriveAsync` sends a `RunSchedulerWork` envelope;
`WorkflowSchedulerCommandRouter.ProcessAsync` unconditionally enqueues it (`.../WorkflowSchedulerCommandRouter.cs:68`)
then drains; the drainer reads terminal-on-entry and skips, stranding the new item too. The next sweep rediscovers
it. Net: +1 stranded durable row + one drain span per 10s, forever, per completed-with-residual execution.

The Groundwork coalescing convergence test already documented the stranded item in prose
(`GroundworkCoalescingCrashConvergenceTests`, "stranded by the drainer's terminal-status guard (#293) —
deliberately never dispatched"); this unit turns that inert-but-leaking residue into a purge.

## Verdict

Not a harmless idle timer. It is a self-perpetuating re-drive loop that leaks durable queue rows without bound and
keeps the in-process actor un-evictable. Fix at the resumption service: never re-drive terminal executions; purge
their residue instead. (The exact "2-span" the container observer saw is the drain span plus a nested span from the
one sweep in which the item was still dispatchable; steady-state terminal re-drives are drain-only. The count is
incidental — the loop is the defect.)
