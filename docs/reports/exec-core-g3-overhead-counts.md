# G3 persistence/coordination overhead on Root/SQLite G2 journeys

Point-in-time measurement for execution-core slice S13 / goal §G3. Not a budget, target, speedup or throughput claim. Retired BenchmarkDotNet gates (B01–T04 / #2892 evidence policy) are not restored.

**Journeys:**

| Slice | PR | Journey |
|---|---|---|
| 5 | [#4039](https://github.com/elsa-workflows/elsa-foundation/pull/4039) merged `3da38eb53` | `RunBpmnChildCancelTests` / [#4028](https://github.com/elsa-workflows/elsa-foundation/pull/4028) child-to-parent cancel |
| 6 | this report's follow-on branch | `#3992` pending-parent-result crash; `#3993` held native-worker retry; `#3995` callActivity pending-parent-resume crash |

**Limit:** SQLite Owned Workbench, one in-process host. No PostgreSQL, SQL Server or MySQL cell. Root composition only. Transactions are **unmeasured** (SQLite EF logged no `Database.Transaction` Information lines, so tx counters stay 0). Parameterized EF SQL does not include `ContentJson`.

## Environment

| Field | Value |
|---|---|
| Base `origin/main` | `87b8b41fc2650b18866e9c8da3fed3ba6a5a86d8` (CI push green) |
| Measured tree | `7164f2518cb8f2a762e2388f0e02f45641081b83` (`exec-core/g3-overhead-counts`) |
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
- #3995 wait-phase kill (the other Fact in `CallActivityNativeChildCrashTests`); #3993 FinalFault and exhausted-retry Facts
- ETL record/partition/workflow scheduling (#3804/#3789), #3650, R3/retention redesign
- A budget, SLO, speedup or throughput ranking
- Merged native vs BPMN publish/outcome/cancel semantics

## Harness location

- `Essentials/tests/Activities/Bpmn/Hosting/Tests/G3OverheadCountTests.cs` (#4028)
- `Essentials/tests/Activities/Bpmn/Hosting/Tests/G3RemainingJourneyOverheadTests.cs` (#3992 / #3993 / #3995)
- `Essentials/tests/Activities/Bpmn/Hosting/Tests/G3Measurement.cs` and `WorkbenchPersistenceCapture.cs`
- Opt-in `capturePersistence` on `OwnedWorkbench`

The classes live in the existing `bpmn-hosting-rest` integration-tier project. They are not a new required CI check and do not assert overhead numbers.

## Slice 6 — remaining G2 journeys (#3992 / #3993 / #3995)

Follow-on to [#4039](https://github.com/elsa-workflows/elsa-foundation/pull/4039). Reuses the same `capturePersistence` harness and metric families. No `ef-command-budgets.json` rows. No third `ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME` production call site: #3992 uses existing `HoldClaimedBpmnParentResume()`, #3995 uses existing `HoldNativeParentResume()`, #3993 uses no hold.

### Environment (slice 6)

| Field | Value |
|---|---|
| Base `origin/main` | `d3ab41003465c0cadb9a47cd05dcdd4750f3adc7` (CI push green; contains #4039 `3da38eb53`) |
| Measured tree | `e0dc8e14bfc46ee682d6cbf7c8ba7164fa26a8de` (`exec-core/g3-overhead-counts-2`) |
| OS | Ubuntu 24.04.5 LTS (`Linux cursor 6.12.113+ x86_64`) |
| .NET | SDK 10.0.403, host 10.0.14 |
| Provider | SQLite |
| Host | Owned Workbench, one process, `recoverySweepSeconds=0.2` (#3992/#3993 also `directBpmn: true`) |
| Tool | Same as slice 5: EF Core `Database.Command` Information logs; `Stopwatch` per logical step |
| Run count | 3 (each run reported) |

### Coverage reconciliation (slice 6, before writing)

| Existing | Reuse decision |
|---|---|
| `capturePersistence` / `WorkbenchPersistenceCapture` / `G3OverheadCountTests` from #4039 | Reused as-is. Added `G3Measurement` for step/receipt sharing. Did not change production code. |
| `EfCommandBudgetTests` / `ef-command-budgets.json` | Still HTTP/Sequence CI budget. No BPMN row added. |
| Retired B01–T04 / BDN / `tools/performance` | Still absent. Not restored. |
| Two production hold sites (`BpmnEmbeddingDelivery`, `ParentResumeExecutor`) | Reused via existing OwnedWorkbench helpers. No third site. |

### Journey totals (three runs each)

Runtime columns are `elsa_runtime_*` only. All-EF totals include design, identity and host boot. Transactions started/committed/rolled back were **0** on every run (unmeasured; SQLite EF logged no transaction Information lines).

| Journey | Run | Runtime RT | Runtime writes | Lease ops | Fence ops | All-EF RT | Wall clock |
|---|---|---|---|---|---|---|---|
| #3992 pending-parent-result crash | 1 | 3281 | 134 | 39 | 341 | 327139 | 77 s (xunit) |
| | 2 | 3194 | 135 | 40 | 330 | 306525 | ~75 s (steps 65.4 s + start/login) |
| | 3 | 3233 | 135 | 40 | 334 | 317449 | ~75 s (steps 65.7 s + start/login) |
| #3993 held native-worker retry → Done | 1 | 2170 | 494 | 101 | 245 | 18170 | 17 s (xunit) |
| | 2 | 2126 | 494 | 101 | 246 | 23305 | ~17 s (steps 6.6 s + start/login) |
| | 3 | 2136 | 495 | 101 | 245 | 18728 | ~16 s (steps 6.5 s + start/login) |
| #3995 callActivity pending-parent-resume | 1 | 628 | 113 | 18 | 48 | 19474 | 22 s (xunit) |
| | 2 | 637 | 117 | 18 | 52 | 19543 | ~21 s (steps 11.9 s + start/login) |
| | 3 | 633 | 114 | 18 | 49 | 19325 | ~21 s (steps 11.8 s + start/login) |

Class wall clock for the three Facts together: run 1 **117.6 s**, run 2 **114.7 s**, run 3 **114.4 s**. All 9 executions passed. No 30 s post-restart timeout.

**#3992 all-EF spike:** `parent-consumes-Completed` waits on the one-minute `ClaimVisibilityTimeout` fence (~52–54 s). Information-level EF logging of every poll (design/identity included) produces ~300k all-EF round-trips in that step; runtime round-trips are ~2600 and runtime writes are 35. This is a method limit of logging during the fenced wait, not a product defect and not a budget.

### #3992 per-step (latency ms · all-EF RT · runtime RT · all-EF writes · runtime writes · lease · fence)

| Step | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| publish-bpmn-model | 357 · 1045 · 42 · 992 · 7 · 6 · 2 | 299 · 1073 · 36 · 1024 · 8 · 7 · 1 | 294 · 1153 · 41 · 1100 · 8 · 7 · 2 |
| submit-native-parent | 325 · 1110 · 7 · 1081 · 0 · 0 · 1 | 310 · 1051 · 7 · 1022 · 0 · 0 · 1 | 332 · 1398 · 16 · 1353 · 0 · 0 · 2 |
| publish-native-parent | 583 · 1968 · 83 · 1847 · 14 · 8 · 5 | 582 · 2175 · 83 · 2051 · 14 · 8 · 5 | 594 · 2458 · 81 · 2330 · 14 · 8 · 5 |
| execute-parent-admit-child | 1149 · 3018 · 121 · 2868 · 32 · 8 · 16 | 1197 · 3855 · 121 · 3703 · 32 · 8 · 16 | 1159 · 3474 · 116 · 3311 · 32 · 8 · 15 |
| wait-cross-model-human | 656 · 2124 · 145 · 1959 · 24 · 4 · 15 | 558 · 1048 · 145 · 873 · 24 · 4 · 15 | 566 · 1861 · 145 · 1679 · 24 · 4 · 15 |
| complete-child-before-parent-consume | 1372 · 4140 · 121 · 3933 · 19 · 8 · 9 | 1442 · 4612 · 119 · 4408 · 19 · 8 · 8 | 1527 · 5157 · 139 · 4917 · 19 · 8 · 9 |
| kill-before-parent-consume | 1180 · 50 · 0 · 50 · 0 · 0 · 0 | 1178 · 181 · 0 · 178 · 0 · 0 · 0 | 1186 · 0 · 0 · 0 · 0 · 0 · 0 |
| restart-host | 6925 · 6689 · 68 · 6141 · 3 · 0 · 8 | 7218 · 7132 · 68 · 6575 · 3 · 0 · 8 | 7169 · 6721 · 68 · 6170 · 3 · 0 · 8 |
| parent-consumes-Completed | 54321 · 306995 · 2694 · 299103 · 35 · 5 · 285 | 52651 · 285398 · 2615 · 277926 · 35 · 5 · 276 | 52796 · 295227 · 2627 · 287590 · 35 · 5 · 278 |

### #3993 per-step

| Step | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| publish-native-worker | 568 · 1694 · 16 · 1630 · 0 · 0 · 2 | 547 · 1911 · 23 · 1834 · 0 · 0 · 3 | 549 · 1911 · 23 · 1834 · 0 · 0 · 3 |
| publish-worker-artifact | 704 · 2169 · 74 · 2045 · 10 · 3 · 6 | 680 · 2570 · 65 · 2449 · 10 · 3 · 5 | 648 · 2402 · 65 · 2284 · 10 · 3 · 5 |
| publish-bpmn-model | 222 · 672 · 52 · 622 · 13 · 14 · 1 | 231 · 880 · 54 · 825 · 13 · 14 · 1 | 231 · 885 · 54 · 827 · 13 · 14 · 1 |
| submit-native-parent | 25 · 148 · 0 · 141 · 0 · 0 · 0 | 22 · 155 · 0 · 150 · 0 · 0 · 0 | 17 · 18 · 0 · 8 · 0 · 0 · 0 |
| publish-native-parent | 136 · 401 · 76 · 314 · 17 · 12 · 3 | 115 · 535 · 76 · 446 · 17 · 12 · 3 | 114 · 511 · 76 · 427 · 17 · 12 · 3 |
| execute-parent-admit-child | 1212 · 3616 · 130 · 3467 · 35 · 12 · 16 | 1173 · 4236 · 143 · 4065 · 38 · 12 · 20 | 1192 · 3049 · 123 · 2897 · 35 · 12 · 15 |
| wait-cross-model-service | 644 · 1048 · 171 · 860 · 28 · 8 · 15 | 620 · 2102 · 158 · 1928 · 25 · 8 · 11 | 587 · 1693 · 167 · 1486 · 27 · 8 · 14 |
| wait-held-incident | 1554 · 3702 · 729 · 3005 · 186 · 14 · 95 | 1360 · 4487 · 712 · 3860 · 186 · 14 · 95 | 1580 · 4554 · 746 · 3872 · 186 · 14 · 95 |
| retry-held-service | 125 · 452 · 104 · 366 · 23 · 16 · 9 | 113 · 560 · 102 · 473 · 23 · 16 · 9 | 124 · 194 · 113 · 82 · 25 · 16 · 11 |
| parent-consumes-Done | 1633 · 4268 · 818 · 3465 · 182 · 22 · 98 | 1693 · 5869 · 793 · 5070 · 182 · 22 · 99 | 1473 · 3511 · 769 · 2784 · 182 · 22 · 98 |

### #3995 per-step

| Step | Run 1 | Run 2 | Run 3 |
|---|---|---|---|
| submit-native-child | 493 · 1617 · 17 · 1563 · 0 · 0 · 2 | 500 · 1893 · 23 · 1833 · 0 · 0 · 3 | 507 · 1895 · 23 · 1834 · 0 · 0 · 3 |
| publish-native-child | 734 · 2447 · 67 · 2335 · 10 · 3 · 5 | 639 · 2564 · 65 · 2451 · 10 · 3 · 5 | 660 · 2567 · 65 · 2452 · 10 · 3 · 5 |
| submit-callActivity-parent | 68 · 250 · 7 · 211 · 0 · 0 · 1 | 88 · 248 · 0 · 213 · 0 · 0 · 0 | 65 · 268 · 0 · 236 · 0 · 0 · 0 |
| publish-callActivity-parent | 223 · 714 · 60 · 627 · 12 · 6 · 3 | 176 · 758 · 60 · 671 · 12 · 6 · 3 | 213 · 892 · 62 · 803 · 12 · 6 · 3 |
| execute-parent-admit-child | 1481 · 3460 · 122 · 3303 · 34 · 6 · 16 | 1317 · 4039 · 132 · 3869 · 37 · 6 · 18 | 1323 · 4872 · 122 · 4707 · 34 · 6 · 16 |
| wait-child-complete-parent-waiting | 546 · 1701 · 142 · 1530 · 20 · 3 · 6 | 482 · 1917 · 138 · 1744 · 20 · 3 · 7 | 519 · 1033 · 148 · 853 · 21 · 3 · 7 |
| kill-before-parent-consume | 56 · 63 · 0 · 63 · 0 · 0 · 0 | 62 · 113 · 7 · 107 · 1 · 0 · 1 | 48 · 0 · 0 · 0 · 0 · 0 · 0 |
| restart-host | 7286 · 7398 · 113 · 6669 · 18 · 0 · 9 | 7319 · 6739 · 143 · 5999 · 28 · 0 · 13 | 7150 · 7304 · 117 · 6574 · 18 · 0 · 9 |
| parent-consumes-Completed | 443 · 1824 · 100 · 1706 · 19 · 0 · 6 | 348 · 1272 · 69 · 1184 · 9 · 0 · 2 | 358 · 494 · 96 · 383 · 19 · 0 · 6 |

## G-e — recorded #3993 post-restart timeout (causal)

Docs only. No production change. Native and BPMN semantics are not merged. File:line citations below are on `origin/main` `a898bb87f2f9deda151de76738f7f0140d50c90d` unless a historical SHA is named.

### Reconciliation

The 8 November timeout recorded in [#3993](https://github.com/elsa-workflows/elsa-foundation/pull/3993) was **not** in `RunBpmnChildFaultHeldRetryTests`. That class is a native Root `RunBpmn` parent waiting on a direct BPMN `serviceTask` bound to a real native worker; it proves `HoldForRetry` / retry / `FinalFault` and **does not kill or restart** the host (`Essentials/tests/Activities/Bpmn/Hosting/Tests/RunBpmnChildFaultHeldRetryTests.cs`).

The failing assertion in related-filter run 1 was `RunBpmnPendingParentResultCrashTests` ([#3992](https://github.com/elsa-workflows/elsa-foundation/pull/3992)): `"The restarted host did not deliver exactly one native parent continuation."` That journey is a native Root `RunBpmn` parent waiting on a **human** `userTask` child. The child is not a native worker. Do not read the timeout as a G2.2 held-retry failure.

[#4044](https://github.com/elsa-workflows/elsa-foundation/pull/4044) later measured the same #3992 journey on the **hold-claim** tree: `parent-consumes-Completed` waited ~52–54 s on `ClaimVisibilityTimeout`. No 30 s timeout occurred there because the test no longer measures those 30 s from process startup.

### Causal chain on current main (PROVEN)

This is the path the current `#3992` Fact pins. It is the designed dead-deliverer claim fence, not a production defect.

1. The model becomes terminal. `BpmnEmbeddingDelivery` handles `Elsa.Bpmn.ResumeNativeParent.v1` (`BpmnEmbeddingBoundary.cs:24`, `BpmnEmbeddingDelivery.cs:34–51`) and, when the native parent is not yet resumed, may take the test hold before `IBookmarkResumeDispatcher.DispatchAsync`.
2. Delivery is not free: `RuntimePostCommitOutboxProcessor.ProcessAsync` claims first (`RuntimePostCommitOutboxProcessor.cs:160–173`) with `visibilityTimeout: ClaimVisibilityTimeout`. That constant is **one minute** (`RuntimePostCommitOutboxProcessContracts.cs:13`).
3. `RuntimePostCommitOutboxClaimTransitions.Claim` writes status `Delivering` and `DeliveryVisibleAfter = now + VisibilityTimeout` (`RuntimePostCommitOutbox.cs:610–624`). EF persists that row in `EfRuntimePostCommitOutboxStore.ClaimAsync` (`EfRuntimePostCommitOutboxStore.cs:533–538`).
4. Current test hold mode `"hold-claim"` (`OwnedWorkbench.HoldClaimedBpmnParentResume`, `OwnedWorkbench.cs:152`) then `Task.Delay(Infinite)` inside the already-claimed handler (`BpmnEmbeddingDelivery.cs:115–116`). `CompleteClaimAsync` never runs. `StopAsync` is `Process.Kill(entireProcessTree: true)` (`OwnedWorkbench.cs:309`).
5. After kill the physical source is still `Delivering` with the same `deliveryVisibleAfter` (`RunBpmnPendingParentResultCrashTests.cs:106–111`). A later claim is refused while that stamp is in the future (`RuntimePostCommitOutbox.cs:593–594`: `Delivering` is claimable only when `visibleAfter <= now`).
6. A 30 s wait **from restart** expires first: the remaining fence is up to one minute minus however long the predecessor held the claim. The current Fact therefore starts its 30 s bound at `recoveryEligibleAt = max(visibleAfter, restartedAt)` (`RunBpmnPendingParentResultCrashTests.cs:121–124`) and requires the witness to observe the parent still `Running` while `observedAt < visibleAfter` (`:134–137`).
7. `ResumeNativeParent.v1` is registered `UntilAcknowledged(1s)` (`ActivitiesBpmnHostingFeature.cs:41–43`). That delay applies only after a completed claim is recorded `FailedRetryable` (`RuntimePostCommitOutbox.cs:707–721`; log `RuntimePostCommitRetryDeferred` / EventId 67901 at `RuntimePostCommitOutboxProcessor.cs:740–757`). The hold-claim path never reaches that recording, so the 1 s policy does not explain a 30 s wait.

### 8 November #3993 related-filter run 1 (UNKNOWN which side of the then-current race)

At #3993's base `eb4c6a616` the crash Fact used `HoldNativeParentResume()` (`"hold"`), waited only until model `TerminalPendingSettlement` + native parent `Running`, asserted the result source was **not** `Delivered` (`status != 2`), then measured 30 s from restart. `"hold"` throws `Pending()` after the claim (`BpmnEmbeddingDelivery.cs:117`) instead of delaying forever. If `CompleteClaimAsync` finished, the item became `FailedRetryable` with `AvailableAt = recordedAt + 1s` and `deliveryVisibleAfter: null`, which a 30 s wait would cover. If kill landed while the row was still `Delivering`, the one-minute fence remained and the 30 s wait expired.

No retained Workbench or SQLite logs from that run exist here. The failure was timing-shaped (run 1 timed out; runs 2 and 3 passed). **UNKNOWN** which of those two post-kill statuses run 1 actually had. Do not infer it was `Delivering`.

**Ruled out**

- `#3993` held-retry Facts themselves (no restart; related-filter run 1 still passed those Facts).
- `UntilAcknowledged(1s)` as a 30 s wait by itself (only after a completed `FailedRetryable` recording).
- Merging this with native-worker `HoldForRetry` / `RetryServiceAsync` (G2.2 / G2.4 A3) or with callActivity → `DispatchWorkflow` resume (`#3995` uses the other hold site, `ParentResumeExecutor`).
- A production defect: a dead deliverer's `Delivering` row remaining hidden until `ClaimVisibilityTimeout` is the documented claim rule (`RuntimePostCommitOutboxProcessContracts.cs:8–11`). Later the same day, `f96d6d0bb` pinned `hold-claim` so the crash Fact no longer depends on that race, and `9c67f6e35` moved the 30 s bound onto eligibility.

### Local re-run on this slice

Hosted Facts on this VM against `a898bb87f`, SQLite Owned Workbench, one process. SDK 10.0.404. Evidence directory `/tmp/g-e-evidence` (`ELSA_BPMN_HOST_EVIDENCE_DIRECTORY`). First `dotnet test` of the hosting project stopped at MSB3030 until `python3 tools/bpmn/restore_evidence.py` restored the gitignored spec 1017 fixtures; Workbench itself had already built. Then:

| Class | Result | Wall clock |
|---|---|---|
| `RunBpmnPendingParentResultCrashTests` | pass | 1 m 26 s |
| `RunBpmnChildFaultHeldRetryTests` (3 Facts) | pass | 16 s + 15 s + 15 s |
| Filter total | **4/4 passed** | 2.23 min (plus incremental build) |

`#3993` Facts still have no kill/restart. Receipt `root-pending-parent-result-crash/case.json` / `pending-checked.json` on this run:

| Field | Value |
|---|---|
| `sourceRevision` | `a898bb87f2f9deda151de76738f7f0140d50c90d` |
| predecessor exit | 137 (`SIGKILL`) |
| `ResumeNativeParent.v1` after kill | `status` 1 (`Delivering`), `deliveryFencingToken` 1, `deliveryVisibleAfter` `2026-11-11T16:26:44.8358536Z`, owner `runtime-outbox-e6ab8fc5f5504d7d81e1d1d7b3e39d0d` |
| `restartedAt` | `2026-11-11T16:25:54.4042752Z` |
| `recoveryEligibleAt` | same as `visibleAfter` |
| `claimWaitMilliseconds` | 50431.6 (~50.4 s) |
| `settledAt` | `2026-11-11T16:26:47.4182387Z` |
| `settlementAfterEligibilityMilliseconds` | 2582.4 |

A 30 s wait measured from `restartedAt` would have expired at `16:26:24.4Z`, **20.4 s before** `visibleAfter`. That is the same shape as the recorded #3993 related-filter message, on the current hold-claim path. EventId 67901 was not expected and was not required: hold-claim never completes the claim as `FailedRetryable`. `StartEmbedded.v1` in the same pending snapshot is `FailedRetryable` (`status` 3) with `deliveryVisibleAfter: null` — that is the ordinary start-embedded `Pending()` after enqueue (`BpmnEmbeddingDelivery.cs:95`), not the parent-resume wait.

### Runnable example

There is no separate sample app for this milestone. The hosted Facts **are** the runnable example (SQLite Owned Workbench, one in-process host):

| Journey | Class | What it runs |
|---|---|---|
| Held native-worker retry under `RunBpmn` | `RunBpmnChildFaultHeldRetryTests` | [#3993](https://github.com/elsa-workflows/elsa-foundation/pull/3993) / G2.4 A3. No restart. |
| Post-restart pending-parent-result (human `userTask` child) | `RunBpmnPendingParentResultCrashTests` | [#3992](https://github.com/elsa-workflows/elsa-foundation/pull/3992). This timeout's class. |
| Same #3992 journey with EF capture | `G3RemainingJourneyOverheadTests.RunBpmn_pending_parent_result_crash_records_overhead` | [#4044](https://github.com/elsa-workflows/elsa-foundation/pull/4044) slice 6. |
| Wait / restart / consume | `RunBpmnWorkbenchTests` | Historical `#3410` happy path. |

Project: `Essentials/tests/Activities/Bpmn/Hosting/Tests/Elsa.Activities.Bpmn.Hosting.Tests.csproj`. Filter example: `FullyQualifiedName~RunBpmnPendingParentResultCrashTests`.

### Supported profile

No dedicated `supported-profile.md` exists among `docs/reports/exec-core-*`. The profile already stated for this milestone:

- [Unified execution — Active milestone](../program-goals/unified-execution.md): SQLite, built-in host, root native parent, existing supported BPMN human/service-task paths. Completion leaves Program #2892 open.
- [G2 Limit](exec-core-g2-child-outcome-mapping.md#limit-every-hosted-cell): SQLite Owned Workbench, one in-process host, Root unless a cited test says otherwise. No PostgreSQL / SQL Server / MySQL cell. Direct-plane `CallActivity` refused at publish.
- [S8 unsupported / open](exec-core-migration-rollback.md#6-unsupported--open) and the operator note [native-bpmn-migration-and-rollback.md](native-bpmn-migration-and-rollback.md): no live native↔BPMN execution rewrite; publish pipelines stay separate; physical effects remain at-least-once.

This G-e note does not enlarge that profile.
