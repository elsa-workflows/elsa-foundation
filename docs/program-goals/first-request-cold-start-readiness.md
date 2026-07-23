# First-Request / Cold-Start Readiness

Status: active.

Area: host boot and first-request latency (engine performance, phase 4, track 2).

Steward(s): Sipke plus active performance/runtime agents.

## Purpose

Turn the known 8–75 s first-request cold start into an attributed, measured, and then optimized property of the
reference `Elsa.Server` host, so a freshly started container serves its first authenticated call and first
workflow execution in single-digit seconds with no mid-activation contention tail.

This bucket is the successor track that `docs/reports/runtime-http-performance-2026-07.md` explicitly deferred:
that work optimized *warm* steady-state latency (checkpoint coalescing) and left boot cost to a separate track.
This is that track. It is distinct from — and a dependency of — [Workspace Launch
Readiness](workspace-launch-readiness.md): a launchable workspace must also *start fast*, not only be navigable.

## Program (5 units)

1. **Instrument (this unit — spec 129).** Opt-in `Elsa.Boot` phase-timing diagnostic + deterministic schema
   op-count baseline + measurement recipe + baseline report. Measurement only. Gates units 2–5.
2. **ReadyToRun publish.** Add R2R (and evaluate TieredPGO) to `src/Apps/Elsa.Server/Dockerfile` to cut JIT
   cost. Sized by the instrument's host-build / first-request JIT share.
3. **Schema batch / skip-if-current.** Reduce the 873-operation fresh-DB admission and skip it entirely on an
   already-current database. Constraint: the locked apply protocol
   (`specs/094-harden-groundwork-stores/contracts/storage-composition.md:158`) keeps approval semantics; the
   frozen `SchemaVersion` is **not** a lever — skip-if-current needs a separate applied-plan fingerprint.
4. **Opt-in eager activation — IMPLEMENTED (spec 132).** A host-side `IHostedService`
   (`EagerShellActivationHostedService`) that triggers shell activation at startup (behind
   `Elsa:Boot:EagerShellActivation:Enabled`, default OFF) so the activation cliff — and the mid-activation
   contention tail — is paid before the first user request, not during it. Uses the public
   `IShellRegistry.GetOrActivateAsync` (the exact call `ShellMiddleware` makes on a cold request → byte-identical
   shell state), so no CShells edit and no synthetic request are needed. Supports "all configured shells"
   (default/`*`) and named-shell config shapes; a many-shell host pays every activation at boot only when it opts
   in (the documented trade). Demo default remains OFF; flipping it is a separate decision. See
   `specs/132-eager-shell-activation/`.
5. **Warmups.** Targeted first-use warmups (JIT/route-table/serializer/connection) for whatever residual
   first-request cost remains after units 2–4.

EF-initializer consolidation is **not** in this bucket — it belongs to [Zero-EF Persistence](zero-ef-persistence.md).

## Program success criteria (targets for units 2–5)

- Cold boot → healthy < 5 s (with R2R).
- First authenticated call < 3 s (from 8–24 s+ today).
- First workflow execute: single-digit-second p100 with no contention tail.
- Warm-boot schema phase < a few hundred ms (skip-if-current on an unchanged database).

## In scope

- Boot / first-request phase attribution and reproducible baselines.
- R2R/TieredPGO publish, schema admission batching / skip-if-current, opt-in eager activation, first-use warmups.
- The upstream CShells hook proposal needed for per-initializer attribution (spec 129 finding).

## Out of scope

- Warm steady-state latency (done in `runtime-http-performance-2026-07`; ADR 0031/0032).
- EF-provider consolidation (Zero-EF bucket).
- First-user navigability / docs / tour (Workspace Launch Readiness bucket).

## Active objectives

1. Land spec 129: instrument, deterministic op count, recipe, baseline report. **(this unit)**
2. Size units 2–5 from the baseline report and pick the next unit by measured share.
3. File the upstream CShells `IShellInitializerObserver` proposal so unit 2 can attribute per-initializer cost.
   **Done** — `docs/reports/cshells-initializer-observer-proposal.md` (filed by spec 132).

## Linked surfaces

- Spec: `specs/129-cold-start-phase-instrument/` (unit 1), `specs/132-eager-shell-activation/` (unit 4)
- Report: `docs/reports/cold-start-readiness-2026-07.md`
- Upstream proposal: `docs/reports/cshells-initializer-observer-proposal.md`
- Eager activation: `src/Apps/Elsa.Server/Boot/EagerShellActivation*.cs`
- Charter/precedent: `docs/reports/runtime-http-performance-2026-07.md`
- Instrument: `src/Apps/Elsa.Server/Boot/`, `src/Apps/Elsa.Server/Program.cs`
- Recipe: `tools/performance/measure-cold-start.sh`
- Deterministic guard: `tests/Elsa/Persistence/Groundwork/Tests/ColdStartSchemaOperationCountTests.cs`

## Removal or completion conditions

Complete when units 2–5 land and the program success criteria are met on the reference container (or the numbers
are re-scoped with evidence). Until then keep this bucket active and route each unit through it.
