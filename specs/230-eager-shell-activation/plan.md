# Plan — 132 opt-in eager shell activation

## Surface decision

Use the public `IShellRegistry.GetOrActivateAsync(name)` — the same call `ShellMiddleware` makes on a cold request
(verified by decompiling `CShells.AspNetCore.Middleware.ShellMiddleware`). This gives byte-identical shell state and
needs no CShells change and no synthetic-request pipeline. The synthetic-request fallback the task allows for is
**not** needed and is not built.

## Components (all in `src/Apps/Elsa.Server/Boot/`, alongside the spec-129 instrument)

1. `EagerShellActivationOptions` (record + static helpers)
   - Constants: `ConfigurationSection` = `Elsa:Boot:EagerShellActivation`, `EnabledConfigurationKey`,
     `ShellsConfigurationKey`, `AllShellsMarker` = `*`, `ConfiguredShellsSection` = `CShells:Shells`.
   - `IsEnabled(IConfiguration)` — loose "true"/"1" parse (mirrors `BootPhaseTimeline.IsEnabled`).
   - `Read(IConfiguration)` — binds `Enabled` + `Shells`.
   - `ReadConfiguredShellNames(IConfiguration)` — child keys of `CShells:Shells` (the composition).
   - `ResolveTargetShellNames(configured)` — empty/`*` ⇒ all configured; else named, de-duped, order-preserved.
     Pure and unit-testable without a host.
2. `EagerShellActivationHostedService : IHostedService`
   - `StartAsync`: read options; if disabled return; resolve targets; for each, time + `GetOrActivateAsync`;
     swallow+log per-shell failures (degrade to lazy); honor cancellation.
   - `StopAsync`: no-op.
3. `Program.cs` wiring: `if (EagerShellActivationOptions.IsEnabled(configuration)) AddHostedService<…>()`, placed
   just before the root auth registration (after `AddCShellsAspNetCore`, so `IShellRegistry` is registered).

## Why a hosted service (not ApplicationStarted)

- Runs at **host level** — required, because CShells does not run shell-scoped hosted services and the trigger must
  live outside any shell.
- `StartAsync` awaits activation during host start, so the wall is a boot-window cost and the spec-129 observer
  (subscribed before `app.Run()`) attributes the `Initializing→Active` span to boot, not to `first-request`.
- Idempotent activation means the small window between Kestrel listening and `StartAsync` completing is benign: a
  request in that window joins the same in-flight activation rather than starting a second.

## Testing

- Unit tests in `tests/Elsa/Modularity/Tests/EagerShellActivationTests.cs` (project already references
  `Elsa.Server.csproj`): switch parsing, configured-name enumeration, `Read` binding, target resolution
  (all/`*`/named/dedup), and the hosted service driving a recording `IShellRegistry` fake for enabled/disabled,
  all-vs-named, and non-fatal failure.

## A/B evidence

Cold `dotnet run` of `src/Apps/Elsa.Server` with `Elsa:Boot:PhaseTiming:Enabled=true`, OFF vs ON, fresh DB each
run, first request `GET /workflows/http/hello-world`; capture the boot phase table (activation-wall placement) and
the first-request wall. `uptime` recorded per run; the deterministic signal is the activation-wall's phase
attribution, not the wall magnitude (machine under fleet load).

## Docs

- Update `docs/program-goals/first-request-cold-start-readiness.md` unit-4 status.
- File `docs/reports/cshells-initializer-observer-proposal.md` (upstream hook).
