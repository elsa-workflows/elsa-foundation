# Implementation Plan: Diagnostics — Structured Logs (Capture, Live Streaming & Query)

**Branch**: `sfmskywalker/port-diagnostics-modules` | **Date**: 2026-06-18 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/073-diagnostics-structured-logs/spec.md`

## Summary

Port the structured-logs diagnostics capability from elsa-core (`Elsa.Diagnostics.StructuredLogs`)
into elsa-foundation, adapted to foundation architecture. This slice delivers: capture of the host's
`Microsoft.Extensions.Logging` events via an `ILoggerProvider`, an in-memory bounded ring buffer
(recent history), a multi-subscriber live feed with backpressure/drop signalling, and an HTTP surface (`recent`, `sources`, and an SSE `stream`) gated behind a
host-overridable, default-permissive
diagnostics authorization policy. Durable persistence is explicitly a later slice that implements the
same `.Core` contracts.

Technical approach: a two-package domain (`Elsa.Diagnostics.StructuredLogs.Core` contracts/models +
`Elsa.Diagnostics.StructuredLogs` feature/impl) registered as a CShells `IShellFeature`. The live
transport is **Server-Sent Events (SSE)** — the live feed is a `text/event-stream` HTTP endpoint that
streams an `IAsyncEnumerable<StructuredLogEntry>`; `recent` and `sources` are plain HTTP GETs. All
three surfaces are FastEndpoints, auto-mapped via the existing `app.MapShells()`, so there is **no
host hub wiring** in `Program.cs` (the host only adds the feature assembly to the CShells list). SSE
was chosen over SignalR because the workload is one-way server→client browser streaming: native
`EventSource` gives auto-reconnect and `Last-Event-ID` resume, with zero new dependencies on either
side (the studio drops `@microsoft/signalr` for this tab). The existing Console tab keeps its
third-party `ConsoleLogStreaming` SignalR transport untouched; unifying Console onto SSE is a
deliberate, separate follow-up (see research R2).

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), nullable + implicit usings enabled (repo default).

**Primary Dependencies**:
- `.Core`: `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`,
  `Microsoft.Extensions.Primitives` only (§2.1 layer-1 envelope).
- Feature: `CShells.Abstractions`, `CShells.FastEndpoints` (+ `.Abstractions`),
  `Elsa.Api.FastEndpoints` (project ref), `Microsoft.Extensions.Logging`,
  `Microsoft.AspNetCore.Http`, `Microsoft.AspNetCore.Authorization`,
  `Elsa.Platform.PackageManifest.Generator` (PrivateAssets). **No SignalR, no `FrameworkReference`,
  no protobuf** — SSE is plain HTTP over the pinned ASP.NET HTTP packages the `Elsa.Http` domain
  already uses.

**Storage**: In-memory bounded ring buffer only this slice (default impl of `IStructuredLogStore`).
EFCore-based durable persistence deferred to a sibling slice implementing the same `.Core` contracts.

**Testing**: xUnit (`xunit` 2.9.0) + `Microsoft.NET.Test.Sdk`, matching existing
`tests/Elsa/**/Tests` projects. No integration tests (§2.23.6).

**Target Platform**: ASP.NET Core server hosts (Elsa.Server now; host-agnostic feature).

**Project Type**: Backend .NET class libraries + a CShells feature contributing an ASP.NET surface.

**Performance Goals**: Live entry visible to a subscriber < 1s under normal load (SC-001); recent
query returns within the same interaction (SC-002).

**Constraints**: Diagnostics memory bounded by configured capacity under burst (SC-003); capture MUST
NOT throw out of the logging path nor create a feedback loop (FR-010); no behavioural change when
disabled (SC-005, FR-007).

**Scale/Scope**: Single local source per host (v1); bounded buffer default ~2,000 entries (aligns
with the console-stream host defaults); concurrent live subscribers.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Gate | Requirement | Status / How met |
|---|---|---|
| §2.1 Three-layer per feature | Core contracts + feature impl; Core envelope is MS abstractions only | PASS — `Elsa.Diagnostics.StructuredLogs.Core` (contracts/models/options) + `Elsa.Diagnostics.StructuredLogs` (capture, store, live feed, SSE + HTTP endpoints, feature class). |
| §2.2 Domain-only naming | No `Features.*`/`Modules.*` segments; `.Core` + bare impl + `Feature` type suffix allowed | PASS — packages `Elsa.Diagnostics.StructuredLogs[.Core]`; type `StructuredLogsFeature`. |
| §2.17 Duplication beats dependency / §2.20 rule-1 | No premature umbrella / provider stubs | PASS — single in-memory impl in the feature package; no empty `.Api`/`.Persistence` stubs created now. |
| §2.19 Feature identity | Stable `name`, used as options binding key, unique | PASS — `name: "DiagnosticsStructuredLogs"`; options bound under it. |
| §2.20 Provider decomposition | Persistence behind `.Core` contracts; in-memory is the default impl; provider split deferred | PASS — `IStructuredLogStore` is the seam; EFCore providers are a later slice. |
| §2.20 rule-2 Dependency envelope | No meta-packages; envelope reflects use | PASS — SSE uses only the pinned `Microsoft.AspNetCore.Http`; no SignalR/shared-framework/protobuf pulled in. |
| §2.22.1 Domain extension-points catalog | Domain `EXTENSION_POINTS.md` | PLANNED — add `src/Elsa/Diagnostics/StructuredLogs/EXTENSION_POINTS.md`. |
| §2.22.2 Repo-wide index | Update root `EXTENSION_POINTS.md` | PLANNED — add Diagnostics row. |
| §2.23.1 Feature-registration test | Build SP, assert every registered service resolves | PLANNED — `StructuredLogsFeatureTests`. |
| §2.23.2 Per-impl branch-covered tests | Each logic-bearing impl, every branch | PLANNED — store eviction, filter matching, capture mapping/cap, live feed drop/backpressure. |
| §2.23.3 Visibility | Feature class `public` non-sealed; impls `public sealed` | PASS by construction. |
| §2.23.5 Exception boundaries | Wrap infra exceptions in domain exceptions; never throw out of log path | PASS — capture path is guarded (FR-010); domain exception type(s) in `.Core`. |

**Result**: No violations requiring Complexity Tracking. The SSE transport adds no new dependency
(plain HTTP over the already-pinned `Microsoft.AspNetCore.Http`), so the dependency envelope stays
minimal (§2.20 rule-2).

## Project Structure

### Documentation (this feature)

```text
specs/073-diagnostics-structured-logs/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (HTTP + SSE + DI contracts)
│   └── structured-logs.md
├── checklists/
│   └── requirements.md  # (from /speckit-specify; re-validated in /speckit-clarify)
└── tasks.md             # /speckit-tasks output (NOT created here)
```

### Source Code (repository root)

```text
src/Elsa/Diagnostics/StructuredLogs/
├── Core/
│   └── Elsa.Diagnostics.StructuredLogs.Core.csproj   # contracts, models, options, domain exceptions
│       ├── Models/        StructuredLogEntry, LogSource, StructuredLogFilter, DroppedEntriesSignal
│       ├── Contracts/     IStructuredLogStore, IStructuredLogSink, IStructuredLogLiveFeed,
│       │                  IStructuredLogSourceProvider
│       ├── Options/       StructuredLogsOptions
│       └── Exceptions/    StructuredLogsException (+ specifics)
├── EXTENSION_POINTS.md
└── Elsa.Diagnostics.StructuredLogs.csproj            # feature impl + ASP.NET surface
    ├── StructuredLogsFeature.cs                       # IShellFeature (name "DiagnosticsStructuredLogs")
    ├── Capture/    StructuredLogCaptureProvider (ILoggerProvider) + StructuredLogCapturingLogger
    ├── Storage/    InMemoryStructuredLogStore (ring buffer; default IStructuredLogStore + live feed)
    ├── Sources/    LocalStructuredLogSourceProvider
    └── Endpoints/  RecentEndpoint, SourcesEndpoint, StreamEndpoint (FastEndpoints; Stream = SSE)

tests/Elsa/Diagnostics/StructuredLogs/Tests/
└── Elsa.Diagnostics.StructuredLogs.Tests.csproj
```

**Structure Decision**: Two packages under a new `Diagnostics` domain root, following the
`Elsa.Locking` precedent (Core + feature). The ASP.NET surface is three FastEndpoints (`recent`,
`sources`, and the SSE `stream`), all auto-mapped by the existing `app.MapShells()`. There is **no**
host wiring beyond adding the feature assembly to the CShells list — no `MapStructuredLogStreaming()`
hub extension is needed (that was only required under the rejected SignalR option). Endpoints live in
the feature package (not a premature `.Api` split, §2.20 rule-1).

## Complexity Tracking

> No constitution violations to justify. (SSE is plain HTTP over the already-pinned
> `Microsoft.AspNetCore.Http`, so no new dependency envelope; the in-memory store is the default impl
> of a `.Core` contract, so the persistence-provider split is correctly deferred per §2.20 rule-1.)
