# Store-performance adapter child host (#646)

The harness in `../Elsa.Groundwork.StorePerformance.Benchmarks` deliberately ships no adapter — "a missing
adapter is a blocked run, never a simulated result" — so `matrix` refuses to start without
`--child-command`. This project is that command: the leaf that binds the frozen workload contracts to real
Groundwork provider drivers.

## Status

| piece | state |
|---|---|
| CLI, run-request wire contract, native-plan staging | **done**, covered offline by `tests/Elsa/Groundwork/StorePerformance/AdapterHost/Tests` |
| `capture-plan` for routeless workloads | **done** (`checkpoint-commit`) |
| `capture-plan` for workloads with native routes | **done for SQLite** — public runtime queries and real provider plans; other providers fail closed |
| adapter leaves | **done for the E3 baseline set**: `checkpoint-commit`, `bookmark-lookup`, `queue-drain`, and `outbox-drain` |

All four leaves compose the real Groundwork runtime stores and execute frozen correctness through public
runtime contracts. Mutating measurements prepare their invocation-specific fixture before the stopwatch;
the timed body contains only the named public operation. `checkpoint-commit` can be measured on any of the
four providers; SQLite and PostgreSQL have been run historically. The three new leaves require routed
native-plan evidence; the current capture leaf proves those routes on SQLite and refuses unsupported
providers until a corresponding native-plan leaf is added.

The repository-relative `tools/groundwork/run-e3-medium-baseline.py` validates one real evidence set per
workload and prints the exact four `matrix medium` commands. Add `--execute` only after inspection:

```bash
python3 tools/groundwork/run-e3-medium-baseline.py \
  --provider sqlite \
  --evidence-dir "$STAGING" \
  --out "$ARTIFACTS"
python3 tools/groundwork/run-e3-medium-baseline.py \
  --provider sqlite \
  --evidence-dir "$STAGING" \
  --out "$ARTIFACTS" \
  --execute
```

The selected provider driver owns a fresh isolated connection/catalog for each child. The runner never
accepts or retains a connection string. It refuses missing, stale, mismatched, or invented route evidence
before launching a matrix. Measured artifacts also retain an exact provider-native command count for every
latency sample and the observer identity that produced it; adapter calls or estimates are not admissible.
The current exact observer is SQLite's `sqlite3_trace` on the actual measured provider connection; PostgreSQL,
SQL Server, and MongoDB measured children fail closed before preparation until equivalent native command hooks are
added.
The runner performs a fail-closed process audit immediately before each timed matrix and refuses to start while
unrelated `dotnet`, MSBuild, VSTest, testhost, or xUnit processes are active. Run timed cohorts on an idle or
isolated checkout/runner; correctness and native-plan capture remain useful when a timed cohort is interrupted,
but contaminated latency must not be published.

## Operating it

Both the artifact directory and the plan staging directory must live **outside the worktree**. Every child
re-verifies a clean HEAD with `--untracked-files=all`, so a stray file inside the repository aborts the run.

Build this host and the harness in the **same configuration**. Each child re-verifies the harness assembly
digest, and a Debug host cannot satisfy a Release matrix.

**Build once, then stage, then run — never rebuild in between.** The harness assembly is not byte-
deterministic: rebuilding it with no source change produces a different digest. Building the harness
project alone leaves this host's copied reference stale, and `capture-plan` bakes whichever digest *it*
loaded into the staged evidence — so a rebuild between staging and `matrix` fails every child closed. The
safe order is: build both projects, confirm the two copies of
`Elsa.Groundwork.StorePerformance.Benchmarks.dll` hash identically, then `capture-plan`, then `matrix`.

```bash
# Confirm digest parity before staging anything.
shasum -a 256 \
  benchmarks/Elsa.Groundwork.StorePerformance.Benchmarks/bin/Release/net10.0/Elsa.Groundwork.StorePerformance.Benchmarks.dll \
  benchmarks/Elsa.Groundwork.StorePerformance.AdapterHost/bin/Release/net10.0/Elsa.Groundwork.StorePerformance.Benchmarks.dll
```

Run the matrix **detached** (`nohup … & disown`). A cohort is tens of minutes of provider I/O, and a
terminal or session teardown kills it outright — leaving a partial artifact directory, which is deliberately
not resumable.

**Prefer a checkout you are not working in.** Because every child re-verifies a clean HEAD, *any* edit to
the repository while a cohort is running aborts the remaining children — including edits unrelated to the
benchmark, such as touching this README. Running the matrix from a dedicated clean checkout removes the
whole hazard; otherwise, stop editing for the duration of the run.

```bash
# 0. Read the provenance values off the provider itself. Correctness binds the *observed* provider
#    configuration to the requested one entry for entry, so these cannot be guessed.
adapter-host probe-provider --provider sqlite

# 1. Stage the native-plan evidence and copy the three values it prints. For the three routed workloads,
#    the current capture leaf is SQLite-only and executes the public runtime query leaves before staging.
adapter-host capture-plan --workload checkpoint-commit --provider sqlite \
  --cohort tierb-001 --measurement-set groundwork-shared-linked \
  --adapter groundwork --form shared-documents-with-linked-index-tables --scale 100k \
  --commit "$(git rev-parse HEAD)" --composition <64-hex> --provider-version <version> \
  --provider-setting <name=value from probe-provider> \
  --identity checkpoint-commit-sqlite-routeless --out "$STAGING"

# 2. Run the matrix. --child-command must be the built apphost binary, never `dotnet run`.
export ELSA_BENCH_NATIVE_PLAN_STAGING="$STAGING"
dotnet run -c Release --project ../Elsa.Groundwork.StorePerformance.Benchmarks -- matrix 100k ... \
  --child-command .../Elsa.Groundwork.StorePerformance.AdapterHost --out "$ARTIFACTS"
```

## Design decisions, and why

**The host lives under `benchmarks/` and references the driver library under `tests/`.**
`ArchitectureGuardTests.ProjectFiles()` enumerates only `src/` and `tests/`, so nothing here is subject to
the path-convention or forbidden-reference rules; the 354-test architecture suite passes with the edge in
place. `benchmarks/Elsa/Workflows/Runtime/Benchmarks` already takes the same reference. The alternative —
duplicating the drivers — would let the benchmark environment drift from the one that produces the Spec 094
conformance evidence, and then the numbers would not describe the system the evidence describes.

**Plan capture is a separate pre-flight command, not something a child does.** `matrix` takes
`--native-plan-sha256` as an *input*, so the operator commits to a content digest before any child starts.
A child that captured its own plan would have to reproduce that digest byte-exactly across four processes
and, for server providers, four freshly started containers. Capturing once and republishing byte-for-byte
removes the whole class of nondeterminism and keeps capture off the timed path.

**A routeless workload still needs an evidence document.** `ArtifactAdmission.ValidateCorrectness` binds
~18 provenance fields between the request and the document and demands it unconditionally; the route list
is only part of the binding. This is why `checkpoint-commit` (`requiredNativeRoutes: []`) remains the simple
provenance slice, while the routed leaves use the same staging contract after their public query plans have
been captured and provider-validated.

**The host shares `ArtifactStore.JsonOptions` via `InternalsVisibleTo` instead of copying it.** Those
options are PascalCase and register no string-enum converter, so `ProcessKind` travels as a *number*. A
local copy would drift the moment a converter is added, and the drift would surface as a fail-closed
rejection minutes into a cohort — or, worse, parse a warmup request as measured.

## How the `checkpoint-commit` leaf works, and why

**Store composition — verified, and not obvious.** `GroundworkProviderClient.Services` contains *only*
`IDocumentStore` (plus `IBoundedDocumentStore` in physical mode). `AddGroundworkRuntimeStores()` does not
register everything the workload needs either: `IRuntimeExecutionOwnershipService` comes from
`AddWorkflowRuntime()` (`src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs:169`)
and has no Groundwork replacement. The order is therefore:

1. `services.AddSingleton<IPersistenceAccessContextAccessor>(...)` — **first**, because `AddPersistenceCore`
   uses `TryAdd` and its default `"default"`-scope accessor would otherwise win
2. `services.AddSingleton<IDocumentStore>(client.DocumentStore)` (+ `IBoundedDocumentStore` in physical mode)
3. `services.AddWorkflowRuntime()` — registers the in-memory defaults, the ownership service, and
   `IWorkflowExecutableRootWriteLeaseManager`, which `GroundworkRuntimeCheckpointWriter` requires
4. `services.AddGroundworkRuntimeStores()` — `RemoveAll`s the in-memory defaults and swaps in the bridges

**Physical mode is required, not preferred.** `BoundedDocumentStore` is only populated in physical mode,
and `GroundworkWorkflowExecutionStateStore` throws without one. It is also the only mode with a compiled
physical target, so a logical-mode shortcut would not describe the system the Spec 094 evidence describes.

**The correctness storage scope is dictated by the contract.** The frozen scenario stamps
`TenantId = "tenant-checkpoint"` onto every committed `WorkflowExecutionState`, and the bridges call
`PersistenceAccessContext.EnsureTenantScope(state.TenantId)` before any provider I/O — so the correctness
scope *must* be `tenant-checkpoint`. The benchmark rows live in a separate scope of the leaf's choosing.

**Operation decomposition.** `MeasureAsync` loops `while samples.Count < 100 || elapsed < 30s` — 30 seconds
*per operation*, always — and times everything inside `InvokeAsync`. So:

- Run the frozen scenario once in `VerifyCorrectnessAsync`, in its **own storage scope**. The scenarios
  assert exact observable results, so sharing a scope with benchmark rows fails correctness closed.
- Time the *elementary store call* each named phase is composed of, in a separate pre-seeded scope. The
  phase itself (1024 durable commits) is hours at 100 invocations.
- Nothing but the store call inside `InvokeAsync` — no reset, no client open, no schema work. Every
  workload's input carries `"timedSetup": "excluded"`.
- Warmup calls `InvokeAsync(-1L - i)` for 50 **negative** ordinals. Key identities so the warmup and
  measured namespaces cannot collide.

**Provider configuration is observed, never asserted.** `ValidateCorrectness` requires the observed
configuration to match the request entry for entry, so `probe-provider` reads it off the driver's own
sanitized diagnostics and prints the flags to pass. The `journalMode=wal --provider-setting
synchronous=full` pairing in the example above is *illustrative and false for this driver*: the provider
driver builds its SQLite connection string directly and bypasses Groundwork's connection factory, so it
applies no journal or synchronous pragma at all. Do not copy it — run the probe.

**Provider-configuration values are screened.** `ArtifactSafety` rejects any artifact string containing
`://` or a `keyword=`/`keyword:` pattern, where the keywords include `server`, `host`, `port` and
`database`. Concretely: the SQL Server image reference `mcr.microsoft.com/mssql/server:2022-CU21-...`
**will be rejected** because of `server:`. Record the bare tag, not the full reference. Config keys are
screened separately and may not match `server|host|endpoint|data source|database|initial catalog|port`.

**One measurement set is not a verdict.** `Comparison.Compare` needs eight artifacts in two distinct
measurement sets, so `compare` and `gate` will correctly refuse after a single run. That is expected: Tier B
ceilings need only one set — take the median-of-three p95 per operation (the same statistic `GateEvaluator`
uses) and multiply by 3.
