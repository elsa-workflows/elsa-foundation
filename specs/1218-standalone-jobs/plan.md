# Implementation Plan: Standalone durable jobs

**Branch**: `agent/codex/3586-background-jobs` | **Date**: 2026-11-06 | **Spec**: [spec.md](spec.md)

**Status**: Reviewed implementation plan. ADR0096 domain admission was explicitly adopted by the owner
on 6 November 2026. M1 packages and five standalone cases pass on both profiles at b706023ddd; [the receipt](evidence/m1-packaged-standalone.json) records exact scope. M2 and complete runtime/model/operations qualification remain open.

## Summary

Provide a small independently hosted .NET runner with one job lifecycle authority, reusable scheduling/
persistence mechanics and optional native/BPMN and operations adapters. The same registered handler
runs from an ordinary producer, native workflow and genuine BPMN model. The owner-selected product
contract is preserved in the [spec](spec.md); technical proposals remain [proposed](contracts/job-lifecycle.md).

The [provider/source research](research.md) recommends an Elsa-owned default; it does not select a
third-party implementation or assert runtime qualification. Prioritize the durable pause/start and
result/continuation boundaries before writing a broad API or UI.

## Technical Context

**Language/Version**: repository .NET 10/C#, TypeScript/React Studio.

**Primary Dependencies**: existing Elsa contracts, ordinary Microsoft.Extensions hosting/DI, existing
serialization/identity, EF persistence policy; Cronos only behind the existing schedule implementation.
No Hangfire/Quartz/TickerQ dependency in the proposed default.

**Storage**: job-owned EF contexts and migrations, provider engines selected by host. Initial proposed
proof profiles: SQLite local development/restart; PostgreSQL independent-process cluster behavior.
No other engine guarantee before its tests pass. No new generic store framework or cross-store transaction.

**Testing**: risk-based whole affected xUnit projects; provider processes/containers; immutable local
PackageReference consumer; Playwright on owned composed Workbench/Studio. Existing build-slot wrapper
is mandatory. Application test-first cadence is undeclared; do not infer TDD, but retain required
red/green behavioral bite-proofs and appropriate tests for decisions, persistence and race boundaries.

**Target Platform/Shape**: independent producer and worker on supported .NET hosts; optional combined
Workbench and separate Studio, without runtime dependency on mutable workflow design data.

**Constraints**: bounded payloads/pages/worker resources; no head blocking by held work; distributed
limits count authorized live attempts, with stale physical overlap explicitly disclosed. No exactly-once
external effects, production deployment, credentials or package release.

**Scale/Performance**: prove configured concurrency ceilings, bounded batch/scan/backlog processing and
progress of unrelated eligible work. Do not invent throughput or latency claims without measurements.

## Constitution Check

Both constitutions and [review checklist](../../docs/contributing/review-checklist.md) apply. Current
owning versions on this branch are framework 6.1.0 and Elsa 6.5.0; the AGENTS summary is not the version authority.

| Gate | Proposed treatment | State |
|---|---|---|
| Elsa §E2.1 and framework Governance | [ADR 0096](../../docs/adr/0096-jobs-owns-durable-handler-invocations.md) admits optional Jobs under explicit scoped owner adoption, propagated as Elsa 6.5.0 after retaining ADR0095's Decisions 6.4.0 admission | Passed for domain admission |
| §2.1–§2.4, §2.13–§2.20 | Core only contracts/abstractions; concrete provider/operations adapters; one verb-led purpose; no universal Execution umbrella | Design review clean; M1 standalone closure passed; later adapters still require closure checks |
| §2.5–§2.11 | Public inheritable features; scoped services, declared replacement kinds, diagnosed duplicate handlers and bounded CQS store transitions | Required in implementation |
| §2.17 | Extract only demonstrated mechanics with concrete consumers; preserve existing workflow APIs through adapters | Review extraction before implementation |
| §2.22–§2.23, §4 | Extension catalogs/public API, exception wrapping, risk tests, compatibility/version computation | Required in implementation |
| Elsa §E2.2/§E2.6 | Standalone package closure; artifact-only native/BPMN runtime, separate model state and consumption | Required packaged proof |
| Elsa §E2.8 | Optional native activity goes through ordinary activity catalog; no alternate picker authority | Required composed proof |
| §2.24/Model X/activity versioning | Draft/provisional material is identified, not ratified here | No new authority inferred |
| #3052 boundary | Consume accepted contracts; route amendments to its background/variable/recovery topics separately | Recheck if a concrete adapter changes those semantics |
| Elsa §E7 and delivery gates | New test projects in tiers; locked restore/architecture/maps, current-head review/CI, resulting main | Not run |

The domain exception is explicit and limited to that admission. Independent R3 design review was clean
at `44c22f34f`; apply remaining code/package gates as implementation arrives. Repeat the check after
package design and each material contract change. No runtime/provider/UX acceptance is implied.

## Project Structure

### Documentation

`spec.md`, `research.md`, `data-model.md`, `contracts/job-lifecycle.md`, `acceptance-matrix.md`,
`ux-acceptance.md`, `quickstart.md` and `tasks.md` in this directory. ADR0096 owns domain admission;
[program goal](../../docs/program-goals/standalone-jobs.md) owns sequencing, #3586 owns acceptance/transitions.

### Proposed production paths

```text
Extensions/Jobs/
  src/Core/                              Elsa.Jobs.Core
  src/Elsa.Jobs/                         client, handler registry and independent worker
  src/Persistence/EntityFrameworkCore/    job authority, schema and migrations
  src/Workflows/Runtime/                  optional native adapter
  src/Bpmn/                              optional direct BPMN adapter, only if separate API consumer warrants it
  src/Api/                               optional operations API
  src/Studio/                            optional module, server/client paths follow current extension convention
  tests/Tests/                           cheapest behavior/contract tests
  tests/Persistence/ProviderTests/        real provider transitions/contention
  tests/Integration/                     packaged/native/BPMN journeys
tools/jobs/                              isolated package-consumer and failure qualification harness
```

These are proposed module responsibilities, not a request to create empty projects. Split persistence
contracts into a sibling Core only if actual provider/runtime consumers require it; keep implementation
references within the constitution. Verify existing Studio convention before selecting exact project paths.

## Staged delivery and critical path

1. **M0 — agree the design.** Review spec, provider comparison and exact job-domain amendment; settle
   continuation permission arbitration, version/association identities and linked manual-retry behavior.
   Record concrete shared-source ownership with #3582/#3583/#2892/#3815. Review the resulting task list.
2. **M1 / US1 — standalone vertical slice.** Register/pin one handler, idempotently accept a bounded input,
   execute in a separate packaged worker and retain a useful result; demonstrate lost-ack/restart paths.
   This earns the standalone architecture before expanding the surface.
3. **M2 / US1+US2 — scheduling and operational correctness.** Add delayed/recurring occurrences with
   coalescing/replay policy, all four atomic pause/start gates, leased distributed limits, retry,
   timeout/cancel/shutdown, handover and late receipt retention. Prove races with real PostgreSQL workers.
4. **M3 / US3 — model-owned integration.** Real native saved wait/outbox and direct BPMN input receipt invoke
   the same handler. Prove durable result retention, proposed continuation permission cutoff, parent
   terminal/cancel ordering and duplicate delivery. Preserve coalescing delivered-elsewhere refresh.
5. **M4 / US4 — API and Studio.** Build authorized collections/detail/mutations from the agreed
   [UX acceptance record](ux-acceptance.md); verify actual composed journeys and inspect screenshots/traces.
6. **M5 — full acceptance.** Reconcile every matrix row and original criterion against real packages,
   provider/deployment receipts, negative controls and inspected current-head UI evidence. Complete
   independent review, required checks, approved merge path and resulting-main qualification.

Keep one active implementation leaf and one integration lane. Independent read-only reviews and
non-overlapping tests may run concurrently; shared source mutations remain coordinated. Do not make
the whole messaging, trigger, StateMachine or Unified Execution product a prerequisite.

## Key engineering decisions to review

- Use job-owned atomic gate/attempt/permit transactions. A pre-read callback followed by unrelated CAS
  is insufficient. Trust provider time for distributed lease/due decisions; host clocks schedule polling.
- Keep accepted job identity and physical attempt identity separate from fences, results and delivery.
- Materialize recurring occurrences atomically with cursor/coalescing responsibility; processing pause
  holds unmaterialized due responsibility while explicit submissions continue to be accepted.
- Use one durable, replayable permission for the exact model input/result. Proposed continuation cutoff
  allows already granted continuation to finish after pause; it is not a general cross-store transaction.
- Manual retry is proposed as a linked new invocation, visibly preserving prior terminal state and
  avoiding automatic reattachment to a consumed parent result.
- Keep leases and physical stopping distinct. Normal timeout/cancellation retains a local physical slot
  until exit; stale processes after authority loss may overlap new authorized work.

## Validation and review

The [58-case acceptance matrix](acceptance-matrix.md) is the test plan, not evidence. Select the
cheapest meaningful test per risk; use independent process/provider tests for cluster claims, genuine
models for model claims and actual composed Studio for UX. Report whole-suite discovery/execution counts
and bite-proofs for behavior changes. Do not treat fixtures as product acceptance.

Every delivery commit preserves failing/not-run states. A clean review requires independent correctness,
standards and original-acceptance/UX lenses under the current org loop. The final evidence names exact
source/PR heads, artifacts, provider versions/deployment and actual resulting-main checks.

## Complexity Tracking

New domain admission is explicitly adopted in ADR0096, rather than hidden in host Tasks. A general Execution umbrella, synthetic workflow jobs, dual scheduler stores and job-chain
language are rejected because existing domains already own those separate responsibilities.
