# Cold-start readiness baseline — July 2026

## Outcome

The reference `Elsa.Server` host was instrumented with an opt-in boot / first-request phase timer (spec 129,
program unit 1). This unit **measures only — it optimizes nothing.** Two independent findings anchor the program:

1. **The cost is the lazy shell-activation cliff paid inside the first request, not host build.** Host build and
   Kestrel startup are seconds; the first matching request that triggers CShells shell activation dominates by
   one-to-two orders of magnitude. A warm second request returns in tens of milliseconds.
2. **Fresh-database schema admission applies a deterministic 873 DDL operations** for the reference SQLite
   `GroundworkAllFeaturesDeploymentSchema`, on one connection, inside that first request. A warm restart applies
   **0** (idempotent). This number is load-independent (asserted by `ColdStartSchemaOperationCountTests`) and is
   materially higher than the ~581 previously reputed.

## Load caveat (read first)

This machine runs parallel fleet sessions; during capture the 1-minute load average ranged roughly **95–455**
(measured `uptime` alongside each number below). **Every wall-clock number here is indicative, not a benchmark.**
The trustworthy, reproducible evidence is (a) the deterministic 873-operation count and (b) the *phase ratios*
(activation cliff ≫ host build). Re-run the recipe on a quiet machine (`uptime` load < 2) for publishable walls.

## Per-phase table (indicative walls, load ~200–455)

Captured from the `Elsa.Boot` phase table on a cold `dotnet run` of `src/Apps/Elsa.Server` with
`Elsa:Boot:PhaseTiming:Enabled=true`, first request `GET /workflows/http/hello-world` (404 — no workflow
published; the 404 still drives full shell activation through `ShellMiddleware`):

| Phase | Start (ms) | Duration (ms) | Notes |
|---|---:|---:|---|
| `host-build` | 0.0 | 2,526.7 | CreateBuilder → Build: 117-feature catalog, Nuplane package-ALC loads, DI |
| `config-ready` | 1,166.7 | — | configuration layering complete |
| `kestrel-startup` | 2,528.2 | 5,121.8 | Build → listening |
| `kestrel-ready` | 7,650.0 | — | accepting connections; shell still not activated |
| `first-request` | — | **106,417.7** | **the activation cliff** (lazy shell activation + admission), load 455 |
| `shell:default:active` | — | — | see caveat below |
| warm 2nd request | — | **34.0** | shell already active — one-time cliff confirmed |

The `curl` wall for the first request was **115.6 s** at load 455; the warm second request was **34 ms**. Ratio,
not magnitude, is the signal: on a quiet machine the July report measured ~24 s (contended) / ~8 s (clean) for
the same activation, versus sub-50 ms warm — the same one-to-two order-of-magnitude cliff.

## Fresh vs. warm database delta (deterministic)

| Case | Applied schema operations | Source |
|---|---:|---|
| Fresh SQLite database (first request, `AutoApplyOnStartup=true`) | **873** | `ColdStartSchemaOperationCountTests` |
| Warm database (already admitted) | **0** | same test — idempotent restart |

873 = the physicalized `CREATE`/index operation set for the 8 reference deployment families (Runtime, Secrets,
Studio Preferences, Distributed, Workflows Design, Activities Design, Design-atomic ledger, Publishing). Identity
is excluded because the reference host persists ASP.NET Core Identity through EF Core, not Groundwork
(`shells.json` enables `FoundationIdentityAspNetCoreIdentityEntityFrameworkCore`). The
`elsa-groundwork.db` file created by the cold run was ~4.7 MB.

## Mid-activation contention tail

A second request arriving while the shell is still activating does not get its own activation — it **queues behind
the in-flight lazy activation** and returns only when activation completes. The recipe fires two concurrent
requests at cold start and reports the slower as the contention tail; in the July report a request issued during
activation waited **24 s** against an ~8 s clean activation. So the observed cost is not merely "the first request
is slow" — it is "**every** request that arrives before activation finishes pays the full activation wall." This
is the single most user-visible cold-start symptom and the primary target for unit 4 (opt-in eager activation).

## Key finding — per-initializer attribution is NOT host-observable (gates program unit 2)

CShells (external package `0.0.29-preview.147`) registers each `IShellInitializer` via
`AddShellInitializer<T>(LifecyclePhase, order)`, storing a `ShellInitializerRegistration { InitializerType, Phase,
Order }`, and at activation resolves each initializer **by its concrete registered type** and invokes
`InitializeAsync` internally. There is no host interception point: a `Decorate<IShellInitializer>` is never hit
(resolution is by concrete type), and the registration carries a `Type`, not a replaceable factory. The host can
therefore time the **whole activation wall** but cannot split it across schema-admission vs. EF EnsureCreated vs.
seeders without editing each feature.

Additionally, in the captured run the `IShellRegistry` lifecycle subscriber received the default shell's `Active`
transition but **not a paired `Initializing`** for the first lazy activation, so even the activation-wall
*bracketing* from that seam is partial; the cliff is bracketed by `kestrel-ready` → `first-request` instead.

**Proposed upstream CShells hook:** an opt-in `IShellInitializerObserver` (or reuse the `IShellLifecycleSubscriber`
shape) invoked around each `InitializeAsync` with the initializer's concrete type + phase + order + duration. That
single seam makes per-initializer attribution host-observable with no host reflection and no per-feature edits.
Until it lands, unit 2 sizing uses: the whole activation wall (this instrument) + the deterministic 873-op count +
targeted timing added inside the specific initializers under investigation.

## Reproduction

- Build: git `4012e69fb7229bc303a2ae6a4190b5ea513f563d` (this branch) plus the spec 129 instrument; .NET `10.0.300`;
  `Darwin arm64`; loopback HTTP; Groundwork SQLite (default `shells.json`, Development).
- Deterministic op count (load-independent):

```bash
dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj \
  --filter "FullyQualifiedName~ColdStartSchemaOperationCountTests"
```

- Phase table (cold host, instrument on):

```bash
# from a clean tree (no *.db under src/Apps/Elsa.Server)
Elsa__Boot__PhaseTiming__Enabled=true ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --project src/Apps/Elsa.Server/Elsa.Server.csproj
# then, once listening, trigger activation:
curl -s -o /dev/null -w '%{http_code} %{time_total}s\n' http://localhost:5095/workflows/http/hello-world
# the phase table prints to the server log at first-request completion.
```

- Full cold-start recipe (JSON + Markdown artifacts, contention tail, load-annotated):

```bash
bash tools/performance/measure-cold-start.sh --launch \
  --workflow-url http://localhost:5095/workflows/http/hello-world \
  --output-json /tmp/elsa-cold-start.json \
  --output-markdown /tmp/elsa-cold-start.md
```

Timing budgets stay opt-in local/quiet-machine gates. The container path
(`docker/compose/docker-compose.images.yml`, elsaworkflows/elsa-server, SQLite, port 13000) is the intended
publishable baseline; the local `Elsa.Server` equivalent was used here because the published image was not
rebuilt in this worktree and the machine load made a container run pointless for walls — the deterministic op
count and phase ratios do not depend on which host runs.

## Recommended next unit

Both attackable costs are now sized: the activation cliff (units 3 + 4) and JIT (unit 2). By measured impact:

1. **Unit 4 — opt-in eager activation** removes the contention tail (the worst user-visible symptom: every early
   request pays the full wall) by moving activation off the request path. Highest user-facing win; host-side, no
   external-package change required.
2. **Unit 3 — schema skip-if-current** removes the 873-operation admission on warm boots (the common
   restart/redeploy case) and batches it on fresh boots. Deterministic, high-leverage, guarded by the pinned test.
3. **Unit 2 — ReadyToRun publish** cuts the ~2.5 s host-build + JIT-heavy activation share; do it alongside, and
   file the CShells `IShellInitializerObserver` proposal so unit 2 can attribute the residual per-initializer cost.
