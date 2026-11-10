# G3 persistence/coordination overhead on a Root/SQLite G2 journey

Point-in-time measurement for execution-core slice S13 / goal §G3. Not a budget, target, speedup or throughput claim. Retired BenchmarkDotNet gates (B01–T04 / #2892 evidence policy) are not restored.

**Journey:** `RunBpmnChildCancelTests` / [#4028](https://github.com/elsa-workflows/elsa-foundation/pull/4028) child-to-parent cancel (operator cancel of a direct BPMN model while a native `RunBpmn` Root parent is waiting, kill before parent consume, restart).

**Limit:** SQLite Owned Workbench, one in-process host. No PostgreSQL, SQL Server or MySQL cell. Root composition only.

## Environment

| Field | Value |
|---|---|
| Base `origin/main` | `87b8b41fc2650b18866e9c8da3fed3ba6a5a86d8` (CI push green) |
| Measured tree | this report's commit on `exec-core/g3-overhead-counts` |
| OS | Ubuntu 24.04.5 LTS (`Linux cursor 6.12.113+ x86_64`) |
| .NET | SDK 10.0.403, host 10.0.14 |
| Provider | SQLite |
| Host | Owned Workbench, one process, `directBpmn: true`, `recoverySweepSeconds=0.2` |
| Tool | EF Core `Database.Command` / `Database.Transaction` Information logs parsed as `CommandCaptureInterceptor`-equivalent counts; `Stopwatch` per logical step; SQLite `ContentJson` for Cancel-kind after stop |
| Run count | 3 (each run reported; not only an average) |

## Coverage reconciliation (before writing the harness)

Searched `main` at `87b8b41fc` for overhead counters, interceptors, command-count tests and retired #2892 benchmark gates.

| Existing | What it is | Reuse decision |
|---|---|---|
| `CommandCaptureInterceptor` + `EfCommandBudgetTests` + `ef-command-budgets.json` | Live in-process HTTP/Sequence **CI budget gate** on SQLite runtime | Reused the **metric families** (every EF command = round-trip; INSERT/UPDATE/DELETE = write). Did **not** add a BPMN row to the budget JSON — that would invent a CI threshold. |
| `RuntimeCheckpointCoalescingTests` | In-memory coalescing behaviour, not production EF round-trips | Not a G2 journey harness. Left alone. |
| `docs/reports/native-runtime-performance-*.md`, `runtime-http-performance`, `runtime-db-access`, `runtime-throughput`, `checkpoint-commit-store-performance` | Historical native HTTP/Sequence or BPMN Start→End starts/s | Different journeys. Not G2. Not re-run. |
| B01–B05 `benchmarks/Elsa.*`, BDN pin, `tools/performance/**`, StorePerformance T04 | Retired under #2892 / ADR 0073 D7 / ADR 0080 D2 | Confirmed absent (`*.Benchmarks.csproj` / `[Benchmark]`). Not restored. |
| G2 hosting Facts (`RunBpmnPendingParentResultCrashTests`, `RunBpmnChildFaultHeldRetryTests`, `CallActivityNativeChildCrashTests`, `RunBpmnChildCancelTests`) | Correctness on Owned Workbench | Interceptor cannot be injected into the production Workbench child without a production hook. G3 therefore patches the **copied** Workbench `appsettings.json` LogLevel only. |

No existing G2-journey overhead ledger was found. One proven journey is enough for G3; the #4028 deferred “exactly one Cancel command” count made child-cancel the required journey.

## Method

1. Opt-in `OwnedWorkbench.StartAsync(..., capturePersistence: true)` copies Workbench appsettings and sets `Microsoft.EntityFrameworkCore.Database.Command` and `.Transaction` to Information. Production appsettings are not changed.
2. Console lines are parsed into round-trips, writes, transactions, lease ops (`elsa_runtime_workflow_executable_root_write_lease`) and fence/claim ops (`ClaimOwnerId` / `ClaimToken` / `FencingToken` / `ExpectedFence` / `VisibleAfterUtcTicks`).
3. `runtimeRoundTrips` / `runtimeWrites` count only SQL mentioning `elsa_runtime_*`. All-EF totals also include design, identity and host boot.
4. Cancel-kind work items are counted from durable SQLite `ContentJson` on `elsa_runtime_scheduler_work_item` (and checkpoint/outbox corroboration). Parameterized EF SQL logs `@p0='?'` and does not include the payload, so log-derived `SchedulerCancelCommands` stay 0.
5. The test **asserts** only agreed semantic counts: HTTP cancel applied once, native `RunBpmn` `Cancelled` once, scheduler Cancel command issued once. Raw overhead numbers are recorded, not gated.
6. Reuses the existing `ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME` call site (`HoldNativeParentResume()`). No third production-side hold site.

## Semantic Cancel counts (asserted; identical on all three runs)

| Count | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| Scheduler Cancel commands issued (`WorkflowExecutionCommandKind.Cancel` in SQLite `ContentJson`) | 1 | 1 | 1 |
| Same count from parameterized EF logs | 0 | 0 | 0 |
| HTTP cancel applied | 1 | 1 | 1 |
| HTTP cancel replayed (same `requestId`) | 1 | 1 | 1 |
| HTTP cancel conflicted (new `requestId` after terminal) | 1 | 1 | 1 |
| Native `RunBpmn` activity `Cancelled` | 1 | 1 | 1 |
| `CancelEmbedded` intents while parent still waiting | 0 | 0 | 0 |
| `CancelEmbedded` intents after parent already `Cancelled` (parent-owned teardown) | 1 | 1 | 1 |
| `CancelActivityScope` commands | 0 | 0 | 0 |

Agreed #4028 semantics: the operator cancel is applied once to the model; the native parent observes exactly one Cancel mapping. `CancelEmbedded` is forbidden while the parent is still waiting. A later `CancelEmbedded` after the parent is already `Cancelled` is parent-owned teardown, not the child commanding the parent.

## Per-step overhead (three runs)

Columns: latency ms · all-EF round-trips · runtime round-trips · all-EF writes · runtime writes · lease ops · fence ops. Transactions started/committed/rolled back were **0** on every step of every run (SQLite EF did not emit `Database.Transaction` Information lines; this is a method limit, not a product defect).

| Step | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| publish-bpmn-model | 313 · 1223 · 34 · 1177 · 8 · 7 · 1 | 322 · 1279 · 36 · 1227 · 8 · 7 · 1 | 317 · 926 · 43 · 873 · 8 · 7 · 2 |
| submit-native-parent | 320 · 1263 · 16 · 1224 · 0 · 0 · 2 | 342 · 1283 · 7 · 1245 · 0 · 0 · 1 | 374 · 1190 · 7 · 1161 · 0 · 0 · 1 |
| publish-native-parent | 578 · 2280 · 74 · 2165 · 14 · 8 · 4 | 582 · 2376 · 83 · 2252 · 14 · 8 · 5 | 585 · 1850 · 83 · 1732 · 14 · 8 · 5 |
| execute-parent-admit-child | 1103 · 3923 · 121 · 3775 · 32 · 8 · 16 | 1124 · 4071 · 126 · 3909 · 33 · 8 · 18 | 1110 · 2548 · 121 · 2401 · 32 · 8 · 16 |
| wait-cross-model-human | 615 · 1306 · 160 · 1101 · 24 · 4 · 15 | 597 · 2539 · 146 · 2360 · 21 · 4 · 11 | 641 · 1666 · 151 · 1461 · 22 · 4 · 12 |
| operator-cancel-child-before-parent-consume | 138 · 172 · 119 · 60 · 20 · 8 · 9 | 295 · 622 · 124 · 478 · 20 · 8 · 9 | 142 · 279 · 109 · 174 · 17 · 8 · 9 |
| kill-before-parent-consume | 1187 · 77 · 0 · 77 · 0 · 0 · 0 | 1162 · 0 · 0 · 0 · 0 · 0 · 0 | 1171 · 50 · 4 · 46 · 0 · 0 · 0 |
| restart-host | 6934 · 7133 · 133 · 6542 · 31 · 0 · 17 | 7000 · 6380 · 138 · 5795 · 33 · 0 · 20 | 6931 · 6703 · 119 · 6129 · 28 · 0 · 16 |
| parent-consumes-Cancelled | 1442 · 3884 · 142 · 3687 · 19 · 4 · 13 | 1370 · 3504 · 137 · 3300 · 17 · 4 · 10 | 2597 · 7703 · 206 · 7367 · 22 · 4 · 19 |

## Journey totals (three runs)

| Metric | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| All-EF round-trips | 21261 | 22054 | 22915 |
| Runtime (`elsa_runtime_*`) round-trips | 799 | 797 | 843 |
| All-EF writes | 19808 | 20566 | 21344 |
| Runtime writes | 148 | 146 | 143 |
| Transactions started / committed / rolled back | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| Lease ops | 39 | 39 | 39 |
| Fence ops | 77 | 75 | 80 |
| Sum of EF-logged command durations (ms) | 203 | 275 | 240 |
| Wall clock of the G3 Fact (s) | 24.7 | 25.4 | 26.2 |

All-EF totals are host-wide (design catalog, identity, publish, boot). Use the runtime columns for coordination overhead on the G2 boundary. `restart-host` is dominated by Workbench boot, not by Cancel consume.

## Touched G2 class timings (three runs each)

No 30 s post-restart wait timeout occurred on this VM. The #3992 timeout recorded on 8 November is retained as historical evidence and was not loosened.

| Class | Run 1 | Run 2 | Run 3 | Result |
|---|---|---|---|---|
| `G3OverheadCountTests` | 24.7 s | 25.4 s | 26.2 s | pass |
| `RunBpmnPendingParentResultCrashTests` (#3992) | 85.8 s | 86.1 s | 85.1 s | pass |
| `RunBpmnChildFaultHeldRetryTests` (#3993) | 45.9 s | 48.3 s | 45.7 s | pass |
| `CallActivityNativeChildCrashTests` (#3995) | 59.2 s | 59.4 s | 61.4 s | pass |
| `RunBpmnChildCancelTests` (#4028) | 33.9 s | 33.9 s | 32.6 s | pass |

## What this does not prove

- Other persistence providers (PostgreSQL, SQL Server, MySQL)
- Multi-host / clustered placement
- Sequence, Flowchart or CompensationScope native parents
- Containing-main provider qualification of the G2 journeys
- #3992 / #3993 / #3995 overhead (correctness re-run only; counts are on the #4028 child-cancel journey)
- ETL record/partition/workflow scheduling (#3804/#3789), #3650, R3/retention redesign
- A budget, SLO, speedup or throughput ranking
- Merged native vs BPMN publish/outcome/cancel semantics

## Harness location

- `Essentials/tests/Activities/Bpmn/Hosting/Tests/G3OverheadCountTests.cs`
- `Essentials/tests/Activities/Bpmn/Hosting/Tests/WorkbenchPersistenceCapture.cs`
- Opt-in `capturePersistence` on `OwnedWorkbench`

The class lives in the existing `bpmn-hosting-rest` integration-tier project. It is not a new required CI check and does not assert overhead numbers.
