# Store-performance adapter child host (#646)

The harness in `../Elsa.Groundwork.StorePerformance.Benchmarks` deliberately ships no adapter — "a missing
adapter is a blocked run, never a simulated result" — so `matrix` refuses to start without
`--child-command`. This project is that command: the leaf that binds the frozen workload contracts to real
Groundwork provider drivers.

## Status

| piece | state |
|---|---|
| CLI, run-request wire contract, native-plan staging | **done**, covered offline by `tests/Elsa/Groundwork/StorePerformance/AdapterHost/Tests` |
| `capture-plan` for routeless workloads | **done** (`checkpoint-commit` is the only one) |
| `capture-plan` for workloads with native routes | **not started** — refuses rather than fakes |
| adapter leaves | **`checkpoint-commit`** (#1175). Every other workload is still a blocked run |

`checkpoint-commit` can be measured on any of the four providers; SQLite and PostgreSQL have been run.
The next unit is a leaf for a workload with native routes, which needs the route-capture side of
`capture-plan` first.

## Operating it

Both the artifact directory and the plan staging directory must live **outside the worktree**. Every child
re-verifies a clean HEAD with `--untracked-files=all`, so a stray file inside the repository aborts the run.

Build this host and the harness in the **same configuration**. Each child re-verifies the harness assembly
digest, and a Debug host cannot satisfy a Release matrix.

```bash
# 0. Read the provenance values off the provider itself. Correctness binds the *observed* provider
#    configuration to the requested one entry for entry, so these cannot be guessed.
adapter-host probe-provider --provider sqlite

# 1. Stage the native-plan evidence and copy the three values it prints.
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
is only part of the binding. This is why `checkpoint-commit` (`requiredNativeRoutes: []`) is the right
first slice — it exercises the whole provenance path without the route-capture and raw-plan-sanitization
subsystem existing.

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
