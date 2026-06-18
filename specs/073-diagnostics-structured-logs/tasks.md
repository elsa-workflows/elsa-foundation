---
description: "Task list for Diagnostics — Structured Logs (spec 073)"
---

# Tasks: Diagnostics — Structured Logs (Capture, Live Streaming & Query)

**Input**: Design documents from `specs/073-diagnostics-structured-logs/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/structured-logs.md, quickstart.md

**Tests**: Unit tests are **REQUIRED** here — the framework constitution mandates a feature-registration
test (§2.23.1) and branch-covered per-implementation tests (§2.23.2). They are not optional for this
feature. Integration tests are out of scope (§2.23.6); end-to-end checks live in `quickstart.md`.

**Organization**: Tasks are grouped by user story. The shared capture+store engine is foundational
(every story needs it); each story adds its own HTTP/SSE surface and tests.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 (live-tail), US2 (recent history), US3 (filtering)
- All paths are repository-relative.

## Path Conventions

- Core library: `src/Elsa/Diagnostics/StructuredLogs/Core/`
- Feature library: `src/Elsa/Diagnostics/StructuredLogs/`
- Tests: `tests/Elsa/Diagnostics/StructuredLogs/Tests/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the new `Diagnostics` domain projects and wire them into the build.

- [X] T001 Create the Core project `src/Elsa/Diagnostics/StructuredLogs/Core/Elsa.Diagnostics.StructuredLogs.Core.csproj` (net10.0, nullable + implicit usings; package refs limited to `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Primitives` per §2.1).
- [X] T002 Create the feature project `src/Elsa/Diagnostics/StructuredLogs/Elsa.Diagnostics.StructuredLogs.csproj` (net10.0; package refs `CShells.Abstractions`, `CShells.FastEndpoints`, `CShells.FastEndpoints.Abstractions`, `Microsoft.AspNetCore.Http`, `Microsoft.AspNetCore.Authorization`, `Microsoft.Extensions.Logging`, `Elsa.Platform.PackageManifest.Generator` PrivateAssets; project refs to Core and `src/Elsa/Api/FastEndpoints/Elsa.Api.FastEndpoints.csproj`; add the nested-folder `Compile Remove` glob for `Core/**` mirroring `Elsa.Http.csproj`).
- [X] T003 Create the test project `tests/Elsa/Diagnostics/StructuredLogs/Tests/Elsa.Diagnostics.StructuredLogs.Tests.csproj` (xunit, `Microsoft.NET.Test.Sdk`, `coverlet.collector`; project refs to the Core and feature projects) following an existing `tests/Elsa/**/Tests` csproj.
- [X] T004 Add all three new projects to the solution file(s) and confirm `dotnet build` resolves them (no code yet).

**Checkpoint**: Projects compile empty and are part of the solution.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The shared Core contracts/models and the capture→store engine every user story depends on.

**⚠️ CRITICAL**: No user-story endpoint work can begin until this phase is complete.

### Core models, contracts, options, exceptions (`...StructuredLogs.Core`)

- [X] T005 [P] Create models `StructuredLogEntry`, `LogProperty`, `LogScope`, `LogExceptionInfo` in `src/Elsa/Diagnostics/StructuredLogs/Core/Models/` per data-model.md (immutable/`public sealed`).
- [X] T006 [P] Create `LogSource` model in `src/Elsa/Diagnostics/StructuredLogs/Core/Models/LogSource.cs`.
- [X] T007 [P] Create `StructuredLogFilter` (with `bool Matches(StructuredLogEntry)`) and `DroppedEntriesSignal` in `src/Elsa/Diagnostics/StructuredLogs/Core/Models/`.
- [X] T008 [P] Create contracts `IStructuredLogStore`, `IStructuredLogLiveFeed` (`Subscribe` yields `StructuredLogStreamItem` envelopes), `IStructuredLogSink`, `IStructuredLogSourceProvider` in `src/Elsa/Diagnostics/StructuredLogs/Core/Contracts/`, plus the `StructuredLogStreamItem` envelope (Entry|Dropped, factory helpers) in `Core/Models/` per data-model.md.
- [X] T009 [P] Create `StructuredLogsOptions` (MinimumLevel, BufferCapacity, SubscriberQueueCapacity, MaxRecentQuerySize, MaxCapturedProperties, MaxCapturedScopeDepth, MaxPropertyValueLength, RecentPath, SourcesPath, StreamPath) in `src/Elsa/Diagnostics/StructuredLogs/Core/Options/StructuredLogsOptions.cs`.
- [X] T010 [P] Create domain exception(s) `StructuredLogsException` (+ any specific subtype) in `src/Elsa/Diagnostics/StructuredLogs/Core/Exceptions/` for §2.23.5 boundary wrapping.

### Engine implementations (`...StructuredLogs`, all `public sealed`)

- [X] T011 Implement `InMemoryStructuredLogStore` in `src/Elsa/Diagnostics/StructuredLogs/Storage/InMemoryStructuredLogStore.cs` — ring buffer (`BufferCapacity` eviction), `Append`, `GetRecent(filter)` (newest-aligned, `MaxCount` clamped to `MaxRecentQuerySize`), implements `IStructuredLogStore` + `IStructuredLogSink`; thread-safe (depends on T005-T009).
- [X] T012 Extend `InMemoryStructuredLogStore` to implement `IStructuredLogLiveFeed.Subscribe(filter, ct)` — per-subscriber bounded channel (`SubscriberQueueCapacity`), yields `StructuredLogStreamItem` envelopes: matching entries as `ForEntry`, and a `ForDropped(DroppedEntriesSignal)` with cumulative count when the channel overflows (drop-oldest); never blocks the producer (FR-006; depends on T011).
- [X] T013 [P] Implement `LocalStructuredLogSourceProvider` in `src/Elsa/Diagnostics/StructuredLogs/Sources/LocalStructuredLogSourceProvider.cs` (single local source: service/machine/process; `GetLocalSource`, `GetKnownSources`).
- [X] T014 Implement capture `StructuredLogCaptureProvider : ILoggerProvider` + `StructuredLogCapturingLogger` in `src/Elsa/Diagnostics/StructuredLogs/Capture/` — map level/category/timestamp/message/template/properties/scopes/exception into `StructuredLogEntry` with caps (MaxCapturedProperties/MaxCapturedScopeDepth/MaxPropertyValueLength), stamp `SourceId`, forward to `IStructuredLogSink`; ignore own category and swallow sink failures so it never throws or loops on the log path (FR-010; depends on T011, T013).

### Feature registration + authorization (`...StructuredLogs`)

- [X] T015 Implement `StructuredLogsFeature : IShellFeature` in `src/Elsa/Diagnostics/StructuredLogs/StructuredLogsFeature.cs` — `public` non-sealed, `[ShellFeature(name: "DiagnosticsStructuredLogs", ...)]` + `[Manifest*]` attrs (the stable name is also the FR-012 capability-detection key); `ConfigureServices` binds `StructuredLogsOptions`, registers the store as `IStructuredLogStore`+`IStructuredLogLiveFeed`+`IStructuredLogSink` (singleton), `IStructuredLogSourceProvider`, the `ILoggerProvider`, and the named default-permissive `Diagnostics:StructuredLogs` authorization policy (depends on T011-T014).

### Foundational tests (constitution-required)

- [X] T016 [P] §2.23.1 feature-registration test `StructuredLogsFeatureTests` in `tests/.../Tests/StructuredLogsFeatureTests.cs` — build the `IServiceProvider` from `StructuredLogsFeature.ConfigureServices` and assert every registered service (store/feed/sink, source provider, `ILoggerProvider`, options, authz policy) resolves.
- [X] T017 [P] §2.23.2 branch tests `StructuredLogFilterTests` for `Matches` (each criterion present/absent; min-level boundary at/above/below) in `tests/.../Tests/StructuredLogFilterTests.cs`.
- [X] T018 [P] §2.23.2 branch tests `InMemoryStructuredLogStoreTests` — append, eviction at capacity, `GetRecent` newest-aligned + `MaxCount` clamp + filter applied, `Subscribe` delivery, backpressure drop emits `DroppedEntriesSignal`, producer never blocks (depends on T011-T012).
- [X] T019 [P] §2.23.2 branch tests `StructuredLogCaptureProviderTests` — field mapping, property/scope/value caps, exception mapping (nested, depth bound), own-category ignored (loop-safety), sink-failure swallowed without throw (depends on T014).
- [X] T020 [P] §2.23.2 branch tests `LocalStructuredLogSourceProviderTests` — local source shape, known-sources set (depends on T013).

**Checkpoint**: The capture→store→feed engine is registered, branch-tested, and resolvable. User-story endpoints can now be built.

---

## Phase 3: User Story 1 — Live-tail structured logs (Priority: P1) 🎯 MVP

**Goal**: A client subscribed to the SSE stream receives structured entries in real time as the host
logs, with reconnect/resume.

**Independent Test**: Connect an `EventSource` to the `stream` path, emit a log, confirm an
`event: entry` arrives with the entry fields and an `id:` line; drop/restore the connection and
confirm it resumes.

### Implementation for User Story 1

- [X] T021 [US1] Implement `StreamEndpoint` (SSE) in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StreamEndpoint.cs` — FastEndpoint at `StreamPath`, sets `Content-Type: text/event-stream`, subscribes via `IStructuredLogLiveFeed`, writes each entry as `id: <sequence>\nevent: entry\ndata: <json>\n\n`, writes `event: dropped` for `DroppedEntriesSignal`, emits `: keep-alive` heartbeats, requires the `Diagnostics:StructuredLogs` policy (depends on T012, T015).
- [X] T022 [US1] Implement `Last-Event-ID` resume in `StreamEndpoint` — read the header, replay buffered entries after that sequence from the store before live streaming (Acceptance 1.2; depends on T021).
- [X] T023 [US1] Implement `StructuredLogEntrySerializer` (`public sealed`) in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/StructuredLogEntrySerializer.cs` — serializes `StructuredLogEntry` to the contract JSON shape (camelCase; properties/scopes/exception, including the null/empty branches for each) used by `data` lines, reused by US2 (depends on T005).

### Tests for User Story 1 (constitution-required)

- [X] T024 [P] [US1] §2.23.2 branch tests `StreamEndpointTests` — SSE framing (`id`/`event`/`data`), entry + dropped events, `Last-Event-ID` resume path, unauthorized rejected when policy tightened (depends on T021-T023).
- [X] T024a [P] [US1] §2.23.2 branch tests `StructuredLogEntrySerializerTests` — assert JSON shape/casing and each conditional branch (properties present/empty, scopes present/empty, exception present/absent, message template present/absent) (depends on T023).

**Checkpoint**: Live SSE tail works and is branch-tested — MVP deliverable.

---

## Phase 4: User Story 2 — Inspect recent history on connect (Priority: P1)

**Goal**: A client can fetch the most-recent entries immediately, independent of any live subscription.

**Independent Test**: Emit N entries, `GET .../recent?take=K`, confirm up to K newest entries return;
exceed buffer capacity and confirm only the most-recent `BufferCapacity` remain.

### Implementation for User Story 2

- [X] T025 [US2] Implement `RecentEndpoint` in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/RecentEndpoint.cs` — FastEndpoint GET at `RecentPath`, calls `IStructuredLogStore.GetRecent`, returns the contract JSON array (reusing the T023 serializer), requires the policy, returns a domain-scoped `400` on invalid input (§2.23.5; depends on T011, T015, T023).

### Tests for User Story 2 (constitution-required)

- [X] T026 [P] [US2] §2.23.2 branch tests `RecentEndpointTests` — newest-aligned, `take` clamp to `MaxRecentQuerySize`, empty result, invalid-input 400, unauthorized rejected when policy tightened (depends on T025).

**Checkpoint**: Recent-history query works independently and is branch-tested.

---

## Phase 5: User Story 3 — Filter the stream and history + sources (Priority: P2)

**Goal**: Narrow `recent` and `stream` by minimum level / category / source, and list known sources
for a UI source selector.

**Independent Test**: Emit entries across levels/categories; query/subscribe with filters and confirm
only matches return; `GET .../sources` returns the local source.

### Implementation for User Story 3

- [X] T027 [US3] Parse `minLevel`/`category`/`source` query params into a `StructuredLogFilter` in both `RecentEndpoint` and `StreamEndpoint` (shared request-binding helper), passing it through to the store/feed (depends on T021, T025).
- [X] T028 [P] [US3] Implement `SourcesEndpoint` in `src/Elsa/Diagnostics/StructuredLogs/Endpoints/SourcesEndpoint.cs` — FastEndpoint GET at `SourcesPath`, returns `IStructuredLogSourceProvider.GetKnownSources`, requires the policy (depends on T013, T015).

### Tests for User Story 3 (constitution-required)

- [X] T029 [P] [US3] §2.23.2 branch tests for filter binding on `RecentEndpoint`/`StreamEndpoint` — each filter param applied, invalid `minLevel` rejected (depends on T027).
- [X] T030 [P] [US3] §2.23.2 branch tests `SourcesEndpointTests` — returns local source, unauthorized rejected when policy tightened (depends on T028).

**Checkpoint**: Filtering + sources work across both surfaces; all three stories independently functional.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Documentation, host enablement, catalog/map updates, and end-to-end validation.

- [X] T031 [P] Write the domain extension-points catalog `src/Elsa/Diagnostics/StructuredLogs/EXTENSION_POINTS.md` (§2.22.1) — list the overridable `.Core` contracts (`IStructuredLogStore`, `IStructuredLogLiveFeed`, `IStructuredLogSink`, `IStructuredLogSourceProvider`) and how to replace them.
- [X] T032 [P] Update the repo-wide `EXTENSION_POINTS.md` index (§2.22.2) with a Diagnostics / Structured Logs row pointing to the domain catalog.
- [X] T033 [P] Write feature documentation `src/Elsa/Diagnostics/StructuredLogs/README.md` (§2.22) — feature name, registered services, options, endpoints (recent/sources/stream), authorization policy, and the SSE event contract.
- [X] T034 Wire the feature into the host: add `typeof(StructuredLogsFeature).Assembly` to the CShells `.WithAssemblies(...)` list in `src/Apps/Elsa.Server/Program.cs` (and add the project reference) so the endpoints auto-map via `app.MapShells()` — no hub mapping needed (depends on T015).
- [X] T035 Refresh the generated maps per AGENTS.md (`bash tools/maps/generate-extension-point-map.sh` + domain/dependency map layers) and review the findings report for drift.
- [X] T036 Run `dotnet build` and `dotnet test tests/Elsa/Diagnostics/StructuredLogs/Tests`; then execute the `quickstart.md` scenarios (live tail, recent, filtering, backpressure, disabled, authorization) against `Elsa.Server`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: no dependencies.
- **Foundational (Phase 2)**: depends on Setup; **blocks all user stories**.
- **User Stories (Phase 3-5)**: each depends only on Foundational; US3 wires into the US1/US2 endpoints (T027 depends on T021 + T025).
- **Polish (Phase 6)**: depends on the stories being complete (T034 needs T015; T036 needs everything).

### User Story Dependencies

- **US1 (P1, MVP)**: after Foundational. Independent.
- **US2 (P1)**: after Foundational. Independent of US1 (shares only the T023 serializer, which is built in US1 — if US2 is done first, build the serializer there).
- **US3 (P2)**: after Foundational; integrates into US1+US2 endpoints (T027) but `SourcesEndpoint` (T028) is independent.

### Within Each Story

- Endpoint implementation before its tests; foundational engine before all endpoints.

### Parallel Opportunities

- Setup: T001-T003 are mostly sequential (refs), T004 last.
- Foundational Core: T005-T010 all `[P]` (different files). T011→T012 sequential (same class). T013, T014 parallel after their deps. Tests T016-T020 all `[P]`.
- Across stories: once Foundational is done, US1, US2, and US3's `SourcesEndpoint` can proceed in parallel; T027 waits for US1+US2 endpoints.
- Polish docs T031-T033 all `[P]`.

---

## Parallel Example: Foundational Core models/contracts

```bash
# After Setup, launch the Core file tasks together:
Task: "T005 models StructuredLogEntry/LogProperty/LogScope/LogExceptionInfo"
Task: "T006 LogSource model"
Task: "T007 StructuredLogFilter + DroppedEntriesSignal"
Task: "T008 contracts IStructuredLogStore/LiveFeed/Sink/SourceProvider"
Task: "T009 StructuredLogsOptions"
Task: "T010 StructuredLogsException"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Phase 1 Setup → 2. Phase 2 Foundational (engine + required tests) → 3. Phase 3 US1 (SSE live tail).
4. **STOP and VALIDATE**: quickstart Scenario 1 — live tail works. This is the MVP.

### Incremental Delivery

1. Setup + Foundational → engine ready.
2. US1 (live tail) → MVP.
3. US2 (recent history) → richer log viewer.
4. US3 (filtering + sources) → focused investigation + source selector.
5. Polish → docs, host wiring, maps, full validation.

---

## Notes

- Tests here are **required** (§2.23), not optional. Every logic-bearing `public sealed` impl gets
  branch-covered tests; the feature class gets a registration test.
- Infrastructure exceptions are wrapped in `StructuredLogsException` at feature boundaries (§2.23.5);
  the capture path never throws into host logging (FR-010).
- `[P]` tasks = different files, no incomplete dependencies. Commit after each logical group.
- No SignalR/hub/protobuf — SSE is plain HTTP; endpoints auto-map via `app.MapShells()`.

## Implementation notes (post-implement)

Branch coverage for the thin endpoints (T024/T029/T030) is delivered against the extracted
`public sealed` logic units rather than via HTTP integration tests (integration is §2.23.6 out of
scope). The endpoint-level tasks map onto these unit tests:

- T024 (SSE framing: `id`/`event`/`data`, entry + dropped) → `StructuredLogSseFormatterTests`.
- T027/T029 (query-param binding; invalid `minLevel`/`take` rejected) → `StructuredLogFilterBinder` +
  `StructuredLogFilterBinderTests`; the endpoints are thin adapters that surface
  `InvalidLogQueryException` as `400`.
- T023/T024a/T026/T030 (JSON contract shape + recent/sources serialization) →
  `StructuredLogEntrySerializerTests`.
- The `Diagnostics:StructuredLogs` authorization is implemented via FastEndpoints
  `ConfigurePermissions` + `EndpointSecurityOptions` (default-permissive when security is disabled,
  host-overridable), not a separate ASP.NET named policy.

Host-wide capture (the open caveat) was **validated** against `Elsa.Server`: the shell-registered
`ILoggerProvider` captures host-level logs (EF Core, FastEndpoints, etc.), the `recent`/`sources`
endpoints return data, the SSE `stream` frames entries, and `Last-Event-ID` resume replays buffered
entries. All 43 unit tests pass.
