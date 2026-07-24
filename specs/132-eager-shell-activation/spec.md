# 132 — Opt-in eager shell activation (Cold-Start Readiness, program unit 4)

## Goal

Move the CShells **lazy shell-activation cliff off the first-request path**. Today the first matching request pays
the full activation wall (~8 s clean / ~24 s contended / ~106 s under fleet load — spec 129 baseline), and **every
request that arrives mid-activation queues behind the same in-flight activation** and pays that whole wall. That
contention tail is the single most user-visible cold-start symptom (spec 129 report §"Mid-activation contention
tail"). This unit adds an **opt-in, host-side** trigger that activates the configured shell(s) at boot, through the
same activation path the middleware uses, so the wall is paid before the first user request instead of during it.

Charter/precedent: `docs/program-goals/first-request-cold-start-readiness.md` (unit 4); baseline
`docs/reports/cold-start-readiness-2026-07.md`; instrument `src/Apps/Elsa.Server/Boot/` (spec 129).

## Non-goals (this unit)

- No change to the activation code path itself — eager activation must produce **byte-identical shell state**, just
  earlier. It calls the same `IShellRegistry.GetOrActivateAsync` the request middleware calls.
- No editing CShells (external package `0.0.29-preview.147`). No new upstream dependency.
- No schema batching / skip-if-current (unit 3), no ReadyToRun (unit 2), no warmups (unit 5).
- Not on by default. Flipping the demo container's default to ON is a separate, later decision.

## Evidence: the activation surface (verified against CShells 0.0.29-preview.147)

Decompiled `CShells.AspNetCore.Middleware.ShellMiddleware.InvokeAsync`: after resolving a `ShellId`, it calls

```csharp
var shell = await _registry.GetOrActivateAsync(shellId.Name, context.RequestAborted);
```

`IShellRegistry.GetOrActivateAsync(string name, …)` is **public** (`CShells.Abstractions`,
`CShells.Lifecycle.IShellRegistry`). It activates the shell if not already active and otherwise returns the active
instance — idempotent and internally coordinated, so a request racing an in-flight eager activation joins that same
activation rather than starting a second one. Calling it from the host therefore drives the **exact** activation the
first request would, producing identical state. **No synthetic-request fallback is needed** — a clean public API
exists, so we use it. (The synthetic-pipeline fallback discussed in the task is unnecessary and is not shipped.)

Configured shell names are the child keys under `CShells:Shells` — the same source CShells'
`ConfigurationShellBlueprintProvider` (`private const string ShellsPath = "CShells:Shells"`) reads, so enumerating
that section matches the `shells.json` composition exactly without touching the registry.

## Gotchas (verified)

- **Host-level trigger only.** CShells does not run shell-scoped `IHostedService`s, and the eager trigger must sit
  *outside* any shell (it is what activates the shells). The trigger is a root/host `IHostedService`
  (`EagerShellActivationHostedService`), registered on `builder.Services` (the application root), exactly like the
  console-log-streaming host services already are (see the Program.cs "root host-level diagnostic" note).
- **Scope disposal.** CShells disposes the `IShellInitializer` scope right after activation (memory:
  cshells-initializer-scope-disposal). Eager activation does not open or hold a shell scope — `GetOrActivateAsync`
  returns the activated `IShell`; we never call `BeginScope()`. So there is nothing for us to leak or dispose.
- **Failure is non-fatal.** Eager activation is an optimization. If it throws, we log a warning and continue; the
  shell then activates lazily on its first request, i.e. today's behavior. It never crashes the host.

## Behavior

1. `Elsa:Boot:EagerShellActivation:Enabled` (default absent/false) gates everything. When off, the hosted service is
   never registered and the host constructs nothing.
2. When on, `EagerShellActivationHostedService.StartAsync` resolves the target shells and calls
   `GetOrActivateAsync` for each, timing and logging each activation.
3. **Multi-shell config shape** (`Elsa:Boot:EagerShellActivation:Shells`):
   - **absent / empty** → **all configured shells** (every child key under `CShells:Shells`). On the reference
     single-shell host that is exactly the demo `default` shell — so the demo default needs no shell list.
   - **`["*"]`** → same as empty: all configured shells (explicit "all" marker).
   - **`["a","b"]`** → only those named shells, in order, de-duplicated. Unknown names surface as a logged
     activation failure (non-fatal).
   - A many-shell host thus pays every activation at boot only when it opts in — the documented trade.

## Instrument interaction (spec 129)

When both this switch and `Elsa:Boot:PhaseTiming:Enabled` are on, the spec-129 `BootShellActivationObserver`
(subscribed to `IShellRegistry` before `app.Run()`) records the `Initializing→Active` activation-wall during the
**boot window** — because the hosted service's `StartAsync` runs during host start, before any request. The
`first-request` phase then contains **no activation wall**. That phase-attribution move (activation-wall present in
the boot window, absent from `first-request`) is the deterministic A/B signal, independent of noisy wall times.

## Acceptance

- OFF (default): identical to spec-129 behavior — activation wall inside `first-request`.
- ON: activation wall attributed to boot; `first-request` phase small; a request issued immediately after start
  does not pay the activation wall.
- Unit tests cover switch parsing, target resolution (all/named/`*`/dedup), and the hosted service driving
  `GetOrActivateAsync` for the resolved shells with non-fatal failure handling.
- Full solution build; full `Elsa.Modularity.Tests`.

## Follow-ups

- Upstream CShells `IShellInitializerObserver` proposal filed as
  `docs/reports/cshells-initializer-observer-proposal.md` (per-initializer attribution — unblocks unit 2 sizing).
- Whether to flip the demo container default ON: a separate decision (see the report's recommendation).
