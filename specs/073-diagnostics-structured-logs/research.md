# Phase 0 Research — Structured Logs Diagnostics

This resolves the open technical decisions the spec deferred to planning. Each item records the
Decision, Rationale, and Alternatives considered.

## R1 — Capture mechanism

**Decision**: Capture via a custom `ILoggerProvider` (`StructuredLogCaptureProvider`) registered in DI
by the feature, producing a lightweight `ILogger` that forwards each enabled event into the in-memory
store. Scopes are captured via the logger's scope stack; structured properties are read from the
state when it implements `IReadOnlyList<KeyValuePair<string, object?>>`.

**Rationale**: This is the portable, idiomatic .NET surface — identical in concept to elsa-core's
StructuredLogs provider. It requires no host hook and works for every `Microsoft.Extensions.Logging`
sink. Registering the provider through `IShellFeature.ConfigureServices` means the logging pipeline
picks it up at host build.

**Alternatives considered**:
- *Very-early static hook* (like `ConsoleStreamHook.Install()` for console capture) — rejected: only
  needed to catch pre-host-build startup logs and to intercept raw console writes; structured logging
  goes through the standard abstraction, so a DI-registered provider is sufficient and cleaner.
  Startup logs emitted before the logging system is built are out of scope for v1 (documented).
- *`ILoggerFactory` decoration* — rejected: more invasive, no benefit over a provider.

**Loop-safety (FR-010)**: The capturing logger MUST ignore its own category and MUST NOT log on the
capture path; any sink/store failure is swallowed (optionally counted) so it never throws back into
the host's logging call.

## R2 — Live transport (SSE vs SignalR vs WebSocket)

**Decision**: **Server-Sent Events (SSE)**. The live feed is a `text/event-stream` HTTP endpoint
(`StreamEndpoint`, a FastEndpoint) that writes an `IAsyncEnumerable<StructuredLogEntry>` to the
response, emitting named events (`entry`, `dropped`) with an `id:` line carrying the entry sequence so
the browser's native `EventSource` can resume via `Last-Event-ID`. `recent` and `sources` are plain
HTTP (FastEndpoints) GETs. All three are auto-mapped by the existing `app.MapShells()`; there is **no**
host hub wiring.

**Rationale**:
- The workload is one-way server→client, browser-only. Native `EventSource` gives auto-reconnect and
  `Last-Event-ID` resume — a near-perfect fit for a log tail — with **zero new dependencies** on
  either side (no SignalR server hub, no `@microsoft/signalr` in the studio bundle).
- SSE is plain HTTP over the already-pinned `Microsoft.AspNetCore.Http`, keeping the dependency
  envelope minimal (§2.20 rule-2). No `FrameworkReference Microsoft.AspNetCore.App`, no protobuf.
- Client→server interaction is trivial (subscribe + filter) and is expressed as query-string
  parameters on the stream GET, so SSE's one-way nature is not a constraint here.

**Decision context (Console parity)**: The existing Console bottom-panel tab uses a SignalR transport
**owned by the third-party `ConsoleLogStreaming.AspNetCore` package**, not by foundation code. It is
deliberately left untouched. Structured Logs is greenfield and foundation-owned, so it is the
low-risk place to adopt SSE. The studio bottom panel therefore temporarily speaks two transports
(SignalR for Console, SSE for Structured Logs) — an intentional, time-boxed inconsistency.
**Follow-up (separate work unit):** evaluate retiring the `ConsoleLogStreaming` package and unifying
Console onto the foundation-owned SSE transport, which would also remove a preview third-party
dependency. Tracked under the Diagnostics Observability Readiness program-goal bucket.

**Auth caveat (carried into R5)**: the browser's native `EventSource` cannot set an `Authorization`
header. With the default-permissive policy this is a non-issue; hosts that tighten the policy use
cookie auth or a token query-string parameter (the studio already uses credentialed/CORS calls), or a
`fetch`+`ReadableStream` SSE reader that can set headers.

**Alternatives considered**:
- *SignalR hub (shared framework)* — would match the Console tab and give automatic transport
  fallback + clean bearer auth, but pays for bidirectional/group capability the workload never uses
  and adds the `@microsoft/signalr` client. Rejected for this greenfield module in favour of the
  leaner SSE fit; Console parity is addressed by the follow-up above, not by matching SignalR now.
- *Raw WebSocket* — most plumbing, loses `EventSource` reconnect/resume. Rejected.

## R3 — In-memory bounded store + multi-subscriber live feed

**Decision**: A single `InMemoryStructuredLogStore` (public sealed) implements both
`IStructuredLogStore` (append + `GetRecent(filter)`) and `IStructuredLogLiveFeed`
(`Subscribe(filter)`), backed by a fixed-capacity ring buffer guarded for concurrency. Each
subscriber gets a bounded channel; when full, the oldest/over-budget items are dropped and a
`DroppedEntriesSignal` (running count) is delivered instead of blocking the producer.

**Rationale**: One cohesive default implementation keeps the slice minimal (§2.17/§2.20 rule-1).
Bounded buffer + per-subscriber bounded channel satisfies SC-003 (bounded memory) and FR-006
(backpressure + drop signal) without blocking the host logging path.

**Alternatives considered**:
- *Separate store and feed classes* — deferred; not needed until a second store (EFCore) arrives,
  at which point the live-feed seam may be lifted into its own contract/impl.
- *Unbounded queue per subscriber* — rejected: violates SC-003.

## R4 — Source model (v1 local source)

**Decision**: One `IStructuredLogSourceProvider` default (`LocalStructuredLogSourceProvider`) that
reports a single local `LogSource` describing the host (service name / process / host). Every captured
entry is stamped with that source id. The `sources` endpoint returns the observed set (one entry in
v1). The `source` field and the sources surface are retained on the contracts so remote multi-source
aggregation can be added later without a contract change (spec FR-005, clarified).

**Rationale**: Matches the clarified scope and the console-stream source-selector model the studio
reuses; avoids over-building multi-source ingestion now.

**Alternatives considered**: full multi-source aggregation (rejected — out of scope); no source
concept (rejected — breaks the studio source selector and future aggregation).

## R5 — Authorization

**Decision**: Gate `recent`, `sources`, and the `stream` behind a **named** authorization policy
(constant `StructuredLogsPolicy`, e.g. `"Diagnostics:StructuredLogs"`). The feature registers the
policy with a **default-permissive** requirement (allow anonymous), matching the current
console-stream posture; the host can override the policy registration to tighten access without code
changes (FR-008, SC-006).

**Rationale**: Keeps dev-time parity with the permissive console surface while making production
hardening a pure configuration/registration override — no consumer changes.

**Alternatives considered**: hard-required auth (rejected — breaks parity/dev UX); no policy
(rejected — no host override seam, fails SC-006).

## R6 — Configuration & feature identity

**Decision**: Feature `name = "DiagnosticsStructuredLogs"` (stable, §2.19). `StructuredLogsOptions`
bound under that name exposes: `MinimumLevel`, `BufferCapacity`, `SubscriberQueueCapacity`,
`MaxRecentQuerySize`, `MaxCapturedProperties`, `MaxCapturedScopeDepth`, `MaxPropertyValueLength`,
and the endpoint paths (defaulting under `/_elsa/studio/diagnostics/structured-logs/...` to match
the console-stream path shape: `recent`, `sources`, `stream`).

**Rationale**: §2.19 makes the feature name the binding key for options, diagnostics, and telemetry.
Path defaults mirror console-stream so the studio host config stays uniform.

## R7 — Persistence boundary (deferred slice)

**Decision**: `IStructuredLogStore` (and the live-feed/source contracts) are the seam a later
EFCore-based slice implements durably (`*.Persistence.Core` / `.Persistence.EFCore` /
`.Persistence.EFCore.Sqlite`), per the foundation persistence base. No persistence types are created
in this slice (§2.20 rule-1: no anticipatory stubs). FR-011 is satisfied by keeping the store contract
free of in-memory assumptions.

**Rationale**: Aligns with the chosen EFCore persistence direction and keeps this slice shippable
in-memory while guaranteeing the persistence slice changes no consumers.

## Resolved unknowns summary

| Spec deferral | Resolved by |
|---|---|
| Live transport mechanics | R2 — SSE (`text/event-stream` FastEndpoint, `Last-Event-ID` resume) |
| Capture approach | R1 — `ILoggerProvider` |
| Store/backpressure shape | R3 — ring buffer + per-subscriber bounded channel + drop signal |
| Source model details | R4 — single local source provider, retained source field |
| Authorization shape | R5 — named default-permissive, host-overridable policy |
| Options/feature name | R6 — `DiagnosticsStructuredLogs` + `StructuredLogsOptions` |
| Persistence seam | R7 — `IStructuredLogStore` contract; EFCore slice later |
