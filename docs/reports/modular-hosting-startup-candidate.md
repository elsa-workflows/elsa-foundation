# CShells Startup Activation Candidate

Program: [Modular Hosting Upstream Delivery](../program-goals/modular-hosting-upstream-delivery.md); [CShells #143](https://github.com/valence-works/cshells/issues/143). Status: draft extraction design for later refinement, not delivered behavior.

Research note, read-only. CShells source baseline: local combined prerequisites `352a25e`. Foundation host and consumer tests were inspected at the program checkout; report source paths identify their owning repository. This is a bounded extraction design, not an implementation or verification result.

## What exists today

CShells already owns the activation primitive: `IShellRegistry.GetOrActivateAsync(name, ct)` is the lazy, same-path operation that serializes concurrent activation per shell name (`src/CShells.Abstractions/Lifecycle/IShellRegistry.cs:34-46, 22-25`). `ActivateAsync`, `ReloadAsync`, `ReloadActiveAsync` and lifecycle subscriptions are also exposed, but there is no startup activation, retry, or readiness policy there (`IShellRegistry.cs:48-77, 146-155`). Despite its name, `CShellsStartupHostedService` does not activate shells: `StartAsync` is a no-op and `StopAsync` drains/disposes actives at shutdown (`src/CShells/Hosting/CShellsStartupHostedService.cs:8-27, 30-69`). Keep that shutdown coordinator separate.

Foundation.Host has a stronger boot contract. Its `EagerShellActivationHostedService` is enabled by default, selects children under `CShells:Shells`, does one serial attempt per shell in `StartAsync`, and starts background retries for failures after the host can listen (`src/apps/Elsa.Foundation.Host/Shells/EagerShellActivationHostedService.cs:40-85`). Retries are indefinitely capped exponential backoff (1 second initial, 1 minute maximum), with subtractive jitter; malformed/nonpositive settings fall back to defaults (`src/apps/Elsa.Foundation.Host/Shells/EagerShellActivationRetryOptions.cs:13-59`). Shutdown cancels and waits for retry work, bounded by the host shutdown token even if activation ignores cancellation (`EagerShellActivationHostedService.cs:88-98`).

Foundation also classifies Elsa EF module refusals as operator-action cases: they retry only at the max interval (`EagerShellActivationRetryOptions.cs:30-44`; refusal reporting in `EagerShellActivationHostedService.cs:136-147`). `ShellActivationTracker` records failed attempts and clears them when any activation path makes the shell active (`src/apps/Elsa.Foundation.Host/Shells/ShellActivationTracker.cs:13-33, 54-77, 123-132`). `/health/ready` reports whether all configured shells are active and a sanitized failure code/attempt count/next-attempt time (`src/apps/Elsa.Foundation.Host/Health/HealthEndpoints.cs:7-63`). The Attention contributor adds Elsa-specific permissions, EF details, and warning/critical semantics (`src/apps/Elsa.Foundation.Host/Shells/ShellActivationAttentionContributor.cs:1-63`). These classification, Attention, and host-health models must remain outside CShells.

Workbench deliberately has different defaults and ordering. `EagerShellActivationHostedService` is registered only when `Elsa:Boot:EagerShellActivation:Enabled` is explicitly true; it selects configured names/all names, serially tries them before host startup completes, logs and swallows faults, and performs no retry (`src/apps/Elsa.Workbench/Program.cs:410-418`; `Boot/EagerShellActivationHostedService.cs:26-70`; selection in `Boot/EagerShellActivationOptions.cs:15-115`). Separately, `DefaultShellWarmup` is registered by default. It returns immediately from `StartAsync`, waits for `ApplicationStarted`, then optionally activates the default shell and publishes status/telemetry (`src/apps/Elsa.Workbench/Program.cs:195-202`; `Readiness/DefaultShellWarmup.cs:21-36, 51-95`). `WarmDefaultShell` defaults true and names `default` (`Readiness/ShellReadinessOptions.cs:3-15`). `/health/ready` is Workbench's default-shell projection, correlating warmup generation and active registry state (`Readiness/ShellReadinessEndpointExtensions.cs:15-43`). Preserve both paths and their independent opt-ins; the post-listen warmup must not become eager pre-listen activation by default.

## Concrete minimal upstream proposal

Add a reusable per-run activation runner in `CShells.Hosting`, but do not register it through `AddCShells` and do not give CShells ownership of the host's startup ordering. Hosts keep thin `IHostedService` adapters: Foundation awaits the initial pass from its existing adapter; Workbench's eager adapter awaits a one-shot pass; Workbench's warmup adapter waits for `ApplicationStarted` and then invokes the same runner for its one shell. Normal `AddCShells` remains fully lazy.

Proposed surface (names illustrative pending upstream review):

```csharp
public interface IShellActivationRunner
{
    Task<IShellActivationRun> StartAsync(
        IReadOnlyList<string> shellNames,
        ShellActivationRetryPolicy? retryPolicy = null,
        IShellActivationObserver? observer = null,
        CancellationToken startupCancellationToken = default);
}

public delegate TimeSpan? ShellActivationRetryPolicy(
    string shellName, Exception failure, int failedAttempts);

public interface IShellActivationRun : IAsyncDisposable
{
    IReadOnlyList<ShellActivationAttemptState> Snapshot { get; }
    Task StopAsync(CancellationToken shutdownToken = default);
}
```

`StartAsync` performs the initial attempts serially in the caller's startup phase, then returns a run handle. A `null` retry policy means one shot; a policy returning `null` for a specific failure stops retrying that shell; a positive delay schedules its next attempt. The run owns retry tasks and a private stopping token. `StopAsync` cancels and joins them, bounded by the supplied host shutdown token; `DisposeAsync` cancels and observes any remaining work. The initial pass observes `startupCancellationToken`; retry lifetime is explicitly owned by the returned handle so the IHostedService can stop it on host shutdown. Activation always goes through `GetOrActivateAsync`.

The observer receives immutable attempt-completed values with shell name, attempt number, timestamps, outcome/active generation, and the exception for host-local classification/logging. The run's immutable current snapshot keeps only generic values (attempt count, active generation, last failure type/time, next retry time/status); it does not expose exception messages or an endpoint. Observer exceptions must not suppress an activation result or terminate retry scheduling; they are logged by the runner. The runner reconciles success with `GetActive`/registry lifecycle so a request or reload that activates a shell clears stale failed state before the next retry. An optional injected `TimeProvider` (or internal delay abstraction) makes retries deterministic in tests without real sleeps.

There is no built-in target selector, host phase selector, retry default, readiness aggregation, health route, telemetry schema, or Elsa configuration binding. Hosts provide target names and policies. A host-supplied retry delegate sees the original exception: Foundation maps transient faults to capped exponential delay plus jitter and maps `IEfModuleRefusal` to the max interval; CShells itself has no EF dependency. Workbench passes no retry policy, preserving one-shot eager semantics and swallowed failures. This keeps package support to activation execution/lifetime and a small state projection rather than a control plane.

## Consumer defaults and ownership boundaries

| Consumer | Preserve | Keep local |
|---|---|---|
| Foundation.Host | Default-on eager first attempt; serial attempts before listening; unbounded retry after start; capped exponential/jitter; host-stop cancellation; readiness requires all configured shells. Adapter passes its configured shell names and Elsa retry policy, awaits `StartAsync` initial pass, stores the run, stops it from `StopAsync`, and projects runner attempt state through its current readiness endpoint. | EF refusal classification and slower check interval; public sanitized health JSON; Attention severity/details/permissions; `AddShellStartupValidation`. Replace the duplicate local retry loop/tracker attempt bookkeeping with runner state + an Elsa projection; keep subscriber reconciliation for activations caused by request/reload. |
| Workbench eager adapter | Eager option stays off unless explicitly enabled; opted-in eager attempt remains pre-listen, one-shot, logs/swallow failures and continues to next name. | Workbench options/target selection, activation timing logs. Pass no retry policy. |
| Workbench default warmup adapter | Warmup remains enabled by default after `ApplicationStarted`, returns immediately from host `StartAsync`, and activates only configured default shell. It calls the same runner for one target with no retry policy, then maps attempt outcome into `ShellReadinessState`; feature-discovery + activation telemetry remains around this call. Eager and warmup may overlap: same-name serialization/reuse in `GetOrActivateAsync` prevents duplicate activation, while warmup retains its own readiness transition and phase telemetry. | Workbench `ApplicationStarted` wait, readiness state/endpoint/options, telemetry and host-specific failure codes. Keep auto reload opt-in false, owned by its separate package-change work. |

There is an intentional overlap when Workbench opts into eager activation: eager and `DefaultShellWarmup` can both call `GetOrActivateAsync`. The runner is just a common execution primitive; each adapter owns its own state transitions, phase, and telemetry. Registry same-name serialization and active-shell reuse make this an observation/reuse rather than duplicate generation; warmup still owns terminal readiness and its telemetry. Preserve the order and test this overlap instead of merging the state machines. The Workbench readiness endpoint does not activate a shell; it compares the active shell and its readiness snapshot (`ShellReadinessEndpointExtensions.cs:15-39`).

`AddShellStartupValidation` is an Elsa extension for running each shell's `ValidateOnStart` checks during activation, because the generic host validates only the root container (`src/apps/Elsa.Foundation.Host/Program.cs:87-89`, `src/apps/Elsa.Workbench/Program.cs:260-262`). Keep it in Elsa. Issue #2354 remains the owner of Foundation.Host composition extraction; #143 should add no competing edits to `Program.cs` or general host-composition APIs. This work is about generic activation scheduling/retry primitives only, not composition takeover, package refresh, or a control plane.

## Consumer regression proof targets

1. `tests/essentials/Modularity/Tests/FoundationHostEagerActivationTests.cs`: preserve serial first attempt + retry success, delay/backoff/jitter defaults and cap, refusal-at-cap, stop cancellation/bounded shutdown, sanitized readiness reason codes, tracker clearing on request/reload activation, and Attention mapping (`:60-160, 162-205, 208-344`).
2. `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostEagerActivationRetryTests.cs:35-61`: process stays alive/not-ready while SQLite is unavailable, becomes ready after repair without restart. This is the principal real-host retry/readiness regression.
3. `tests/essentials/Modularity/Tests/EagerShellActivationTests.cs:21-153`: Workbench option parsing and target selection, disabled is inert, enabled selection order, faults are swallowed and later names still attempted. Add/assert pre-listen ordering and overlap with default warmup if shared runner code changes.
4. `tests/essentials/Modularity/Tests/ShellReadinessTests.cs:31-249`: default options, nonblocking `StartAsync`, activation only after `ApplicationStarted`, disabled path, cancellation, status transitions and telemetry.
5. `tests/essentials/Workbench/Tests/CompositionActivationObservationTests.cs:94-177` and `tests/essentials/Workbench/Tests/WorkbenchShellActivationTests.cs:126-127`: keep readiness observation non-activating and retain the existing ready generation through failed candidate composition.
6. `tests/essentials/Cluster/EntityFrameworkCore/Tests/FoundationHostBootTests.cs:210-225`: eager activation off remains lazy until request, then serves/finalizes. Also retain `FoundationHostBootTests.cs:52-208` feed-loaded module / refusal behaviors.

Focused source-level ordering gap: Workbench unit tests cover target selection/fault behavior and warmup tests cover post-listen ordering separately; add a gated integration assertion only if implementation changes their coordination. No tests/builds were run for this note.

## Minimal proof contracts

- Use an injected `TimeProvider` to prove initial serial order, capped retry timing, retry cancellation/await on `StopAsync`, and no extra attempt after stop without wall-clock sleeps.
- A failing target does not prevent later initial targets from being attempted; a null retry policy makes exactly one attempt per target; policy `null` stops that target while positive delay schedules it.
- An activation made through request/reload between retries clears stale failed state and does not cause a second active generation. Observer faults do not corrupt result or retry ownership.
- Foundation consumer tests preserve default-on pre-listen first attempts, continuing capped retries, refusal-at-cap, cancellation, sanitized readiness, and Attention mapping. Workbench tests preserve default-off eager, pre-listen one-shot and continue-on-fault, plus warmup's nonblocking start/post-`ApplicationStarted` timing and independent telemetry/readiness.
- Add one coordinated Workbench test for eager + warmup enabled: both adapters can observe the same active generation; exactly one registry build occurs; warmup still publishes its own readiness state and telemetry.

## Main risk

A shared runner can accidentally erase meaningful host differences: blocking startup versus post-listen warmup, Foundation's ongoing recovery versus Workbench's one-shot best-effort opt-in, or failure diagnosis/readiness semantics. Keep the shared surface limited to serial registry activation, retry scheduling ownership, and generic attempt state. Host adapters own required-shell sets, lifecycle phase, readiness, telemetry, and detailed failure interpretation. API names and exact registration shape remain for upstream review; this note recommends no new hosted-service takeover or composition API.

## Cancellation and recovery boundaries to resolve in the task

Cancellation during the serial first pass must cancel/dispose its private run without starting orphan retries or requiring a handle that was never returned. Stop must remain bounded when activation ignores cancellation; a subsequent DisposeAsync must not reintroduce an unbounded join of that task. Specify how remaining task faults are observed and the cancellation source is cleaned when outstanding work completes.

Once startup activation succeeds, the job stops retrying. A later drain/unregister must not silently turn startup recovery into perpetual automatic reactivation. External request/reload activation may reconcile stale failed state while recovery is pending; host readiness still derives from the currently active registry generation.
