# Implementation Plan: Actor terminal eviction (#542 / spec 128)

## Layers touched

Source of truth is `src/` (per repo convention). Changes are additive and gated by a default-on kill switch.

### 1. Terminal fact plumbing (`WorkflowExecutionCommandProcessResult`)
- Add `bool WorkflowTerminated = false` positional param (defaulted, so existing named-arg construction is unchanged).
- `FromDrain` sets it from `drainResult.StoppedOnTerminalStatus`.
- Add a `Terminal` static factory (test/support only; production always projects the real drain via `FromDrain`).

### 2. Surface terminal on the mailbox dispatch result (`InProcessWorkflowExecutionActor.EnqueueAsync`)
- On `AcceptedButFaulted`: add `runtime.dispatch.workflowTerminated = "true"` to the existing fault metadata when the
  process result is terminal.
- On `Accepted`: attach a metadata dictionary with the same key when terminal (accepted results carry no reason but may
  carry metadata — validated by `WorkflowExecutionCommandDispatchResult`).

### 3. Self-passivating handle + options + gauge (`InProcessWorkflowExecutionActorProvider`)
- `_agents` now stores `SelfPassivatingActorHandle` (wraps the inner actor). Cached per key via `GetOrAdd`, so
  `Assert.Same` for repeated activations still holds and `_agents.Count` is the live-mailbox gauge source.
- The handle implements both `EnqueueAsync` overloads (delegating to the matching inner overload so dispatch options are
  never lost) and, after awaiting the inner dispatch, calls `provider.PassivateAsync` when the result carries the
  terminal metadata and `PassivateOnTerminal` holds. Passivation uses `CancellationToken.None` (cleanup must complete;
  the reaper is the backstop) and boundary `AfterCheckpointCommit`.
- New `RuntimeActorEvictionOptions { PassivateOnTerminal = true }` constructor-injected (a new options singleton
  following the `RuntimeReplaySafeFusionOptions` pattern).
- New instance `Meter` (`Elsa.Workflows.Runtime`) with `ObservableGauge` `elsa.runtime.actor.live_mailboxes` observing
  `_agents.Count`. Provider implements `IDisposable` to dispose a self-created meter; an optional injected meter (tests)
  is left to the caller. Modeled on `GroundworkPrivilegedAccessSink`'s meter usage.

### 4. Straggler reaper (`RuntimeResumptionService.SweepAsync`)
- In the existing terminal branch (spec 113 purge site), after `PurgeResidualSchedulerWorkAsync`, call
  `agentProvider.PassivateAsync` (boundary `ProviderSafeBoundary`) for the terminal id. Idempotent no-op if no mailbox.

### 5. DI wiring
- `RuntimeCoreServiceCollectionExtensions`: `TryAddSingleton<RuntimeActorEvictionOptions>()`, and register the provider
  via an **explicit factory** (`new InProcessWorkflowExecutionActorProvider(executor, evictionOptions)`) rather than
  greedy ctor selection — target-typed / ambiguous-ctor call sites have broken CI before, and the container disposes the
  singleton (and its meter) on shutdown.
- `WorkflowsRuntimeDistributedFeature`: the composed `InProcessWorkflowExecutionActorProvider` is registered with the
  same explicit factory so the local-drain engine inherits eviction.

## Constructor design (DI-safe)

`InProcessWorkflowExecutionActorProvider` keeps the three existing public ctors (`()`, `(executor)`, `(executor, int)`)
and adds `(executor, RuntimeActorEvictionOptions)` and the fullest `(executor, int, RuntimeActorEvictionOptions,
Meter?)`. DI uses explicit factories, so greedy selection is never relied upon.

## Test plan (full projects, never subsets)

- `tests/Elsa/Workflows/Runtime/Tests` — extend `RuntimeInProcessAgentProviderTests`: terminal metadata surfaced;
  registry-to-zero after 5000 distinct terminal runs; kill-switch growth; activation-after-eviction for every
  activation reason (theory); the three races; live-mailbox gauge drop. Extend `RuntimeResumptionServiceTests`: sweep
  reaps a lingering terminal mailbox, never a non-terminal one.
- `tests/Elsa/Workflows/Runtime/Distributed/Tests` — extend `DistributedWorkflowExecutionActorProviderTests`: terminal
  command evicts the local mailbox + redelivery re-activates; reaper passivation releases the placement lease.

## Gate

Full solution build (`dotnet build Elsa.Server.slnx`) — mandatory (ctor call-site regressions). Run full test projects,
not filters, on the shared machine (check `uptime` first).
