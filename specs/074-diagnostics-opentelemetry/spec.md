# Feature Specification: Diagnostics — OpenTelemetry (Ingestion, Live Streaming & Query)

**Feature Branch**: `sfmskywalker/port-otel-diagnostics-backend` (per-feature worktree branch)

**Created**: 2026-06-18

**Status**: Draft

**Input**: Port the OpenTelemetry diagnostics capability from elsa-core (`Elsa.Diagnostics.OpenTelemetry`) into elsa-foundation, adapted to foundation architecture. This slice (slice 3 of the diagnostics-observability-readiness program goal) covers OTLP/HTTP protobuf ingestion of traces/metrics/logs, redaction, a capacity-bounded in-memory store, a query API, and an SSE live stream. Durable persistence (slice 4) and the studio UI are separate follow-up slices.

## Overview

Operators and developers running an Elsa server need to see the application's OpenTelemetry signals — distributed traces, metrics, and logs correlated to workflow execution — without standing up an external collector + backend (Jaeger/Prometheus/Loki). This feature accepts telemetry pushed by the host's own OTLP exporter over OTLP/HTTP protobuf, normalizes and redacts it, retains a bounded recent window in memory, and exposes it through query endpoints and a live SSE stream so a diagnostics UI (the studio "OpenTelemetry" tab) can browse traces/metrics/logs and tail them in real time.

This slice delivers the backend: OTLP/HTTP ingestion, redaction, an in-memory bounded store, the query API, and the live feed. A later slice adds durable, queryable persistence behind the same `IOpenTelemetryStore` contract; the studio UI and gRPC ingestion are separate follow-ups.

## Clarifications

### Session 2026-06-18

- Q: Live transport — SignalR (as in source) or SSE? → A: SSE, mirroring the Structured Logs slice. Native `EventSource` reconnect, no `@microsoft/signalr` in studio, one transport pattern across diagnostics domains. The SignalR hub/subscription-manager/client are not ported.
- Q: Collector wiring — `EndpointRouteBuilderExtensions`/`IWebShellFeature` (as in source) or FastEndpoints? → A: FastEndpoints, consistent with the rest of the foundation and auto-mapped via `app.MapShells()`, avoiding a new host-plumbing dependency. The protobuf parser and ingestion security are ported verbatim and reused.
- Q: gRPC ingestion in this slice? → A: Deferred. `EnableGrpc` defaults to `false` and no gRPC route is mapped; the option and disabled-reason are retained so a host can light it up later (source ships gRPC disabled too).
- Q: Redaction posture? → A: Keep source redaction — sensitive attribute names (by key) and sensitive text patterns (by regex), bounded by a timeout — and surface the lists as manifest settings.
- Q: Ingestion authentication? → A: API-key header when configured; otherwise allow unauthenticated loopback only. This is orthogonal to the studio permission model that guards the query + SSE endpoints.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ingest OTLP telemetry pushed by the host (Priority: P1)

A host configured with an OTLP/HTTP exporter pointed at the Elsa server pushes traces, metrics, and logs; the server accepts, normalizes, and retains them so they can be queried and streamed.

**Why this priority**: Without ingestion there is nothing to show. It is the minimum viable slice a diagnostics UI can consume.

**Independent Test**: POST an OTLP/protobuf payload to `{base}/traces` (and `/metrics`, `/logs`); confirm the normalized signals are retrievable via the query API with trace/span/resource/workflow metadata intact.

**Acceptance Scenarios**:

1. **Given** the feature is enabled, **When** a valid OTLP/HTTP protobuf trace payload is POSTed to the collector, **Then** the server returns success and the trace/span/resource are queryable with their correlation metadata (trace id, root span, resource id, workflow instance id, status).
2. **Given** a malformed protobuf body, **When** it is POSTed to the collector, **Then** the server rejects it with `400` and remains stable.
3. **Given** a payload larger than the configured maximum body size, **When** it is POSTed, **Then** the server rejects it with `413` without buffering it unbounded.

### User Story 2 - Live-tail telemetry as it arrives (Priority: P1)

A diagnostics UI opening the OpenTelemetry tab wants newly ingested resources/traces/metrics/logs to appear in real time.

**Why this priority**: Real-time visibility is the core value of a live diagnostics surface.

**Independent Test**: Subscribe to the SSE `stream`; POST an OTLP payload; confirm typed SSE frames (`resource`, `trace`, `metric`, `log`) arrive for the ingested signals.

**Acceptance Scenarios**:

1. **Given** an SSE client is subscribed, **When** a batch is ingested, **Then** the client receives typed `event:` frames for the matching resources/traces/metrics/logs.
2. **Given** an SSE client disconnects and reconnects, **Then** streaming resumes without crashing the host (no `Last-Event-ID` resume is offered because stream items carry no sequence; the client simply resumes the live tail).
3. **Given** telemetry arrives faster than a slow client consumes it, **When** the client's bounded queue overflows, **Then** the oldest items are dropped and a `dropped` frame is delivered in-band rather than blocking the host or growing memory unbounded.

### User Story 3 - Query and filter recent telemetry (Priority: P1)

A developer investigating a problem wants to browse recent resources, traces (and a single trace's detail), metrics, and logs, filtered by service/resource/workflow/status/time/text.

**Why this priority**: Browsing recent history is required for the studio tab to be useful, not just a live tail.

**Independent Test**: Ingest a spread of signals; call each `search` endpoint with filters and the single-trace + storage + collector-config endpoints; confirm only matching results are returned and clamped to the configured query size.

**Acceptance Scenarios**:

1. **Given** ingested signals, **When** a client POSTs a filter to `traces/search` (or resources/metrics/logs), **Then** only matching entries are returned, clamped to the max query size.
2. **Given** a known trace id, **When** a client GETs `traces/{traceId}`, **Then** the full trace detail (spans) is returned, or `404` if unknown.
3. **Given** the feature is running, **When** a client GETs `storage`, **Then** it returns store diagnostics (counts and dropped totals); and `collector-configuration` returns the push configuration a collector needs.

### User Story 4 - Sensitive data is redacted on ingestion (Priority: P2)

An operator wants secrets that leak into telemetry attributes or log bodies masked before they are stored or streamed.

**Why this priority**: Telemetry frequently carries tokens/passwords; redaction materially reduces exposure, but the backend is still useful without it (hence P2).

**Independent Test**: Ingest a batch with attributes whose keys match the sensitive-names list and bodies matching the sensitive-text patterns; confirm the stored/streamed values are masked.

**Acceptance Scenarios**:

1. **Given** an attribute whose key matches the sensitive-names list, **When** the batch is ingested, **Then** its value is replaced with a redaction marker in the store and on the stream.
2. **Given** a log body matching a sensitive-text pattern, **When** the batch is ingested, **Then** the matched span is masked (pattern evaluation bounded by the configured timeout).

### Edge Cases

- **No subscribers**: Ingestion continues feeding the bounded store; no unbounded growth, no host impact.
- **Feature disabled**: No collector/query/stream endpoints registered; host telemetry export is unaffected.
- **High-volume burst**: Per-signal ring buffers and per-subscriber bounded channels protect host memory and throughput; drops are counted and signaled.
- **Ingestion auth**: When an API key is configured, requests without the matching header are rejected `401`; when none is configured, only loopback is allowed unauthenticated.
- **Studio authorization**: Query/SSE endpoints require the diagnostics permission; when the host tightens it beyond the default-permissive baseline, unauthorized callers are rejected.
- **gRPC**: Disabled in this slice; the option and disabled-reason are retained.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST accept OpenTelemetry traces, metrics, and logs pushed over OTLP/HTTP protobuf at signal-specific collector endpoints, parse the protobuf wire format without requiring a protobuf NuGet dependency, and normalize signals into a correlated model (resources, traces, spans, metric instruments/points, log records) including workflow correlation metadata.
- **FR-002**: The system MUST expose ingested telemetry as a real-time stream that one or more clients can subscribe to concurrently, delivering typed events per signal kind.
- **FR-003**: The system MUST maintain capacity-bounded in-memory retention per signal (traces, spans, metric points, log records, resources), evicting oldest entries beyond each configured capacity and counting evictions as drops.
- **FR-004**: The system MUST support query endpoints for resources, traces (list + single-trace detail), metrics, and logs, plus store diagnostics and collector configuration, with filtering by at least service/resource/workflow/trace id/status/time-range/text and result size clamped to a configured maximum.
- **FR-005**: The system MUST track the set of recently-seen telemetry resources (sources) so a client can present a source/service selector.
- **FR-006**: The system MUST apply backpressure or eviction for slow/stalled subscribers and MUST signal when stream items were dropped, without blocking or destabilizing the host.
- **FR-007**: The system MUST be packaged as an opt-in feature that, when not enabled, adds no collector, query, or stream endpoints and leaves host telemetry export unchanged.
- **FR-008**: The system MUST authenticate ingestion via an API-key header when configured and otherwise allow ingestion only from loopback addresses; this MUST be independent of the studio authorization model.
- **FR-009**: The system MUST gate the query and live-stream endpoints behind a named diagnostics authorization policy that defaults to permissive and is host-overridable so production can tighten access without code changes.
- **FR-010**: The system MUST redact sensitive data on ingestion — attribute values whose key matches a configurable sensitive-names list, and free-text fields matching a configurable list of sensitive-text regex patterns (evaluation bounded by a configurable timeout) — before storage or streaming.
- **FR-011**: The ingestion, store, redaction, source-registry, live-feed, read-facade, and collector-configuration surfaces MUST be defined behind contracts that a later persistence or transport slice can implement without changing consumers (the in-memory store/feed are the default implementations, registered with `TryAdd*`).
- **FR-012**: The system MUST expose configurable options including per-signal capacities, subscriber channel capacity, max query size, max HTTP body size, ingestion API key + header, loopback allowance, redaction lists/timeout, and the OTLP/stream paths — bound under the feature's stable name (`DiagnosticsOpenTelemetry`).
- **FR-013**: gRPC ingestion MUST be deferrable behind an option (default off) with no gRPC route mapped, while retaining the option and a disabled-reason so a host can enable it later.
- **FR-014**: The system MUST expose its capability/feature presence so a remote diagnostics UI can detect whether OpenTelemetry diagnostics are available on a host. The stable feature name enumerated via the host's modularity registry is the capability-detection key; no separate capability endpoint is required.

### Key Entities *(include if feature involves data)*

- **Telemetry Resource**: A service/instance origin of signals (service name, instance id, sdk language, attributes, last-seen, status).
- **Telemetry Trace / Span**: A correlated distributed trace (trace id, root span, name, start/end, duration, status, resource ids, workflow instance ids, span count) and its constituent spans.
- **Metric Instrument / Point**: A named instrument (kind/unit) and time-stamped measurement points with attributes and resource/trace correlation.
- **OTLP Log Record**: A correlated log line (timestamp, severity, body, trace/span id, resource id, attributes).
- **OpenTelemetry Batch**: The normalized ingestion unit `(Resources, Traces, Spans, Instruments, MetricPoints, Logs)`.
- **Stream Item / Dropped-Items Signal**: A live envelope carrying one signal (resource/trace/metric/log) or an in-band dropped-items summary `(signalType, count, reason)`.
- **Filters / Results**: Per-signal query criteria and clamped result sets; storage diagnostics; collector configuration.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With the feature enabled, a valid OTLP/HTTP trace payload POSTed to the collector returns success and becomes queryable within the same interaction (verified by an ingest→query round-trip).
- **SC-002**: With an SSE client subscribed, an ingested batch surfaces as typed `resource`/`trace`/`metric`/`log` frames within 1 second under normal load.
- **SC-003**: Under a sustained burst exceeding consumer capacity, host memory attributable to diagnostics stays bounded by the configured per-signal capacities and the host stays stable; drops are counted and reported.
- **SC-004**: Filtering returns only matching results across resources/traces/metrics/logs (zero non-matching in a verification run), clamped to the max query size.
- **SC-005**: When the feature is disabled, none of the collector/query/stream endpoints are reachable and host telemetry export is unchanged.
- **SC-006**: A request without the configured API key (and not from loopback) cannot ingest; an unauthorized caller cannot query or subscribe when the host tightens the diagnostics policy.
- **SC-007**: Attribute values matching the sensitive-names list and bodies matching the sensitive-text patterns are masked in stored and streamed output.

## Assumptions

- "Users" are operators/developers/diagnostics tooling (notably the studio OpenTelemetry tab), not end users of workflows.
- This slice provides in-memory bounded retention + live feed + query API only; durable persistence is slice 4, implementing the same `IOpenTelemetryStore` contract (EFCore-based, mirroring the Structured Logs persistence slice).
- Ingestion is push-based over OTLP/HTTP protobuf from the host's own exporter; the parser is a self-contained hand-roll (no protobuf NuGet).
- The feature follows foundation architecture conventions (domain-only naming, `.Core` contract layer, opt-in shell-feature registration, FastEndpoints, authorization via the host's diagnostics policy).
- The feature name is stable and used as the configuration/telemetry binding key per framework rules.

## Out of Scope (this slice)

- Durable persistence and long-range historical querying (slice 4: OpenTelemetry EFCore persistence).
- The studio "OpenTelemetry" tab UI (separate slice in elsa-foundation-studio).
- gRPC ingestion (deferred behind `EnableGrpc`).
- SignalR transport (replaced by SSE; not ported).
- Alerting, retention policies beyond the in-memory cap, sampling, and export to external backends.
