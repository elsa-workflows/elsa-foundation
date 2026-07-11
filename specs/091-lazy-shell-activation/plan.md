# Implementation Plan: Observable Shell Readiness and Cold Activation

**Branch**: `codex/624-shell-readiness` | **Date**: 2026-07-12 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/091-lazy-shell-activation/spec.md`

## Summary

Move default-shell preparation off the first workflow request and behind an honest, non-blocking readiness contract. Start one background warmup after the process is listening, expose excluded root liveness/readiness paths, instrument the owned activation phases and every startup task, and add an isolated cold-boot harness. The first measured optimization is a Groundwork SQLite exact-schema-history fast path: when the current manifest/provider tuple is already committed, open the existing store without rebuilding all portable indexes; operators retain an explicit rematerialization repair knob.

## Technical Context

**Language/Version**: C# / .NET 10; Bash for controlled cold-boot measurement

**Primary Dependencies**: ASP.NET Core hosted services/minimal endpoints, CShells lifecycle and routing, System.Diagnostics activities/metrics, Elsa Tasks, Groundwork Documents + Groundwork SQLite

**Storage**: Existing Groundwork SQLite runtime database and EF Core SQLite design/diagnostic stores; no new persistent entity

**Testing**: xUnit; existing Modularity, Tasks, Groundwork SQLite, Activities HTTP integration, lifecycle/isolation, and Architecture projects

**Target Platform**: ASP.NET Core server on macOS/Linux/Windows; measurement command targets macOS/Linux Bash environments

**Project Type**: Modular .NET web-service foundation plus opt-in performance tooling

**Performance Goals**: Cold shell-ready p95 at least 30% below baseline and ≤30 s on the reference lane; first workflow after ready p95 ≤750 ms; warm workflow p95 remains ≤50 ms

**Constraints**: Health probes must not trigger shell resolution; preparation must be stampede-safe and cancellation-aware; active means startup tasks and route refresh completed; shell isolation/reload semantics and database materialization correctness cannot regress; ordinary CI does not enforce wall-clock budgets

**Scale/Scope**: One root-hosted readiness/warmup composition, startup/provider telemetry, one SQLite materialization fast path and repair knob, one cold-boot harness, focused unit/integration/architecture evidence

## Constitution Check

*GATE: Passed before research and re-checked after design. Both constitutions remain draft; this plan treats their applicable gates as binding while calling out deferred configuration classification.*

| Gate | Status | Evidence |
|---|---|---|
| Framework §2.1 / §2.20 layering and provider decomposition | PASS | Root readiness stays in the application host; task telemetry stays in Elsa.Tasks; the schema-history optimization stays inside the SQLite provider leaf. No provider detail enters a `.Core`. |
| Framework §2.5.1 lifetimes | PASS | Readiness state and telemetry instruments are process-static singletons; the warmup executes host lifecycle logic once; scoped startup tasks remain scoped. |
| Framework §2.6.2 replacement semantics | PASS | Existing shell registry, route table, and document-store selections remain authoritative. No competing implementation is registered. |
| Framework §2.12 / Elsa §E4 configuration status | PASS WITH DRAFT NOTE | Warmup and rematerialization settings are explicitly host/provider settings. The plan makes no claim about tenant or workflow scope while classification is deferred. |
| Framework §2.15 foundation composition | PASS | Honest readiness and default local-provider startup behavior belong in the reference foundation host. No external service is introduced. |
| Framework §2.21 / §2.23 test discipline | PASS | Existing lifecycle, isolation, route, persistence, and HTTP tests remain. New logic-bearing classes receive branch-complete unit tests plus real-host integration coverage. |
| Framework §2.22 documentation | PASS | Operator paths, settings, diagnostics, benchmark use, rollback, and the warmup task are documented in the work unit and performance report. |
| Framework §2.24 sanctioned patterns | PASS | Hosted lifecycle composition and provider-specific adapter logic reuse existing framework seams; no new cross-feature composition pattern is introduced. |
| Elsa §E2.2 Design/Runtime split | PASS | Root readiness observes shell lifecycle; the SQLite provider optimization concerns runtime persistence only and introduces no Runtime → Design dependency. |
| Elsa §E2.4 foundation composition | PASS | The reference host eagerly prepares only its configured default shell and preserves independently activated tenant shells. |
| Elsa §E6 naming | PASS | Proposed names (`DefaultShellWarmup`, `ShellReadinessState`, `StartupTaskTelemetry`) fit the component budget and use concrete role nouns. |
| Durability/materialization invariant | PASS | The fast path is allowed only after an exact manifest identity/version and provider name/version tuple is durably recorded; a force-rematerialize setting provides repair and rollback. |

## Phase 0 Research Decisions

Research conclusions are recorded in [research.md](./research.md). The decisive findings are:

- CShells is deliberately lazy: the first shell-routed request currently pays discovery, composition, provider initialization, startup tasks, route initialization, and endpoint mapping.
- The current clean baseline is 43.500964 s to default-shell activation and 0.610067 s for the first successful hello-world response after activation.
- Liveness/readiness paths must be excluded from CShells routing; otherwise the probe itself activates the default shell.
- Background preparation starts after `ApplicationStarted`; readiness observes state and `IShellRegistry.GetActive` without awaiting or triggering activation.
- Owned activities and bounded task-type metrics provide honest phase attribution; opaque CShells composition remains a coarse named phase rather than invented precision.
- Groundwork's current SQLite factory replans and backfills declared indexes on every open even when the exact schema-history tuple is already committed. This is the first measured material bottleneck to remove.

## Phase 1 Design

### Root readiness and warmup

Add a process-root `ShellReadinessState` and `DefaultShellWarmup` to the reference server. The background warmup waits for `ApplicationStarted`, records one attempt, explicitly warms the runtime feature catalog, then calls the existing stampede-safe `IShellRegistry.GetOrActivateAsync` for the configured default shell. Cancellation or failure updates stable diagnostic state and is logged; it does not crash Kestrel.

Map `/health/live` and `/health/ready` as root endpoints and add both to CShells web-routing exclusions. Liveness always represents the process. Readiness performs no activation: it returns 200 only when `IShellRegistry.GetActive` exposes an Active default generation; otherwise it immediately returns 503 with the bounded warmup status/code. This naturally follows successful external activation and reloads without a sticky process-global ready bit.

Configuration under the root `Elsa:Readiness` section provides `WarmDefaultShell` (default true) and `DefaultShellName` (default `default`). Disabling warmup restores lazy activation while readiness remains an honest observer.

### Activation telemetry

Use stable activity/meter names and bounded tags:

- host warmup total, feature-catalog discovery, and shell activation remainder;
- each startup task, tagged by registered task type, outcome, and single-node skip state;
- Groundwork SQLite provider initialization, tagged by `materialized` versus `history-hit`;
- existing `UpdateRouteTableStartupTask` appears as its own measured startup task; route count is added at the synchronizer boundary where available.

Activities carry detailed correlation; histograms carry durations with a bounded task-type dimension; structured completion logs make the local benchmark usable without a telemetry backend. Payloads, connection strings, shell secrets, workflow IDs, and exception messages are not metric dimensions.

### Groundwork SQLite fast open

Before full materialization, inspect `groundwork_schema_history` using the same SQLite connection. If an exact row exists for manifest identity, manifest version, provider name, and provider version, open `SqliteDocumentStore` directly and retain the connection in a normal `SqliteDocumentStoreHandle`. Missing history/table or `RematerializeOnStartup = true` uses the existing `SqliteDocumentStoreFactory.CreateAsync` path unchanged.

The feature setting defaults to false because the committed schema-history tuple is the migration authority. Operators can force the current full behavior for repair, validation, or diagnostics. Concurrent processes cannot observe an uncommitted history row, so the fast path never races ahead of materialization commit.

### Cold-boot evidence

Add `tools/performance/measure-server-cold-start.sh`. Each boot runs a prebuilt Release DLL from a fresh mutable content root cloned from frozen SQLite snapshots, detects the listening socket without making a shell-routed request, polls the excluded readiness path, invokes the workflow once, validates exact status/body, terminates the process, and records raw timings and provenance. JSON and Markdown reports include nearest-rank p50/p95 and optional budgets. Failures retain the boot log and mutable directory.

Wall-clock thresholds remain opt-in. CI proves readiness semantics, stampede behavior, phase observations, schema-history selection, workflow behavior, and shell lifecycle deterministically.

## Project Structure

### Documentation (this feature)

```text
specs/091-lazy-shell-activation/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── shell-readiness.md
├── checklists/
│   └── requirements.md
└── tasks.md
```

### Source Code (repository root)

```text
src/Apps/Elsa.Server/
├── Program.cs
├── Readiness/
│   ├── DefaultShellWarmup.cs
│   ├── ShellReadinessOptions.cs
│   ├── ShellReadinessSnapshot.cs
│   └── ShellReadinessState.cs
└── appsettings.json

src/Elsa/Tasks/
├── Diagnostics/StartupTaskTelemetry.cs
└── Services/TaskManager.cs

src/Elsa/Persistence/Groundwork/Sqlite/
├── SqliteGroundworkDocumentStoreInitializer.cs
├── SqliteGroundworkRuntimePersistenceShellFeature.cs
└── DependencyInjection/SqliteGroundworkDocumentStoreRegistration.cs

tests/Elsa/Modularity/Tests/
└── ServerReadinessTests.cs

tests/Elsa/Tasks/Tests/
└── StartupTaskTelemetryTests.cs

tests/Elsa/Persistence/Groundwork/Sqlite/Tests/
└── SqliteGroundworkInitializationTests.cs

tests/Elsa/Architecture/
└── ArchitectureGuardTests.cs

tools/performance/
└── measure-server-cold-start.sh

docs/reports/
└── shell-activation-performance-2026-07.md
```

**Structure Decision**: Keep host-specific operational readiness in Elsa.Server, generic startup-task observability in Elsa.Tasks, and schema-history mechanics in the existing SQLite provider leaf. No new project or public `.Core` contract is justified.

## Post-Design Constitution Re-check

All pre-research gates remain passing. The design adds no package, persistence entity, cross-domain implementation dependency, Runtime → Design reference, or new composition mechanism. The host owns readiness policy; the provider leaf owns its optimization; activities/metrics are diagnostic side effects and never gate correctness. The only provisional area is root/provider configuration classification, already marked host-scoped pending the constitution's deferred configuration decision.

## Complexity Tracking

No constitutional violations or exceptions are required.
