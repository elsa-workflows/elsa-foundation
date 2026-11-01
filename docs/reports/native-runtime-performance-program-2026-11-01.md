# Runtime Efficiency continuation evidence

Program [#2531](https://github.com/elsa-workflows/elsa-foundation/issues/2531), draft integration [#3601](https://github.com/elsa-workflows/elsa-foundation/pull/3601).
This is an in-progress receipt. It does not establish completion of M0–M5, current-main throughput, or independent-host capacity.
The [baseline assessment](native-runtime-performance-2026-10-31.md) and [first implementation receipt](native-runtime-performance-implementation-2026-10-31.md) retain their original cohorts and exclusions.

## Hydration and preparation

Hydration metadata (#2657) merged in #3598 as `11a07e86686c68bb523334409e66f39deaed068c`, after review and required checks on `f445cea8d370e24c7b9f80a9ce89c4ce6e927e27`.
The first main run was cancelled when main advanced. Containing main `1d77ed131de3305da8b8236f739c63085bff3e73` then passed [CI 41289485400](https://github.com/elsa-workflows/elsa-foundation/actions/runs/41289485400).

Integration head `36444c42792d96e13f9c05a98bd73e9c07ed3ddb` adds demand-driven input reads and weak immutable JavaScript/conversion preparation. Independent early review found fresh-hydration script misses and missing closed-scope read-budget proof; both were addressed and the next three-lens review was clear. Final integrated qualification remains due.

## Owned activity reads: rejected experiment

Three variants tested #2583: private provider-scope rows, sharing private rows under the actual live ownership lease, and priming that lease's bounded read memo from a successfully committed fresh checkpoint. Mutable runtime documents were still reconstructed on each read. Replay and uncertain outcomes never primed the memo. These were experiments and the entire prototype was removed.

| SQLite reference workflow | Current candidate, Durable | Read memo | Commit-primed memo |
|---|---:|---:|---:|
| Ten WriteLines | 336 | 336 | 336 |
| HTTP reference | 204 | 204 | 204 |
| Fork/join with resume | 253 | 252 | 251 |

Balanced/Ephemeral were 90, 104, 180 respectively; only fork/join changed, to 179 and then 178. All ten budget tests passed in each variant. The extra durable-state ownership and invalidation machinery did not justify saving two commands in one fixture. No budget was lowered and no throughput gain is attributed to #2583. The issue's acceptance remains open.

## Cooperative cancellation of a progressing native drain

The default actor composition publishes a mailbox-local signal while a valid Cancel waits behind an active turn. Discrete and replay-safe fused steps notice it before the next step. The orchestrator flushes its folded segment through the existing fenced checkpoint path and returns `Yielded`, releasing the lease and mailbox so the normal Cancel handler can execute. A cancelled waiter withdraws its signal. The configured concrete local provider is shared with distributed placement. An activity already running must finish or cooperate before handoff; delivery through remote transport is separately qualified.

Six real SQLite integration cases cover External and ReplaySafe loop bodies under Durable, Balanced and Ephemeral. Each starts an otherwise unbounded progressing While with no total-cycle cap, requests Cancel through the public actor API while the first body is executing, and proves one completed body, durable Cancelled state, a yielded turn and unchanged activity state after a resumption sweep. A bounded runaway guard makes removal of the behavior fail without hanging.

Three mailbox cases cover a valid waiting Cancel, cancellation of the waiter, and rejection of another partition. They also check that the signal is execution-specific and is cleared before the Cancel turn.

| Deliberate mutation | Failing controls | Restored |
|---|---:|---:|
| Remove scheduler yield check | 6/6 | 6/6 pass |
| Remove fused-step yield check | 3/6 | 6/6 pass |
| Keep a cancelled/acquired waiter's signal | 2/3 | 3/3 pass |
| Omit final folded-segment flush | 6/6 | 6/6 pass |

Whole affected suites on the restored candidate: runtime 3,122 passed, HTTP integration 101 passed, zero failed/skipped. Discovery accounts for all cases: HTTP lists 101; runtime lists 3,111 including three unexpanded theories that execute as 14 rows (4, 2 and 8), yielding 3,122. The nine SQLite command counts stayed 336/204/253 (Durable) and 90/104/180 (Balanced/Ephemeral). The new idle intent check adds no EF command; no budget was raised. These are correctness and command-count results, not throughput measurements.

The first compile attempt used the wrong start-dispatch overload; the first behavioral attempt omitted the resumption-service registration in the test fixture. Those setup failures are retained separately and are not causal red controls. A subsequent replay-safe case exposed one extra fused body iteration; the explicit fused-step check fixed it.

## Original incomplete-start investigation

The original failed snapshots remain unexplained. A new cohort ran the unchanged original local Perf binary and runtime in 100 fresh macOS processes, with the original C1/Memory/Balanced/one-leaf shape and an exit-only startup hook to capture the first failing heap. All 1,425,291 total executions completed, with no capture triggered. Eight earlier original diagnostic processes and eight independent Linux diagnostic processes also completed, in separate cohorts. These passes establish neither a causal explanation nor a fix. No throughput inference is made from these shared-machine correctness runs.

## Evidence custody and remaining work

Raw logs, TRX, source snapshots, rejected cache patches, hashes and experiment scripts are retained at `/private/tmp/elsa-runtime-efficiency-program` on the qualification machine. Frozen original binaries and original failed results remain under `/private/tmp/elsa-native-perf-20261031`. Heap files, if captured, are private diagnostic artifacts and are not committed.

Remaining acceptance is tracked on #2531 and spec 1183: reconcile older objectives, resolve the original anomaly and identifier concerns, deliver applicable persistence/scheduling/diagnostics work, run representative sustained and failure/recovery cohorts including independent hosts, then review and qualify the exact integration head and containing main. This report is not a substitute for those gates.
