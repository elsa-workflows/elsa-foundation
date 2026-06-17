# Feature Specification: Diagnostics — Structured Logs (Capture, Live Streaming & Query)

**Feature Branch**: `sfmskywalker/port-diagnostics-modules` (shared worktree branch; per-feature branch hook intentionally skipped)

**Created**: 2026-06-18

**Status**: Draft

**Input**: Port the structured-logs diagnostics capability from elsa-core (`Elsa.Diagnostics.StructuredLogs`) into elsa-foundation, adapted to foundation architecture. This slice covers capture, live streaming, an in-memory bounded store, and a query/recent API. Durable persistence is a separate follow-up slice.

## Overview

Operators and developers running an Elsa server need to see the application's structured log events (level, message, category, timestamp, scopes/properties, exception) without attaching an external log aggregator or reading raw console output. This feature captures the host's structured logs and exposes them through a query/recent API and a live stream, so a diagnostics UI (the studio bottom-panel "Structured Logs" tab) can tail logs in real time and inspect recent history.

This slice delivers an in-memory bounded store (recent buffer) and the live feed. A later slice adds durable, queryable persistence behind the same contracts.

## Clarifications

### Session 2026-06-18

- Q: Source model — local-only, full aggregation, or no source concept? → A: Local source per host in v1; retain a `source` field in the model/contracts so the studio source-selector works and remote aggregation can be added later (no multi-source ingestion yet).
- Q: Authorization posture for the endpoints/stream? → A: Gate behind a named diagnostics authorization policy that defaults to permissive (matching the current console-stream surface) and is host-overridable so production can tighten it without code changes.
- Q: How much of each log event to capture? → A: Core fields (level/category/timestamp) + rendered message + exception + a bounded set of structured properties and scopes, with a configurable size cap.
- Q: Server/Studio dual-host parity in this slice? → A: Keep the feature host-agnostic and target Elsa.Server now; Studio-host parity (for a Server/Studio toggle) is deferred to the studio slice.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Live-tail structured logs (Priority: P1)

An operator watching a running Elsa server wants to see structured log events appear in real time as the server emits them, so they can observe behavior and spot warnings/errors as they happen.

**Why this priority**: Real-time visibility is the core value; without it the feature is just a log file. It is the minimum viable slice that a diagnostics UI can consume.

**Independent Test**: With the feature enabled, connect a live-stream client; trigger log output on the server; confirm the emitted entries arrive on the stream with level, timestamp, category, and message intact.

**Acceptance Scenarios**:

1. **Given** the structured-logs feature is enabled, **When** the host emits a log event at or above the configured minimum level, **Then** a subscribed live-stream client receives a structured entry containing level, timestamp, category, message, and any structured properties/scope and exception detail.
2. **Given** a live-stream client is connected, **When** the connection drops and reconnects, **Then** streaming resumes without crashing the host and without requiring a server restart.
3. **Given** log events are produced faster than a slow client can consume, **When** the client's buffer is exceeded, **Then** the system drops the oldest/over-budget entries and signals that entries were dropped rather than blocking the host or growing memory unbounded.

### User Story 2 - Inspect recent history on connect (Priority: P1)

When a diagnostics UI opens the Structured Logs tab, it wants to immediately show the most recent entries (a replay buffer), not just events that happen after it connected.

**Why this priority**: A live tail that starts empty is far less useful; recent-history replay is expected behavior for a log viewer and is required for the studio tab to feel responsive.

**Independent Test**: Produce a number of log entries, then request recent entries via the API; confirm the most recent N (up to the configured cap) are returned newest-aligned, independent of any live subscription.

**Acceptance Scenarios**:

1. **Given** the server has emitted log entries, **When** a client requests recent entries, **Then** it receives up to the configured maximum most-recent entries.
2. **Given** more entries have been emitted than the buffer cap, **When** a client requests recent entries, **Then** only the most recent cap-many entries are retained and returned (oldest evicted).

### User Story 3 - Filter the stream and history (Priority: P2)

A developer investigating a specific problem wants to narrow logs by minimum level, category, and/or source, so they can focus on relevant entries.

**Why this priority**: Filtering greatly increases usefulness but the feature is still valuable (live tail + recent) without it; hence P2.

**Independent Test**: Emit entries across multiple levels/categories/sources; request recent entries and subscribe with filter criteria; confirm only matching entries are returned/streamed.

**Acceptance Scenarios**:

1. **Given** entries exist at multiple levels, **When** a client requests entries filtered to a minimum level, **Then** only entries at or above that level are returned/streamed.
2. **Given** entries exist for multiple categories or sources, **When** a client filters by category/source, **Then** only matching entries are returned/streamed.
3. **Given** a multi-source deployment, **When** a client requests the list of known sources, **Then** the system returns the sources observed so far so the UI can offer a source selector.

### Edge Cases

- **No subscribers**: Capture continues feeding the bounded buffer; no unbounded growth, no host impact.
- **Feature disabled**: No capture, no endpoints/stream registered; host logging behaves exactly as before.
- **Logging recursion**: Capturing logs must not itself emit logs that get captured in a feedback loop.
- **High-volume burst**: Backpressure/eviction protects host memory and throughput; dropped-entry signaling is surfaced.
- **Sensitive data**: Entries may contain whatever the host logs; the feature does not add redaction in this slice (documented assumption).
- **Authorization**: Diagnostics endpoints/stream require the host's diagnostics authorization policy; unauthorized callers are rejected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST capture structured log events emitted through the host's standard logging abstraction, including level, timestamp, category, rendered message, exception information when present, and a **bounded** set of structured properties and active scopes (truncated/capped per a configurable size limit; see FR-009). Capturing the message template in addition to the rendered message is permitted but optional.
- **FR-002**: The system MUST expose captured entries as a real-time stream that one or more clients can subscribe to concurrently.
- **FR-003**: The system MUST maintain a bounded in-memory buffer of the most recent entries and expose a "recent" query that returns up to a configurable maximum, evicting oldest entries beyond the cap.
- **FR-004**: The system MUST support filtering of both the recent query and the live subscription by at least minimum level, category, and source.
- **FR-005**: The system MUST attribute entries to a log source and expose the set of known sources so a client can present a source selector. In v1 the host reports a single local source describing itself; the source field and sources surface are retained so remote multi-source aggregation can be added later without contract changes. Cross-process/remote source ingestion is out of scope for this slice.
- **FR-006**: The system MUST apply backpressure or eviction for slow/stalled subscribers and MUST signal when entries were dropped, without blocking or destabilizing the host.
- **FR-007**: The system MUST be packaged as an opt-in feature that, when not enabled, adds no capture, endpoints, or stream and leaves host logging unchanged.
- **FR-008**: The system MUST gate diagnostics endpoints and the live stream behind a named diagnostics authorization policy. The policy defaults to permissive (matching the current foundation console-stream surface) and MUST be overridable by the host so production deployments can tighten access without code changes.
- **FR-009**: The system MUST expose configurable options including minimum capture level, buffer capacity (max retained entries), per-subscriber queue/replay limits, and the captured-property/scope size cap (FR-001), bound under the feature's stable name.
- **FR-010**: Capture and streaming MUST NOT create an infinite logging feedback loop and MUST NOT throw out of the host's logging path on diagnostics failure.
- **FR-011**: The capture, store, live-feed, and query surfaces MUST be defined behind contracts that a later persistence slice can implement durably without changing consumers (the in-memory store is the default implementation of those contracts).
- **FR-012**: The system MUST expose its capability/feature presence so a remote diagnostics UI can detect whether structured logs are available on a given host.

### Key Entities *(include if feature involves data)*

- **Structured Log Entry**: A single captured event — identity/sequence, timestamp, level, category, rendered message (and optionally message template), a bounded set of structured properties, a bounded scope chain, exception detail, and originating source reference.
- **Log Source**: A logical origin of entries. In v1 this is the single local host describing itself (service/process/host metadata); the field and shape are retained so future remote sources fit without contract changes.
- **Log Filter / Query**: Criteria for selecting entries — minimum level, category, source, and a maximum count for recent queries.
- **Dropped-Entries Signal**: A marker indicating that some number of entries were not delivered to a subscriber due to backpressure/eviction.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the feature enabled and a client subscribed, an operator sees newly emitted log entries appear on the live stream within 1 second of emission under normal load.
- **SC-002**: Opening the diagnostics view shows the most recent entries immediately (recent-history replay returns within the same interaction, with no live events required first).
- **SC-003**: Under a sustained burst that exceeds consumer capacity, host memory attributable to diagnostics remains bounded by the configured cap and the host continues processing without log-path errors; dropped entries are reported to the affected client.
- **SC-004**: Filtering by minimum level, category, or source returns only matching entries in both recent queries and the live stream (zero non-matching entries in a verification run).
- **SC-005**: When the feature is disabled, there is no measurable change to host logging behavior and none of the diagnostics endpoints or streams are reachable.
- **SC-006**: An unauthorized caller cannot read entries or subscribe to the stream when the host configures the diagnostics policy to require authorization (default-permissive policy is host-overridable per FR-008).

## Assumptions

- "Users" of this feature are operators/developers/diagnostics tooling (notably the studio Structured Logs bottom-panel tab), not end users of workflows.
- This slice provides an in-memory bounded store and live feed only; durable, long-term queryable persistence is a separate follow-up slice that implements the same contracts (EFCore-based, per the chosen persistence direction).
- Capture targets the host's standard structured-logging abstraction; logs written directly to console bypassing that abstraction are out of scope here (console output is already covered by the existing console-log streaming surface).
- Redaction/PII handling of log content is out of scope for this slice; entries reflect whatever the host already logs.
- The feature follows foundation architecture conventions (domain-only naming, three-layer separation, opt-in feature registration, authorization via the host's diagnostics policy). Specific framework/runtime/storage choices are deferred to the plan.
- The feature name is stable and used as the configuration/telemetry binding key per framework rules.
- Multi-host/source metadata mirrors the existing console-stream source model so the studio UI can reuse its source-selector pattern. In v1 each host reports only its own single local source; the feature is host-agnostic and this slice targets the Elsa.Server backend, with Studio-host parity deferred to the studio slice.

## Out of Scope (this slice)

- Durable persistence and long-range historical querying (separate slice: Structured Logs EFCore persistence).
- The studio bottom-panel "Structured Logs" tab UI (separate slice in elsa-foundation-studio).
- OpenTelemetry ingestion/visualization (separate domain slices).
- Re-implementing console-log streaming (already provided by the ConsoleLogStreaming surface).
- Log redaction, alerting, retention policies, and export.
