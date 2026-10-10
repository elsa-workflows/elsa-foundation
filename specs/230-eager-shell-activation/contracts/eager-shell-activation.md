# Contract — opt-in eager shell activation (spec 132)

## Switch

- Config key: `Elsa:Boot:EagerShellActivation:Enabled` (host config; env
  `Elsa__Boot__EagerShellActivation__Enabled=true`).
- Default: **off**. When off, `EagerShellActivationOptions.IsEnabled` returns false; `Program.cs` registers no
  hosted service and the host constructs nothing. Independent of the spec-129 phase-timing switch.

## Shell selection

- Config key: `Elsa:Boot:EagerShellActivation:Shells` (string array; env
  `Elsa__Boot__EagerShellActivation__Shells__0=...`).
- **absent / empty** → all configured shells (child keys of `CShells:Shells`). On the reference single-shell host
  that is exactly the demo `default` shell.
- **`["*"]`** → same as empty (explicit "all" marker).
- **`["a","b"]`** → those named shells, in order, de-duplicated.

## Activation path

- The hosted service (`EagerShellActivationHostedService`, a **host-level** `IHostedService`) calls
  `IShellRegistry.GetOrActivateAsync(name)` per target shell during `StartAsync`. This is the exact call
  `CShells.AspNetCore.Middleware.ShellMiddleware` makes on a cold request, so the resulting shell state is
  byte-identical to lazy activation — only the timing (boot vs. first request) differs.
- It never opens a shell scope (`IShell.BeginScope()` is a per-request concern), so there is nothing to leak or
  dispose. The CShells `IShellInitializer` scope disposal happens inside activation, unchanged.
- **Non-fatal:** a per-shell activation failure is logged (warning) and swallowed; that shell then activates
  lazily on its first request. Boot is never failed by eager activation. Cancellation is honored.

## Observable behavior (verified, spec-129 instrument on, low load)

| Signal | OFF (baseline) | ON (eager) |
|---|---:|---:|
| `first-request` phase duration | ~3596 ms (activation cliff) | ~18 ms (no activation) |
| first request curl wall | ~3.74 s | ~0.15 s |
| `shell:default:active` recorded | after `first-request` (~4766 ms) | during boot, before `kestrel-ready` (~4651 ms) |
| host `Application started` wall | ~1.0 s | ~5.2 s (activation paid at boot) |
| warm 2nd request | ~4 ms | ~5 ms |

The deterministic, load-independent signal is the **phase attribution move**: the activation wall leaves
`first-request` (ON) and appears in the boot window; the first request pays no activation wall. Wall magnitudes are
indicative (shared machine).
