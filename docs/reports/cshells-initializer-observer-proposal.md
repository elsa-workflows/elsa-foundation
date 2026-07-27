# Upstream proposal — CShells `IShellInitializerObserver`

Status: proposal (for the external CShells repo; not implementable in `elsa-foundation`).
Origin: drafted as a finding in spec 129 (Cold-Start Readiness unit 1), filed as a carryable artifact by spec 132
(unit 4, opt-in eager shell activation).
Target package: `CShells` / `CShells.Abstractions` (observed at `0.0.29-preview.147`).

## Problem

The First-Request/Cold-Start Readiness program (`docs/program-goals/first-request-cold-start-readiness.md`) needs
to attribute the shell-activation wall **per initializer** — schema admission vs. EF `EnsureCreated`/`Migrate` vs.
identity seeding vs. task-pump start — to size unit 2 (ReadyToRun) and target the residual after units 3–4. That
attribution is **not host-observable today**, which is the gating finding for unit 2
(`docs/reports/cold-start-readiness-2026-07.md`, §"per-initializer attribution is NOT host-observable").

### Why the host cannot observe it (verified against 0.0.29-preview.147)

- Initializers are registered by concrete type: `AddShellInitializer<T>(LifecyclePhase, order)` stores a
  `ShellInitializerRegistration { InitializerType, Phase, Order }`.
- At activation, CShells resolves each initializer **by its concrete registered type** from the shell container and
  invokes `IShellInitializer.InitializeAsync` internally.
- Consequences for a host trying to wrap it:
  - A `Decorate<IShellInitializer>()` is never hit — resolution is by concrete type, not by the `IShellInitializer`
    service.
  - The registration carries a `Type`, not a replaceable factory/delegate, so there is no injection seam.
  - `IShellLifecycleSubscriber` fires only on whole-shell `ShellLifecycleState` transitions
    (`Initializing`/`Active`/…), which brackets the **entire** activation wall, not the individual initializers.
    (And per spec 129 §"observability caveats", even that bracket can arrive as a bare `Active` mark without a
    paired `Initializing` for the first lazy activation.)

So the host can time the whole `Initializing→Active` wall but cannot split it without editing every feature's
initializer — which defeats the point of a framework-level diagnostic.

## Proposed hook

An opt-in observer invoked by CShells around each `InitializeAsync`, resolved from the shell (or host) container if
present and skipped entirely if absent (zero cost when unused):

```csharp
namespace CShells.Lifecycle;

/// <summary>
/// Optional observer invoked by the activation pipeline around each <see cref="IShellInitializer"/> run.
/// Resolved once per activation; if none is registered, the pipeline behaves exactly as today (no overhead).
/// Implementations must be non-throwing and side-effect-free with respect to activation — purely diagnostic.
/// </summary>
public interface IShellInitializerObserver
{
    /// <summary>Called immediately before an initializer's <c>InitializeAsync</c> is invoked.</summary>
    ValueTask OnInitializerStartingAsync(ShellInitializerExecutionContext context, CancellationToken ct = default);

    /// <summary>
    /// Called immediately after an initializer completes (or faults). <paramref name="result"/> carries the
    /// elapsed duration and, on failure, the exception — CShells still propagates the original failure to the
    /// activation caller; the observer is notified, not given a chance to swallow it.
    /// </summary>
    ValueTask OnInitializerCompletedAsync(ShellInitializerExecutionContext context, ShellInitializerObservation result, CancellationToken ct = default);
}

/// <summary>Identifies the initializer being run and the shell/activation it belongs to.</summary>
public sealed record ShellInitializerExecutionContext(
    ShellDescriptor Shell,           // Name + Generation
    Type InitializerType,            // the concrete registered type
    LifecyclePhase Phase,            // the registered lifecycle phase
    int Order);                      // the registered order within the phase

/// <summary>The outcome of a single initializer run.</summary>
public sealed record ShellInitializerObservation(
    TimeSpan Elapsed,
    bool Succeeded,
    Exception? Error);
```

### Where it plugs in

In the activation pipeline's initializer loop (the code that today does, per phase/order,
`resolve(registration.InitializerType)` then `await initializer.InitializeAsync(context, ct)`):

```csharp
var observer = shellServices.GetService<IShellInitializerObserver>(); // resolved once per activation; may be null
foreach (var registration in orderedRegistrations)
{
    var initializer = (IShellInitializer)shellServices.GetRequiredService(registration.InitializerType);
    var execContext = new ShellInitializerExecutionContext(
        shell.Descriptor, registration.InitializerType, registration.Phase, registration.Order);

    if (observer is not null)
        await observer.OnInitializerStartingAsync(execContext, ct);

    var sw = ValueStopwatch.StartNew();
    Exception? error = null;
    try
    {
        await initializer.InitializeAsync(context, ct);
    }
    catch (Exception ex)
    {
        error = ex;
        throw; // unchanged: activation still fails as today
    }
    finally
    {
        if (observer is not null)
            await observer.OnInitializerCompletedAsync(
                execContext, new ShellInitializerObservation(sw.Elapsed, error is null, error), ct);
    }
}
```

Notes for the CShells maintainer:
- **Opt-in / zero-cost.** Resolve the observer once per activation; when unregistered, the loop is byte-identical
  to today. No new required dependency.
- **Non-authoritative.** The observer is notified around the run but never alters ordering, results, or failure
  propagation. Observer exceptions should be caught and logged by CShells so a buggy diagnostic can't break
  activation.
- **Container choice.** Resolving from the shell container lets a shell-composed diagnostic feature observe its own
  activation; a host-container overload (or resolving host-registered observers too) would let a host observe every
  shell. Either is acceptable for our use — the host-level case is what unit 2 needs.
- **`LifecyclePhase`/`ShellDescriptor`/`ShellInitializerRegistration`** already exist in
  `CShells.Abstractions`/`CShells.Lifecycle`; the only new public types are the two records and the interface.

## What it unlocks

- Per-initializer duration attribution of the activation wall, host-observable with **no host reflection and no
  per-feature edits** — directly sizes program unit 2 (ReadyToRun/TieredPGO share of activation JIT) and confirms
  the schema-admission initializer as the dominant term that unit 3 targets.
- A precise `Initializing→Active` bracket per shell (the `OnInitializerStarting` of the first initializer is a
  reliable activation-start signal), fixing the spec-129 caveat that the first lazy activation delivered `Active`
  without a paired `Initializing`.
- Complements the in-repo spec-129 boot instrument: the boot timeline would forward these callbacks to
  `boot.shell:<name>:initializer:<type>` spans on the existing `Elsa.Boot` ActivitySource — no host wiring beyond
  registering one observer.

## Interim (until the hook lands)

Unit 2 sizing continues to use: the whole-activation wall (spec-129 instrument) + the deterministic 922-op schema
count (`ColdStartSchemaOperationCountTests`) + targeted timing added temporarily inside the specific initializers
under investigation. Opt-in eager activation (spec 132) does **not** depend on this hook — it moves the whole wall
off the request path regardless of per-initializer visibility.
