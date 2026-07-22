# Contract — boot phase instrument (spec 129)

## Switch

- Config key: `Elsa:Boot:PhaseTiming:Enabled` (host config; env `Elsa__Boot__PhaseTiming__Enabled=true`).
- Default: **off**. When off, `BootPhaseTimeline.CreateIfEnabled` returns `null`; `Program.cs` registers no boot
  singleton, adds no middleware, subscribes no lifecycle observer, and starts no ActivitySource listener. The
  only always-present cost is one `Stopwatch.StartNew()` at process entry (nanoseconds, never read when off).

## ActivitySource

- Name: `Elsa.Boot`. Intentionally distinct from the runtime execution tracing source so a boot-span listener
  never attaches to the hot path. Spans: `boot.host-build`, `boot.kestrel-startup`, `boot.first-request`,
  `boot.shell:<name>:activation-wall`, etc. Tags: `boot.phase`, `boot.start_ms`, `boot.duration_ms`,
  `boot.detail`. Spans only materialize when a listener is subscribed.

## Recorded phases (host-observable)

| Phase key | Source | Kind |
|---|---|---|
| `config-ready` | after configuration layering | mark |
| `host-build` | `0 → after builder.Build()` | span |
| `kestrel-startup` / `kestrel-ready` | `IHostApplicationLifetime.ApplicationStarted` | span + mark |
| `shell:<name>:activation-wall` | `IShellRegistry.Subscribe` → `Initializing`→`Active` | span (see caveat) |
| `first-request` | front-of-pipeline middleware | span |

The phase table is printed via `ILogger` once, at first-request completion (`EmitTableOnce`, idempotent).

## Observability caveats (findings)

1. **Per-initializer timing is not host-observable.** CShells resolves each `IShellInitializer` by its concrete
   registered type and invokes `InitializeAsync` internally; the registration carries a `Type`, not a replaceable
   factory, so there is no host interception point. The instrument times the whole activation wall only. Gating
   finding for program unit 2. Proposed upstream fix: a CShells `IShellInitializerObserver` invoked around each
   `InitializeAsync` with concrete type + phase + order + duration.
2. **First-activation wall bracketing is partial.** In the captured run the `IShellRegistry` subscriber received
   the default shell's `Active` transition but not a paired `Initializing`, so `activation-wall` recorded as a
   bare `shell:default:active` mark rather than an `Initializing→Active` span. The activation cost is therefore
   bracketed by `kestrel-ready` and `first-request` (which wraps the lazy activation the request triggers) rather
   than by a subscriber-delivered pair. This is an additional limit of the external-package seam, not a bug in
   the instrument.

## Deterministic schema baseline

`ColdStartSchemaOperationCountTests` pins the reference SQLite `GroundworkAllFeaturesDeploymentSchema`
fresh-database applied operation count at **873** and asserts warm restart applies **0**. Load-independent;
regression guard for unit 3 (skip-if-current).
