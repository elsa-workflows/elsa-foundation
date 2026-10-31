# Native runtime performance implementation — 31 October 2026

Status: implementation and controlled comparison qualified locally; CI and final Code Review remain pending. Issue [#3542](https://github.com/elsa-workflows/elsa-foundation/issues/3542), PR [#3545](https://github.com/elsa-workflows/elsa-foundation/pull/3545), [spec 1180](../../specs/1180-native-runtime-performance/spec.md). This follows the [baseline assessment](native-runtime-performance-2026-10-31.md); it is owner-requested evidence, not a new CI timing gate.

## Implementation

- The default empty-input materializer skips construction of execution history, durable-value projections and variable frames. It still invokes the normal snapshot writer, observes cancellation and validates the input contract. Custom materializers receive their complete current context.
- Immutable executable-node preparation reuses binding-set validation, input ordering and the binding fingerprint. A weak table owns preparation by actual node identity. Evaluated values, registry-dependent resolutions, invocation snapshots and scoped services remain per invocation. Pinned input dictionaries now reject mutation through a downcast.
- Coalescing sessions reuse sorted activity/value overlays and parent-filtered activity views. Binary successor lookup replaces rebuilding and sorting the whole overlay for each returned item. Activity/value changes invalidate their corresponding views. Durable reads, authorization, continuation and existing session boundaries remain in place.
- EF binding reuses provider-method lookup and compiled configuration factory code by actual runtime type. Weak keys permit collectible providers to unload. Schema, history-table name and migrations assembly are per-call arguments, not cached configuration. Pooling defaults are unchanged.

The existing bounded executable caches remain the first layer. This adds reusable preparation to the objects they already own; it does not introduce a distributed cache or cache live workflow state globally. No persistence mode, checkpoint cadence, durable schema, public execution-model contract or concurrency policy changes.

## Correctness and lifetime evidence

The final affected-project runs contain 7,282 cases: **7,281 passed, one optional SQLite stress case skipped**. Every listed test maps to a result. Several suites expand theories at execution; the accounting file maps those expansions and the skipped case rather than presenting discovery and execution counts as identical.

| Project | Passed | Skipped |
| --- | ---: | ---: |
| Workflows.Runtime | 3,065 | 0 |
| EF BindingDrift | 23 | 0 |
| Activities.Runtime | 720 | 0 |
| Sequence | 22 | 0 |
| ControlFlow | 238 | 0 |
| Flowchart | 121 | 0 |
| Persistence.EntityFramework | 710 | 1 |
| Runtime.Persistence.EntityFrameworkCore | 1,729 | 0 |
| Workflows.Publishing | 61 | 0 |
| Architecture | 592 | 0 |

Workbench Debug and the Release probe build passed. Generated-map/solution-filter checks and architecture graph restore passed. The initial architecture run failed its exact source inventory because the new provider test file was missing; registering that file restored the complete suite.

Rebuilt Workbench processes with isolated SQLite stores passed all ten JSON capture/default/typed-variable HTTP cases, a JavaScript loop printing exactly 1 through 10 with no incidents, and suspension followed by an actual process restart and resume. The restart retained the variable and executed Set once, with no duplicate effect. Each fixture stopped only its owned process and removed its disposable content root. An initial e2e launch failed before host startup because an older temporary PowerShell installation lacked a working `Resolve-Path`; the complete runs used the verified 7.6.9 installation.

Causal controls failed with the relevant optimization removed: zero-input store reads, repeated fingerprint allocation, repeated overlay reconstruction and repeated EF expression compilation. The read-only contract assertion failed before its fix. Additional mutations caused the intended failures for an injected workflow-state read, omitted custom request inputs and strongly retained collectible provider delegate types. Production sources were restored before the final complete-suite runs. Two independent review rounds covered correctness, repository standards and acceptance; the second left no actionable findings.

## Controlled comparison

The comparison contains **20 accepted process runs and 19,997 measured completed workflows**. Nine additional attempts were excluded for external CPU interference; they remain in the evidence archive. All accepted runs passed completion, exact leaf/state counts, incident, queue and outbox checks, and the expected checkpoint/transaction audit. The comparison uses the original frozen `ab46bc22a52aa6e456bca6ac0e803c80904de081` baseline and production implementation from `adf1929371c77c68589f22b0ba4b8ed9ec96c3ea`. The candidate directory starts as an exact copy of the baseline and replaces only `Elsa.Workflows.Runtime.dll`, `Elsa.Activities.Runtime.Core.dll` and `Elsa.Persistence.EntityFramework.dll`. All other DLLs, including the probe itself, match by SHA-256. Candidate production code is unchanged since `54c9b8dc`; subsequent changes strengthen tests.

Each cell runs twice in fresh processes/databases, with baseline/candidate order reversed in repetition two. The matrix covers Balanced at 1, 10 and 100 leaves with one caller, Balanced at 10 leaves with sixteen callers, and Durable at 10 leaves with one caller. Each interval runs at least twenty seconds and 100 completions after warmup. Hardware, runtime, PostgreSQL settings, no-op leaf semantics and external-load exclusions match the baseline assessment. No EF context pooling is enabled. Results rejected for external CPU interference are retained separately.

Throughput, allocation and p95 below are medians of the two process results; ranges are the observed minimum and maximum throughput, not confidence intervals. One leaf is a standalone activity; ten and 100 leaves include a Sequence, for 11 and 101 total activity executions. SQL/transaction counts come from one separate untimed accounting dispatch per process and are EF interceptor counts, not physical round trips. The command counts agree between the two repetitions of each variant.

| Mode | Leaves | Callers | Variant | WF/s | Observed range | p95 ms | MiB/WF | EF commands | Checkpoints / EF tx |
| --- | ---: | ---: | --- | ---: | --- | ---: | ---: | ---: | --- |
| Balanced | 1 | 1 | baseline | 46.38 | 43.12–49.64 | 38.0 | 2.31 | 46 | 1 / 2 |
| Balanced | 1 | 1 | candidate | 48.83 | 43.91–53.75 | 36.9 | 2.29 | 45 | 1 / 2 |
| Balanced | 10 | 1 | baseline | 20.61 | 18.50–22.72 | 89.2 | 8.74 | 76 | 1 / 2 |
| Balanced | 10 | 1 | candidate | 28.17 | 26.55–29.78 | 67.0 | 8.13 | 65 | 1 / 2 |
| Balanced | 10 | 16 | baseline | 148.62 | 146.19–151.04 | 261.2 | 8.71 | 76 | 1 / 2 |
| Balanced | 10 | 16 | candidate | 183.03 | 173.60–192.46 | 169.1 | 8.10 | 65 | 1 / 2 |
| Balanced | 100 | 1 | baseline | 2.24 | 2.21–2.27 | 542.5 | 227.19 | 391 | 3 / 7 |
| Balanced | 100 | 1 | candidate | 5.69 | 5.63–5.75 | 284.2 | 70.02 | 290 | 3 / 7 |
| Durable | 10 | 1 | baseline | 5.66 | 5.49–5.84 | 366.0 | 27.09 | 286 | 12 / 25 |
| Durable | 10 | 1 | candidate | 6.15 | 5.94–6.35 | 263.5 | 23.95 | 275 | 12 / 25 |

In this cohort, Balanced ten-leaf throughput improves by **37%** at one caller and **23%** at sixteen callers; the 100-leaf result improves by **154% (2.54×)** and managed allocation falls by **69%**. Durable ten-leaf throughput improves by **9%**, with **12%** less allocation. The one-leaf median improves by 5%, but its observed ranges overlap substantially, so that small timing change should be treated cautiously. Both repetitions improve in every measured cell, with unchanged checkpoint/transaction counts.

The larger gain than the earlier isolated prototype is not proof of an additional independent speed multiplier: that was a different cohort. Do not add or multiply its percentages, the pooling experiment, and these combined results. This shared-machine policy removes major interference but does not establish dedicated hardware, confidence intervals, multi-machine scaling, or production SLOs. The 100-leaf baseline repetitions contain only 100 measured completions each, which limits tail inference.

[Raw results, hashes, test accounting, controls and reproduction notes](evidence/native-runtime-performance-implementation-2026-10-31/README.md) preserve both accepted and excluded observations. The disposable PostgreSQL container and its anonymous volume were removed and verified absent; the backend fixtures also verified their owned content roots were removed.

## Limits and next work

These are native, warmed, no-op workflows, not a capacity promise. The test journeys prove selected expressions, variable updates and restart semantics; they do not measure their throughput. The repeated comparisons assess this implementation as a group and do not attribute a separate percentage to each optimization.

The next performance work should profile representative payloads, expressions, definition diversity and suspension/resumption. The default path still builds the complete context when any input exists; literal-only and narrowly scoped bindings are candidates for further demand-driven reads once their dependencies are explicit. Then qualify workload-specific concurrency/backpressure and tenant-safe context pooling. A durable execution-state cache needs an explicit freshness, ownership and authorization protocol before implementation. This change does not establish BPMN/ETL performance, multi-machine failover, distributed transport scaling or a fix for the separately observed incomplete in-memory start.
