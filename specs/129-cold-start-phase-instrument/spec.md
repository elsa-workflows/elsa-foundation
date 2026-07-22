# 129 — Boot / first-request phase-timing instrument + cold-start baseline (Cold-Start Readiness, program unit 1)

## Goal

Give the host an **opt-in boot / first-request phase-timing diagnostic** that attributes the known 8–75 s cold
start across its real phases, plus a **reproducible baseline** (deterministic schema operation count + a
container/local measurement recipe). This unit is **measurement only — it optimizes nothing.** It exists to
gate and size the other four units of the First-Request/Cold-Start Readiness program (ReadyToRun publish, schema
batch/skip-if-current, opt-in eager activation, warmups).

Charter / precedent: `docs/reports/runtime-http-performance-2026-07.md` measured a 24 s mid-activation wait and an
~8 s clean activation on the synchronous `hello-world` path and **explicitly deferred boot cost to a separate
track** — this track. That report optimized warm steady-state latency (checkpoint coalescing); it did not touch
startup. This spec instruments startup so the next units can.

## Non-goals (this unit)

- No change to boot cost. No ReadyToRun/TieredPGO Dockerfile change (that is program unit 2). No schema
  batching or skip-if-current (unit 3). No eager activation (unit 4). No warmups (unit 5).
- No change to steady-state behavior when the instrument switch is off. The instrument must be inert by default.

## What the cold start is made of (verified evidence)

- **Phase A — host build.** `src/Apps/Elsa.Server/Program.cs` builds a 117-feature host: `AddNuplane` +
  `AutoloadPackages` + `NuplaneAssemblyProvider` package-ALC loads, `AddCShellsAspNetCore` over ~90
  ProjectReference assemblies (`src/Apps/Elsa.Server/shells.json`), then `builder.Build()`.
- **Phase B — the activation cliff.** `app.MapShells()` installs the CShells `ShellMiddleware`; **shell activation
  is lazy on the first matching request.** CShells is an external package (`0.0.29-preview.147`,
  `Directory.Packages.props`); it cannot be edited. No in-repo eager-activation option exists.
- **Phase C — initializers.** 21 `IShellInitializer` implementations run during activation. The dominant one is
  `src/Elsa/Persistence/Groundwork/Sqlite/SqliteGroundworkDocumentStoreInitializer.cs`: with the default
  `AutoApplyOnStartup = true` it calls `InspectRuntimeAdmissionAsync`, which for the reference
  `GroundworkAllFeaturesDeploymentSchema` (8 feature families; identity is EF-backed here, so it is excluded)
  applies **873 DDL operations** on a fresh SQLite database, inside the first request, on one connection. Other
  initializers: `OpenIddictIdentityStoreInitializer` (EF EnsureCreated/Migrate), the identity seeders, the two
  diagnostics EF-SQLite stores, and `RunShellTasksInitializer` (task-pump start).
- **Phase D — JIT.** `src/Apps/Elsa.Server/Dockerfile` publishes with **no ReadyToRun / TieredPGO** — the JIT
  baseline. Recorded, not changed here.

## The instrument

A dedicated **`Elsa.Boot` `ActivitySource`**, default **off** via the host config switch
`Elsa:Boot:PhaseTiming:Enabled` (so production pays nothing), kept **separate from the runtime hot-path tracing
source** so subscribing to boot spans never perturbs steady-state workflow tracing. When on, it records nested /
sequential phase spans against one monotonic stopwatch started at process entry, and emits a **console phase
table** at first-request completion. Each span is also emitted on the `Elsa.Boot` ActivitySource for an OTel
listener.

Host-observable phases (recorded):

| Phase | How it is observed |
|---|---|
| `config-ready` | Mark after configuration layering. |
| `host-build` | Span 0 → after `builder.Build()` (feature catalog + package ALC + DI). |
| `kestrel-startup` / `kestrel-ready` | `IHostApplicationLifetime.ApplicationStarted`. |
| `shell:<name>:activation-wall` | `IShellRegistry.Subscribe(IShellLifecycleSubscriber)` → `Initializing`→`Active`. |
| `first-request` | Front-of-pipeline middleware times the first request end-to-end. |

### Key finding — per-initializer timing is NOT host-observable (gates program unit 2)

CShells registers each initializer with `AddShellInitializer<T>(LifecyclePhase, order)`, storing a
`ShellInitializerRegistration { InitializerType, Phase, Order }`. At activation CShells resolves each
`InitializerType` **by its concrete registered type** from the shell service provider and invokes
`InitializeAsync` internally. There is **no host interception point between registrations**: a host-side
`Decorate<IShellInitializer>` is never hit because resolution is by concrete type, and the registration carries a
`Type`, not a replaceable factory. Therefore the host can time the **whole activation wall** but **cannot**
attribute it to individual initializers (schema-admission vs. EF vs. seeders) without editing each feature.

The three phases the external package exposes are `LifecyclePhase.Prepare (0)` → `Default (1000)` →
`Start (2000)`; these are coarse buckets, not per-initializer, and are internal to CShells.

**Proposed upstream CShells hook (for unit 2):** add an opt-in `IShellInitializerObserver` (or reuse the existing
`IShellLifecycleSubscriber` shape) that CShells invokes around each `InitializeAsync` call with the initializer's
concrete type + phase + order + wall duration. That single seam makes per-initializer attribution host-observable
without any host-side reflection or per-feature edits. Until it exists, unit 2 sizing uses: (a) the whole
activation wall from this instrument, (b) the deterministic 873-op schema count, and (c) targeted per-initializer
timing added inside the specific initializers under investigation.

## Deterministic schema baseline

`ColdStartSchemaOperationCountTests` (in `Elsa.Persistence.Groundwork.Tests`) reconstructs the exact reference
SQLite physical target from the `GroundworkAllFeaturesDeploymentSchema` manifest-source set and pins the
fresh-database applied operation count at **873**, and asserts a warm restart applies **0** (idempotent). This
number is load-independent, is the anchor for the baseline report, and is the regression guard unit 3
(skip-if-current) compares against. It changes only when a feature adds/removes a storage unit or index.

## Measurement recipe

`tools/performance/measure-cold-start.sh` (in the `measure-http-workflow.sh` style): cold boot → healthy, first
authenticated call, first workflow execute, and the **mid-activation contention tail** (a second request fired
while the shell is still activating). Emits JSON + Markdown artifacts and scrapes the server's emitted phase
table. Every wall number is reported next to the 1-minute load average, because host load dominates wall time on
a shared fleet machine. The demo container path (`docker/compose/docker-compose.images.yml`,
elsaworkflows/elsa-server, SQLite, port 13000) is documented; when running the published container locally is
impractical, the local `Elsa.Server` equivalent is used and said so honestly.

## Baseline report

`docs/reports/cold-start-readiness-2026-07.md`: per-phase attribution, the actual 873-op count, fresh-vs-warm DB
delta, the contention-tail measurement, the reproduction command + build SHA + host + load caveats, and the
unit-2 gating finding.

## Success criteria (this unit)

1. Instrument is inert when `Elsa:Boot:PhaseTiming:Enabled` is off (no boot services registered, no pipeline
   middleware, no ActivitySource listeners) — proven by the full test projects of everything touched.
2. When on, it emits a phase table attributing host-build, Kestrel-ready, per-shell activation wall, and
   first-request wall.
3. The deterministic 873-op schema count is pinned by a passing test and reproduced in the report.
4. The measurement recipe produces JSON + Markdown artifacts and captures the contention tail.
5. The per-initializer observability limitation and the upstream CShells hook proposal are documented as the
   unit-2 gating finding.

## Program success criteria (stated in the bucket doc, delivered by later units)

Cold boot→healthy < 5 s (with R2R); first authenticated call < 3 s (from 8–24 s+); first workflow execute
single-digit-second p100 with no contention tail; warm-boot schema phase < a few hundred ms. Sequencing: this
instrument → R2R publish → schema batch / skip-if-current → opt-in eager activation → warmups. The locked apply
protocol (`specs/094-harden-groundwork-stores/contracts/storage-composition.md:158`) keeps approval semantics;
the frozen `SchemaVersion` is not a lever — skip-if-current needs a separate applied-plan fingerprint.
EF-initializer consolidation belongs to the Zero-EF bucket.
