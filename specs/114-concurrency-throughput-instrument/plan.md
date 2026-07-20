# Implementation Plan: Concurrency / throughput instrument

**Spec**: [spec.md](./spec.md) · **Branch**: `worktree-agent-a262ede19cf609747` · **Status**: implemented

## Approach

An instrument-only unit: no product code changes. Extend the existing benchmark project with a
concurrency benchmark, reusing the existing harness and graph shapes. Three edits, all additive.

### 1. Additive harness parameterization (`tests/Elsa/Activities/Testing`)

The shared `WorkflowExecutionHarness` hard-coded a single execution id (`wfexec-1`) and identity
(`artifact-1`) — used by ~55 test files, so every change is additive and existing call sites are
untouched.

- `DeterministicRuntimeExecutionIdGenerator`: new constructor overload taking an explicit
  `workflowExecutionId` (the old constructor delegates with the default constant).
- `WorkflowExecutionHarness`: instance fields for the execution id + identity (defaulting to the existing
  constants); a new `Build(identity, workflowExecutionId, activityIds)` overload; a new
  `NewExecutable(root, identity)` static overload; `ExecutionId` / `ExecutableIdentity` accessors. Existing
  `Build(...)` / `NewExecutable(root)` / `WorkflowExecutionId` / `Identity` are unchanged and now delegate
  to the parameterized forms with the defaults.

This lets N harnesses run distinct executions against one shared store.

### 2. Shared graph builders (`benchmarks/.../BenchmarkWorkflows.cs`)

Extract the 2-node and hot-loop graph builders (previously private in `EngineExecutionBenchmarks`) into an
`internal static BenchmarkWorkflows`, made identity-aware (optional per-execution identity). The single-run
suite forwards its private builders to it (its `[Fact]` bodies and docs are unchanged); the concurrency suite
uses it directly. DRY: both suites now build byte-identical graphs from one place.

### 3. Concurrency benchmark (`benchmarks/.../EngineConcurrencyBenchmarks.cs`)

One `[Fact]` drives N ∈ {1, 8, 32, 128} concurrent hot-loop×10 executions across three backends
(in-memory, isolated-sqlite, shared-sqlite; shared-postgres if the Testcontainers driver reuses cleanly).
For each (backend, N): build N harnesses (distinct id + identity + a **distinct** activity-execution-id pool
per run — production ids are globally unique; the checkpoint-commit marker is keyed on a work-item-derived
CommitId with no execution-id partition, so a shared store needs unique ids or concurrent runs collide),
pay per-provider setup before the timed window, then `Task.WhenAll` the timed runs. Report total wall,
per-run p50/p95/min/max, aggregate commits, commits/run, throughput.

## Measurement discipline

- Run `uptime` first; the benchmark also stamps load into its output. Machine load has invalidated runs
  before — wall times are reported with that caveat; **commit counts are the deterministic evidence**.
- One warmup pass per backend (discarded) before the measured levels.
- No hard perf assertions (they flake). Each run asserts only that its workflow completed.

## Verification

- `dotnet build` the benchmark project and the touched `Elsa.Activities.Testing` project — both clean.
- Run a representative harness-consumer test project to confirm the additive harness changes don't regress.
- Run the concurrency benchmark; record the curve + bottleneck analysis in [research.md](./research.md).
