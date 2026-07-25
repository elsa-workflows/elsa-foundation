# 134 — ReadyToRun container publish (Cold-Start Readiness, program unit 2)

## Goal

Cut the JIT-heavy share of the reference `Elsa.Server` cold start by publishing the **container** image with
**ReadyToRun (R2R)** AOT pre-compilation of the app's own assemblies, and honestly **measure** the win against the
size cost. This is program unit 2 of [First-Request / Cold-Start
Readiness](../../docs/program-goals/first-request-cold-start-readiness.md), sized by the spec-129 baseline: host
build is ~2.5 s and Kestrel startup ~5 s before any activation, dominated by assembly load + JIT of the ~90
project assemblies (`docs/reports/cold-start-readiness-2026-07.md`).

This is a **small** unit: publish-configuration + measurement. It changes only the container publish path and the
program/spec docs. It does **not** touch shell activation (unit 4 / spec 132), schema admission (unit 3 / spec
133), or any application code.

## What R2R does here

`PublishReadyToRun=true` runs crossgen2 over the app's managed assemblies at publish time, embedding native code
alongside the IL. At startup the runtime executes the pre-compiled native code instead of JIT-compiling each
method on first call, so the JIT-bound host-build + Kestrel-startup phases shrink. R2R images are not "frozen":
methods still participate in tiered compilation and can be re-JIT'd with Dynamic PGO, so steady-state peak
throughput is unaffected.

## Decisions (with evidence — see `research.md`)

- **R2R: ON**, scoped to the Dockerfile publish (`-p:PublishReadyToRun=true`), **not** the csproj. Putting it in
  the csproj would slow every local `dotnet build`/dev inner loop (crossgen runs on each publish). The Dockerfile
  is the only place the shipped image is produced, so that is where the cost belongs.
- **RID: required, derived from `TARGETARCH`.** R2R is a per-RID AOT step and cannot run on a portable (RID-less)
  publish. The image ships multi-arch (`linux/amd64` + `linux/arm64`, `.github/workflows/docker.yml`), so the
  Dockerfile maps BuildKit's `TARGETARCH` → `linux-x64` / `linux-arm64` and publishes per-RID. Docker's maintained
  GitHub Builder workflow distributes those platforms to matching x64 and ARM64 GitHub-hosted runners, so each
  crossgen pass runs natively and the two RIDs build in parallel.
- **Framework-dependent (`--self-contained false`).** Only the app assemblies get R2R'd and grow; the shared
  aspnet runtime in the final image stage is reused (and is already R2R upstream). The `dotnet Elsa.Server.dll`
  entrypoint is unchanged. This keeps the image-size cost to the app layer only.
- **TieredCompilation / TieredPGO: left at .NET 10 defaults (both already on).** Not overridden — the defaults are
  already optimal for a server and R2R composes with tiered compilation. Cargo-culting explicit values here would
  add risk with no measured benefit.
- **InvariantGlobalization: NOT enabled (rejected).** The host does authentication (ASP.NET Core Identity /
  OpenIddict), JSON, and workflow expression evaluation (Jint) — all of which can touch culture-sensitive string
  and date handling. Dropping ICU is a correctness risk disproportionate to a small startup gain, so it is
  deliberately skipped. See `research.md`.

## Non-goals

- No application-code change. No eager activation (unit 4), no schema batching / skip-if-current (unit 3), no
  warmups (unit 5).
- No change to the local/dev build or the non-container publish path.
- No self-contained / single-file / trimming / full NativeAOT — out of scope for this small unit (trimming in
  particular is unsafe against the reflection-heavy feature catalog).

## Measurement

Recorded in `research.md`: publish-output **size** A/B (deterministic, load-independent) for the shipped
`linux-x64` RID, R2R build success across the full ~90-assembly graph, and a best-effort host-build / Kestrel
phase A/B via the spec-129 boot instrument, annotated with the 1-minute load average and swapped run order. Under
the capture machine's heavy fleet load the wall numbers are indicative only; the deterministic signals are the
size delta and the successful per-RID R2R publish.

## Success criteria (this unit)

1. The container image publishes with R2R for both shipped RIDs (`linux-x64`, `linux-arm64`) with no build break.
2. The published output runs: app starts and the health endpoint (`/`) responds.
3. The size cost and the (load-caveated) startup delta are measured and reported honestly, with a ship/no-ship
   recommendation.
4. No dev/local build is slowed (R2R is Dockerfile-scoped).

## Linked surfaces

- Publish path: `src/Apps/Elsa.Server/Dockerfile`
- Multi-arch build: `.github/workflows/docker.yml`
- Baseline / instrument: `docs/reports/cold-start-readiness-2026-07.md`, `src/Apps/Elsa.Server/Boot/`
- Recipe: `tools/performance/measure-cold-start.sh`
- Bucket: `docs/program-goals/first-request-cold-start-readiness.md`
