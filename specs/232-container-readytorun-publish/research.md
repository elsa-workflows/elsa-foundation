# 134 — Research & measurement: ReadyToRun container publish

## Summary

R2R is enabled on the container publish path only, per-RID, framework-dependent. The deterministic, load-
independent signal (publish-output size for the shipped `linux-x64` RID + a successful crossgen pass over the full
~90-assembly graph) is the trustworthy evidence here; wall-clock startup deltas were **not** trustworthy on the
capture machine (see load caveat) and are reasoned from the spec-129 phase shares instead.

## Settings evaluated

### ReadyToRun — CHOSEN (on, Dockerfile-scoped, per-RID, framework-dependent)

- **Why on:** spec-129 baseline attributes ~2.5 s to `host-build` and ~5 s to `kestrel-startup` before any shell
  activation, dominated by assembly load + JIT of the ~90 project assemblies
  (`docs/reports/cold-start-readiness-2026-07.md`). R2R crossgen-compiles those assemblies at publish time so the
  runtime runs native code instead of JIT-compiling on first call — directly targeting that JIT share.
- **Why Dockerfile, not csproj:** crossgen runs on every publish. Placing `PublishReadyToRun` in the csproj would
  slow every local `dotnet publish`/dev inner loop for no dev benefit. The Dockerfile is the sole producer of the
  shipped image, so the cost is scoped there. Local `dotnet build`/`dotnet run` are untouched.
- **Why per-RID (required):** R2R is an AOT step for a concrete target; a portable (RID-less) publish cannot be
  R2R-compiled (`dotnet publish` errors / silently no-ops without a RID). The Dockerfile derives the RID from
  BuildKit's `TARGETARCH` (`amd64`→`linux-x64`, `arm64`→`linux-arm64`).
- **Why framework-dependent (`--self-contained false`):** only the app assemblies are R2R'd and grow; the shared
  aspnet runtime in the final image stage is reused (already R2R upstream). Keeps the size cost to the app layer
  and preserves the `dotnet Elsa.Server.dll` entrypoint.

### TieredCompilation / TieredPGO — left at .NET 10 defaults (not overridden)

- Both are **on by default** in .NET 10 (TieredPGO/Dynamic PGO default-on since .NET 8). R2R images still
  participate in tiered compilation and can be re-JIT'd with PGO at tier-1, so steady-state peak throughput is
  unaffected by enabling R2R.
- Explicitly setting them would be cargo-culting: no measured benefit, and pinning e.g. `TieredCompilation=false`
  would *hurt* startup (it forces full JIT of non-R2R methods up front). Defaults are already optimal; left alone.

### InvariantGlobalization — REJECTED (not enabled)

- `InvariantGlobalization=true` drops the ICU dependency (a real image-size + startup saving) but forces the
  invariant culture everywhere and makes culture-aware APIs throw / behave as ordinal.
- The reference host does **auth** (ASP.NET Core Identity + OpenIddict — normalization, token/date handling),
  **JSON** (System.Text.Json + Newtonsoft), and **workflow expression evaluation** (Jint JavaScript, culture- and
  date-sensitive). Any of these can rely on culture-sensitive string/date behavior; a silent behavior change in
  auth or expression evaluation is a correctness risk disproportionate to the startup gain from this small unit.
- **Decision: skip.** If a future unit wants it, it needs an explicit audit of every culture-sensitive path
  (identity normalization, `ToLower/ToUpper`, `DateTime.Parse`, sorting) plus a conformance pass — out of scope
  here.

### Trimming / single-file / self-contained / NativeAOT — out of scope

- Trimming is unsafe against the reflection-heavy CShells feature catalog + Nuplane package-ALC loading (assemblies
  are discovered and activated by reflection; the trimmer would strip "unused" feature code). Self-contained /
  single-file bundle the whole runtime and would balloon the image with no startup win over the shared aspnet
  base. NativeAOT is incompatible with the reflection/plugin model. None are pursued.

## Multi-arch / RID verification

- `.github/workflows/docker.yml` builds `platforms: linux/amd64,linux/arm64` through Docker's maintained GitHub
  Builder workflow. It fans the platforms out to matching GitHub-hosted runners (`ubuntu-24.04` and
  `ubuntu-24.04-arm`) and assembles their digests into one multi-platform manifest. Each R2R publish therefore
  runs natively for its target architecture instead of running ARM64 crossgen under QEMU.
- The Dockerfile's `TARGETARCH`→RID map (`${TARGETARCH:-amd64}`, `amd64`→`x64`) covers both shipped arches and
  falls back to `linux-x64` for a plain non-BuildKit `docker build`.
- Local validation here targets `linux-x64` (the primary shipped RID) for the deterministic size A/B, plus
  `osx-arm64` for a runnable native smoke test.

## Load caveat (read first)

The capture machine ran under extreme fleet load (1-minute load average swinging **~106 → ~785** across this
session, with ~140 competing processes). Wall-clock startup numbers are therefore **not** a benchmark — they are
scheduler noise. Proof, from the same-machine paired boot-instrument A/B below: the baseline (no-R2R) run recorded
a *lower* `host-build` than the R2R run (479 ms vs 1543 ms) while its `kestrel-startup` was *higher* (6387 ms vs
3831 ms) — the per-phase deltas point in opposite directions, which is only possible if load, not R2R, is driving
them. The spec-129 baseline itself declared its walls "indicative, not a benchmark" at the far lighter load 95–455.

The trustworthy evidence is therefore deterministic and load-independent: publish-output byte size (below) and a
clean per-RID crossgen pass. The startup win is reasoned from the spec-129 phase attribution — `host-build`
(~2.5 s) + `kestrel-startup` (~5 s) are JIT-bound and are exactly what R2R pre-compiles — plus R2R's
well-characterized 20–40% reduction of JIT-heavy startup. **A real wall delta must be booked on a quiet machine
against the CI-built container (spec-129 recipe) before the program's boot-time target is claimed.**

## A/B — publish output size (deterministic, `linux-x64`, framework-dependent)

Two `dotnet publish -c Release -r linux-x64 --self-contained false` runs, identical except `-p:PublishReadyToRun`.

| Metric | Baseline (no R2R) | R2R | Delta |
|---|---:|---:|---:|
| Total publish dir | 202 MB | 289 MB | **+87 MB (+43%)** |
| All `*.dll` in publish root | 60.5 MB (61,952 KB) | 147.2 MB (150,776 KB) | +86.7 MB (+143%) |
| `Elsa.*` app `*.dll` only | 11.7 MB (11,944 KB) | 26.4 MB (27,064 KB) | +14.8 MB (+127%) |
| `*.dll` count | 261 | 261 | — (no files added; each grew) |

Notes:
- R2R publish across the full ~90-project + transitive-dependency graph completed with **0 errors** (crossgen
  emitted no failures). The `linux-arm64`, `osx-arm64` R2R publishes also succeeded.
- Framework-dependent, so the shared aspnet **runtime** layer in the final image stage is unchanged; the +87 MB is
  the *entire* image cost of R2R and lands only on the app publish layer. Crossgen embeds native code beside the
  IL, so per-assembly growth is expected — the documented R2R size-for-speed trade.
- Most of the growth is in the transitive framework/package assemblies R2R also compiles (EF Core, OpenIddict,
  Jint, Npgsql, System.*), not just Elsa's own +14.8 MB — expected for a dependency-rich host.

## CI publish-time follow-up (2026-07-25)

Profiling four consecutive Docker Image runs showed that the original single-x64-runner design spent almost the
entire critical path compiling the ARM64 R2R image under QEMU:

| Run | x64 restore | x64 R2R publish | ARM64 restore (QEMU) | ARM64 R2R publish (QEMU) | Cache export | Build step |
|---|---:|---:|---:|---:|---:|---:|
| `30136536939` | 29 s | 4m32s | 5m37s | 34m06s | 7m01s | 47m30s |
| `30140770269` | 32 s | 5m32s | 7m11s | 44m19s | 5m02s | 57m07s |
| `30144631408` | 28 s | 5m57s | 7m33s | 46m49s | 3m45s | 58m40s |
| `30150541194` | 28 s | 6m14s | 8m02s | 47m59s | 3m18s (failed) | 59m52s |

The ARM64 publish was consistently about eight times slower than x64. This is an emulation tax, not an inherent
requirement of R2R. The revised design keeps the runtime R2R policy but removes QEMU from the compile path:

- Docker's maintained builder distributes x64 and ARM64 to native runners in parallel and publishes the final
  tags only after both platform digests succeed.
- GitHub Actions caches are isolated per platform, exported with `mode=max`, and treated as best-effort.
- The Dockerfile restores from the repo build configuration plus the preserved `*.csproj`/`*.targets` tree before
  copying source, so ordinary implementation changes no longer invalidate the RID-specific restore layer.
- The workflow skips pushes that cannot affect the image and cancels an obsolete in-progress main build when a
  newer main commit arrives.

The post-change performance budget is **under 15 minutes** for an uncached source publish on standard public
GitHub-hosted runners. The first main-branch run is the verification measurement; if it exceeds the budget,
revisit the ARM64 R2R scope rather than accepting another hour-long publish.

Local pre-merge validation on a native ARM64 development host completed the full ARM64 R2R publish in **5m26s**
with a cached restore. This is not a substitute for the main-branch CI measurement, but it validates the expected
order-of-magnitude improvement over the 34–48 minute emulated ARM64 publish.

## Boot-instrument phase table (INDICATIVE ONLY — noise-dominated, see caveat)

Same machine, `osx-arm64` publishes, `Elsa:Boot:PhaseTiming:Enabled=true`, first request `GET /`, load ~106–161.
Reported for completeness; **not** a basis for the decision (per-phase deltas contradict each other → noise).

| Phase | Baseline (no R2R) | R2R | (spec-129 ref, no R2R) |
|---|---:|---:|---:|
| `host-build` | 479 ms | 1543 ms | 2527 ms |
| `kestrel-startup` | 6387 ms | 3831 ms | 5122 ms |
| host-build + kestrel-startup | 6866 ms | 5375 ms | 7649 ms |

The only signal that survives the noise is the coarse sum (host-build + kestrel-startup), where R2R was ~22% lower
this run — directionally consistent with R2R's expected effect, but within the session's load variance and not
claimable as the number.

## Smoke (published output runs)

The `osx-arm64` R2R publish was run natively (`dotnet Elsa.Server.dll`, fresh CWD, Development). It started, and
`GET /` returned **HTTP 200** `{"status":"Healthy","service":"elsa-server"}` (warm second request 8 ms). The
`linux-x64`/`linux-arm64` outputs were not run (no Linux host available here); their R2R publishes completed
cleanly and are the same publish shape.

## QA

- Full solution build (`dotnet build Elsa.Server.slnx -c Release`): **0 errors**, 130 warnings — all pre-existing
  `GWxxxx` obsolete-API warnings in `Elsa.Persistence.Groundwork.Tests`, unrelated to this change (no code
  touched).

## Recommendation

**Ship R2R ON** (as configured). Rationale:

- **Low risk, reversible:** Dockerfile-only, framework-dependent, defaults preserved (no TieredPGO/Invariant-
  Globalization change), clean build, and the published output runs. Reverting is a one-line Dockerfile edit.
- **Targets the right cost:** R2R pre-compiles exactly the JIT-bound `host-build` + `kestrel-startup` phases
  (~7.5 s per spec-129) that units 3 (schema) and 4 (eager activation) do **not** touch. Even a conservative
  20–30% off those phases is ~1.5–2.5 s shaved before the first request — meaningful against the program's
  "boot→healthy < 5 s" target.
- **Known, bounded cost:** +87 MB (+43%) on the app publish layer. Acceptable for a workflow-server image; if
  image size later becomes a hard constraint (edge/function deployment), this is the first knob to reconsider.
- **CI cost is bounded separately:** R2R remains enabled, but each RID is compiled on its native runner in
  parallel. The 47–60 minute historical Docker builds were caused by QEMU and are no longer an accepted cost of
  the startup policy.

**Caveat on the claim:** the wall win could not be measured honestly on this fleet-loaded machine. Book the real
boot→healthy delta by running the spec-129 recipe against the CI-built container on a quiet machine (`uptime`
load < 2) before crediting R2R with a specific second-count in the program success criteria.
