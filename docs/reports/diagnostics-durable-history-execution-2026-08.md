# Executing the diagnostics durable-history workload: what the stores actually do

Date: 2026-08-12
Scope: `benchmarks/` and `tests/` only. No `src/` behaviour was changed.
Related: #1279, #1285, #1286, #1289, PR #1287.

## Why this report exists

Four documents merged on 2026-08-11 reasoned about the `diagnostics-durable-history` workload without ever
running it, because the session that wrote them had no .NET SDK. PR #1287 added the frozen 15-operation
runner, which CI proved compiles and which nothing had executed.

This report records the first execution of that operation sequence against real Groundwork diagnostics
stores over file-backed SQLite, and the one thing that execution found which inspection had not.

## Headline finding: the frozen scenario cannot be prepared on Groundwork

`specs/094-harden-groundwork-stores/workloads/diagnostics.json` freezes
`retainedRecordsPerStream: 100000`, and the runner asserts the inspected retained count of **all four**
OpenTelemetry record streams equals it:

```csharp
var openTelemetryRetainedCounts = new[]
{
    diagnostics.TraceCount, diagnostics.SpanCount, diagnostics.MetricPointCount, diagnostics.LogRecordCount
};
if (openTelemetryRetainedCounts.Any(count => count != RetainedRecordsPerStream))
    throw new InvalidOperationException(...);
```

Retained counts are a property of the capacities the *adapter* configures — the obligation is documented on
`IDiagnosticsDurableHistoryWorkloadAdapter` itself. But `GroundworkOpenTelemetryStore`'s constructor refuses
the required trace capacity outright:

```csharp
if (value.TraceCapacity > OpenTelemetryRecordStreamDefinitions.MaxTraceRecordCapacity)   // 5_000
    throw new ArgumentOutOfRangeException(
        nameof(options), value.TraceCapacity,
        $"Groundwork trace retention cannot exceed {MaxTraceRecordCapacity} records.");
```

The ceiling is not arbitrary. `MaxTraceRecordCapacity` also sizes the trace stream's grouped-query reduction
profile: `MaxGroupedQueryInputRecords: MaxTraceRecordCapacity` and
`MaxUnionValues: MaxTraceRecordCapacity * 256`. Raising the capacity without raising those would let a
grouped trace query silently reduce a subset of the retained records.

So **no Groundwork adapter can satisfy the frozen contract**, and the failure is at construction, before a
single record is written. The comparand ceiling, not the provider set, is what blocks the workload.

`EfCoreOpenTelemetryStore` has no such ceiling — its `ClampCapacity` is `Math.Max(1, capacity)` — so EF Core
would accept `TraceCapacity = 100_000`. The differential is therefore not merely unexecuted; at the frozen
scale only one of its two comparands can be constructed at all.

### What this means for the merged proposals

- **#1285 (Route 1, narrow to SQLite).** Narrowing the provider set does not make the workload harvestable.
  The blocker is the Groundwork trace ceiling, which applies identically on SQLite. Route 1's edit list is
  still worth reviewing, but it is not sufficient on its own, and its stated purpose — "so the one real EF
  oracle can be harvested" — does not follow.
- **#1286 (Route 2, absolute budgets).** Unaffected in substance: it addresses the three oracle-less
  providers, and this finding sits upstream of the budget question.
- **#1279.** Consistent with it. It found the ten prose-only EF baselines; this is a different failure mode
  in one of the three seams it identified as genuinely dual-stack.

The resolution is a decision, not a patch: either lower the frozen `retainedRecordsPerStream` to something
inside Groundwork's admitted range (which changes the frozen input fingerprint `448b4f12…513667` and the
result digest `d27a2436…fde4e7f7`, so it is a ratification-level change), or raise `MaxTraceRecordCapacity`
together with the grouped-query bounds it sizes (a `src/` change with its own review). Both are out of scope
for a benchmarks-only change.

## The eleven observations, measured

The frozen scale cannot run, but everything *except* the retained volume can. The probe
(`DiagnosticsDurableHistoryProbeTests`) drives the identical 15-operation sequence with every frozen
parameter at its frozen value and the retention at `4000 + 1000` overflow — the largest volume Groundwork's
trace stream admits. A failure there is a real property of the stores, not an artefact of the reduction.

**All eleven hold.** No assertion was weakened to get there.

| Observation | Asserted | Observed | Rests on |
|---|---|---|---|
| `structuredLogHighWater` | appended total | holds | `GetHighWaterMarkAsync` returns the max caller-assigned `Sequence`, across four concurrent writers |
| `structuredLogRecentCount` | `queryLimit` | holds | `MaxRecentQuerySize` ≥ `queryLimit` |
| `structuredLogReplayCount` | `queryLimit` | holds | a null cursor starts a bounded, continuable oldest-first page |
| `structuredLogRetainedCount` | retained capacity | holds | exact `KeepNewest` trim |
| `trimmedRecordsPerStream` | the overflow | holds | see the note below on what actually removes it |
| `openTelemetryRetainedCounts` | `[capacity × 4]` | holds | provider capacity retention prunes each stream to exactly capacity |
| `resourceCount` / `instrumentCount` | frozen catalog sizes | holds | catalog capacities equal to the frozen sizes |
| `diagnosticDropCount` | 0 | holds | capture queues sized to the whole frozen batch count |
| `restartStateMatched` | true | holds | a genuinely reopened client sees committed history |
| `crossScopeResultCount` | 0 | holds | storage-scope isolation across two tenants |

Two things the assertions rest on that are worth stating, because both are silent when wrong.

### The flush budget is the difference between evidence and noise

`FlushAsync` is implemented on both stores as `DiagnosticsDrain.StopAsync`, which waits at most
`ShutdownDrainTimeout` and then **fails every still-queued item as `ShutdownTimeout` loss**. That default is
ten seconds, and the frozen volume does not drain in ten seconds.

At the default, the probe failed in three different places on three consecutive runs, none of which named
the cause:

- retained stream counts of `[960 × 4]` and `[1664 × 4]` against an expected `[4000 × 4]` — both exact
  multiples of the 64-record OTLP batch, i.e. whole batches shed;
- a resource catalog holding 85, then 107, then 112 of 128 resources, the missing ones always the newest
  contiguous tail — the signature of `SaveCatalogsAsync` being cancelled part-way through its ordered save
  loop.

Every one of those reads as a storage defect and none of them is. Sizing `ShutdownDrainTimeout` to the unit
of work makes all of them go away. The leaf sets it explicitly for that reason, the same way
`CheckpointCommitAdapter` sizes `LeaseDuration`. **The capacity and query-clamp obligations documented on
`IDiagnosticsDurableHistoryWorkloadAdapter` should be joined by a drain-budget obligation** — it is the same
class of trap and it is the one that actually bit.

### `trimmedRecordsPerStream` does not measure the explicit trim

The runner computes it as `appendedPerStream - retainedAfterTrim`, and comments that "the deliberate
overflow is what trim removes". On Groundwork the explicit `TrimAsync` is usually not what removes it:
`GroundworkStructuredLogStore` already applies its own retention at `DefaultMaxRetainedEntries = 100_000`
every 5,000 appends, which is exactly the frozen `retainedRecordsPerStream`. The observation is still
correct because it is derived from the total appended, not from the trim's own delete count — but it does
not distinguish an implementation that trims on demand from one that had already trimmed. That is a weaker
claim than the comment implies.

## Two more places the contract and the stores do not line up

### `FlushAsync` cannot even be a plain drain completion

`IDiagnosticsDurableHistoryWorkloadAdapter.FlushAsync` is called three times, with writes in between. But
Groundwork's capture drain stops one way: `DiagnosticsDrain.StopAsync` completes the channel writer and
nothing reopens it, so `GroundworkOpenTelemetryStore.CompleteDrainingAsync` is terminal. An adapter that
implemented `FlushAsync` as "complete every drain" would poison the primary client's OpenTelemetry drain at
operation 2, three operations before operation 6 has to write to it.

The leaf handles this by recreating the store after each flush (`RotatingOpenTelemetryStore`), which is safe
because `GroundworkOpenTelemetryStore.DisposeAsync` disposes only its drain, never the underlying record or
document stores. The structured-log side needs no flush at all: `AppendAsync` completes only after
Groundwork returns the committed cursor, so an append is already durable when it returns. The runner's
remark that "both stores queue `AppendAsync`/`WriteAsync` onto a bounded background drain" is only half true
— `IOpenTelemetryStore.WriteAsync` is a bounded `TryEnqueue` that returns immediately, `AppendAsync` is not.

### Only SQLite is reachable from the adapter host

`diagnostics.json` declares provider evidence for all four providers, and Groundwork ships a diagnostic-record
store factory for each. What is missing is a route from the harness's `GroundworkProviderDriver` to the
connection those providers were started on: the driver keeps it behind a `protected`
`SchemaToolConnectionCore()`, and `GroundworkProviderClient` carries only the runtime document manifest, not
the OpenTelemetry one. Opening a second, adapter-owned database on a container provider would put a topology
claim in the retained artifact that the measured storage does not satisfy, so the leaf fails closed on the
other three rather than substituting one.

## What was not done

- **The frozen workload was never executed end-to-end.** It cannot be, for the reason above.
- **No timed operations.** The workload remains admission-blocked under
  `gate.diagnostics.absolute-budget-required`; the leaf's `Operations` property fails closed rather than
  publishing a list nothing has ever measured.
- **The admission block was not touched.** Unblocking is Route 1's governance-gated change.
- **The EF leaf was not attempted.** It remains blocked on the `physicalFormsFor646` gap recorded in
  `diagnostics-provider-topology-basis.md`.
