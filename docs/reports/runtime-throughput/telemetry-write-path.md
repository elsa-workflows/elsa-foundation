# Request-path `elsa_otel_*` commands: diagnosis and disposition

**Track A2, [#2533](https://github.com/elsa-workflows/elsa-foundation/issues/2533), Program [#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531). 9 October 2026.**

## Question

The [final joined accounting](../runtime-db-access/final-joined-accounting.md) attributes 48 of the 134 Coalesced HTTP request-trace commands, and 128 of the 243 Immediate residuals, to `elsa_otel_*` tables in the ancillary SQLite store. The [findings comment](https://github.com/elsa-workflows/elsa-foundation/issues/2382#issuecomment-6077532251) suggested that these were telemetry writes on the request path and a likely throughput limiter. That premise was wrong.

## Finding

**The 48 commands are not span inserts.** They are six identical retention passes of eight commands each. Retention ran twice for every persisted telemetry batch, and only the redundant second pass carried the request's TraceId.

Evidence, `final-joined-accounting-2026-10-09.json`, `cases[1]` (Coalesced final), HTTP trace:

| Table | Commands | Leading verb |
|---|---:|---|
| `elsa_otel_traces` | 12 | SELECT |
| `elsa_otel_spans`, `_metric_points`, `_logs`, `_resources`, `_instruments` | 6 each | SELECT |
| `elsa_otel_capture_ledger` | 6 | DELETE |
| Any INSERT | 0 | n/a |

The table shape matches one pass of `EfOpenTelemetryStore.ApplyRetentionCoreAsync` exactly: two trace queries, one boundary query for each of the other five tables, and the ledger `ExecuteDelete`. The Immediate HTTP trace shows the same shape 16 times (128 commands).

## Path, verified in source

1. `src/apps/Elsa.Workbench/OpenTelemetryEngineTracingBridge.cs`: an in-process `ActivityListener` folds each drain cycle's span tree into one batch. It then calls `async void PublishAsync`, which is fire-and-forget. There is no OTel SDK exporter and no OTLP self-ingestion loop.
2. `OpenTelemetryIngestor.IngestAsync` → `EfOpenTelemetryStore.WriteAsync`: enqueue onto the `DiagnosticsDrain` channel, await the commit acknowledgement, then call `drain.ApplyPendingRetentionAsync`.
3. The drain loop (`src/essentials/Diagnostics/Persistence/Draining/DiagnosticsDrain.cs`) holds the target lock across each commit and its periodic retention. The OpenTelemetry store configures `RetentionInterval = 1`, so the loop already applies retention after every commit.
4. The writer's barrier then waits for that lock and runs a second, unconditional pass. The barrier runs in the `PublishAsync` continuation, which restores the request's execution context, so its commands carry the request TraceId. The loop's own commit and retention run on the startup context. They are untraced, which is why the accounting shows no inserts.

## Is it on the request's critical path?

No. The bridge never awaits the barrier. Only redaction, validation and enqueue run synchronously inside `Activity.Stop`. The commands were counted against the request because they inherited its trace, not because the request waited for them. The accounting's "all 134 completed before client receipt" result is a race outcome, not a dependency.

## What it actually cost

Each persisted telemetry batch paid for one redundant serializable transaction (`BEGIN IMMEDIATE` on SQLite) and eight commands, all on a store that shares its file with structured logs (`elsa-diagnostics.db`). That is 6 extra transactions per Coalesced HTTP request and 16 per Immediate request, all contending for the single SQLite writer lock. This is real background write-lock pressure under concurrency. Its effect on throughput is unmeasured; Track B's diagnostics on/off variant tests it.

## Disposition: code change

- `DiagnosticsDrain.ApplyRetentionIfPendingAsync`: same barrier, but it skips the pass when no unit has been committed since the last successful retention. Because the loop holds the target lock across commit plus periodic retention, a zero count after the barrier acquires the lock means the writer's commit is already covered. A failed loop pass leaves the count non-zero, so the barrier still runs it.
- `EfOpenTelemetryStore.WriteAsync` uses the conditional barrier. `ApplyPendingRetentionAsync` keeps its unconditional contract for callers that need retention now, for example after direct database edits in tests.
- **Expected effect:** the 48 / 128 traced `elsa_otel_*` commands should disappear from request traces, and each telemetry batch should cost one retention transaction instead of two. Not re-measured in this unit.
- **Proof:** drain unit tests for the skip, apply and lifecycle cases, plus an EF store test asserting that one drained write begins exactly two transactions (commit and retention).

## Not changed (candidates, ranked)

1. **Execution-context suppression in the bridge** (`ExecutionContext.SuppressFlow` around `PublishAsync`). This would stop trace misattribution of any remaining diagnostics work. It changes attribution, not cost, so it is deferred until Track B shows whether attribution matters.
2. **Time- or count-based retention throttling** (`RetentionInterval > 1`) and a conditional ledger delete. This would halve the remaining retention transactions, but it changes how long overflow stays visible. It needs its own review.
3. **One `SaveChanges` per commit** (cached sequences, skip unchanged resource upserts). Medium risk because it touches the replay/idempotency ledger contract.
4. **Operator option:** remove `DiagnosticsOpenTelemetryEntityFrameworkCore` (it falls back to the in-memory store) or `DiagnosticsOpenTelemetryEngineBridge` from throughput-sensitive shells. This is configuration only; neither feature has an `Enabled` flag.

The bridge's sweep and quiet-publish options, the drain batch size and the retention interval cannot be configured today. `OpenTelemetryDiagnosticsOptions.CaptureRecordsPerCommit` is declared but never read. These are recorded as findings, not changed here.
