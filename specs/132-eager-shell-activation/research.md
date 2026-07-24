# Research — 132 opt-in eager shell activation

## CShells activation surface (package 0.0.29-preview.147, decompiled via ilspycmd)

`CShells.Lifecycle.IShellRegistry` (public, `CShells.Abstractions`):

```csharp
Task<IShell> GetOrActivateAsync(string name, CancellationToken ct = default);
Task<IShell> ActivateAsync(string name, CancellationToken ct = default);
IShell? GetActive(string name);
IReadOnlyCollection<IShell> GetActiveShells();
Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken ct = default);
void Subscribe(IShellLifecycleSubscriber subscriber);   // already used by spec-129 observer
// … Reload/Drain/Blueprint members …
```

`ShellMiddleware.InvokeAsync` (decompiled, `CShells.AspNetCore`):

```csharp
bool wasCold = registry.GetActive(shellId.Name) == null;
var shell = await registry.GetOrActivateAsync(shellId.Name, context.RequestAborted);
…
var scope = shell.BeginScope();
context.RequestServices = scope.ServiceProvider;
```

So the request path's activation entry point is `GetOrActivateAsync(name)`. Eager activation calling the same
method drives the identical activation; scope-creation (`BeginScope`) is a per-request concern we do **not**
touch, so eager activation neither opens nor leaks a shell scope.

- `GetOrActivateAsync` = activate-if-cold else return active. Idempotent + internally coordinated ⇒ a request
  racing an in-flight eager activation joins it (no double activation).
- `ActivateAsync` = force a fresh activation/generation; **not** what we want (would not match middleware
  idempotency). We use `GetOrActivateAsync`.

## Configured-shell enumeration

`CShells.Lifecycle.Providers.ConfigurationShellBlueprintProvider` reads shells from
`private const string ShellsPath = "CShells:Shells"`, taking each child section's **key** as the shell name. So
`configuration.GetSection("CShells:Shells").GetChildren()` keys == the composition's shell names. The reference
`shells.json` defines exactly one: `default`. Reading config (not the registry) keeps enumeration synchronous,
deterministic, and free of blueprint-provider timing.

## Where the trigger must live

- Memory (verified in code): CShells disposes the `IShellInitializer` scope right after activation; shell-scoped
  `IHostedService` does not run under CShells. Program.cs already documents the host-root vs shell-container split
  for console-log streaming ("process-global, host-level diagnostic … composed once on the application root").
- Therefore the eager trigger is a **root** `IHostedService` registered on `builder.Services`, resolving the root
  `IShellRegistry` singleton (the same instance the middleware and the spec-129 observer use).

## Instrument ordering

`BootShellActivationObserver` is subscribed to `IShellRegistry` in Program.cs **before** `app.Run()`. The hosted
service's `StartAsync` runs during `IHost.StartAsync` (i.e. during `app.Run()`), after that subscription. So when
both switches are on, the observer captures the eager activation's `Initializing→Active` transitions and records
the wall in the boot window — exactly the deterministic A/B signal we want.

## Decision log

- **Public API, not synthetic request.** A clean public activation API exists → no synthetic-pipeline fallback.
- **Empty Shells = all configured shells.** Makes the demo single-shell host correct with zero shell config, while
  honestly meaning "all" on a many-shell host (opt-in, documented trade). `*` is an explicit alias for the same.
- **Non-fatal failures.** Eager activation degrades to lazy on error rather than failing boot.
- **Await in StartAsync** (vs. fire-and-forget at ApplicationStarted): makes activation a boot-phase cost and keeps
  the phase attribution clean; idempotency makes the listen-window race benign.
