# Store-performance adapter child host — Groundwork v2 (#1425)

The harness in `../Elsa.Groundwork.StorePerformance.Benchmarks` deliberately ships no adapter — "a missing
adapter is a blocked run, never a simulated result" — so `matrix` refuses to start without a child command.
This project is that command, rebuilt on Groundwork v2 after #1420 deleted the v1 host along with the v1
substrate.

## Status

| piece | state |
|---|---|
| project, references, CLI scaffolding (`HostArguments`, `RunRequestWire`, `NativePlanEvidenceStaging`) | **done**, compiles |
| `WritePathRoundTripObserver` — the exact provider-native round-trip counter | **done**, compiles |
| `ProviderConnections` — opens a real v2 connection for all four providers | **done**, compiles |
| `probe-provider` | **done** — reads native server identity, topology, and sanitized driver settings |
| `capture-plan` | **done for checkpoint-commit** — emits its explicitly routeless provenance document; native-route capture remains a separate provider-specific task |
| optional-observer `src/` seam on `GroundworkV2RuntimeCheckpointWriter` | **done**, compiles |
| `RuntimeStoreComposition` — DI composition, unit admission, distinct clients | **done**, compiles |
| `CheckpointCommitAdapter` — correctness half, over the production commit path | **done**, compiles |
| `verify-correctness` command | **done**, compiles, **not yet run against a provider** |
| the five measured operations | **implemented** in the workload-owned phase adapter; no timed cohort claimed |
| a measured cohort | **not run**, and not runnable on a loaded machine by construction |

Nothing here has produced a measurement yet. No number in this project has been measured on v2.

## The finding that shapes the rest of the work

`ProcessMeasurement.ExecuteAsync` refuses any measured process whose observer is missing or inexact:

```csharp
observer = adapter.RoundTripObserver;
if (observer is null || !observer.IsExact)
    throw new PerformanceContractException(... "adapter command counts or synthetic estimates are not admissible.");
```

Groundwork v2 exposes `IProviderCommandObserver`, which counts every provider command issued by a session,
including reads, writes, probes, and retention. `RuntimeStoreComposition` registers one exact observer and
forwards it through both sessions and units of work. `IStorageAccessObserver` remains an audit hook for
privileged access, not a round-trip counter.

The write and read adapters in this host therefore use the same provider-native observer. Remaining
read-dominated workloads are unimplemented adapter leaves, rather than being blocked by an absent read seam.

A third point, and its resolution.

`GroundworkV2RuntimeCheckpointWriter` stages its writes with the static `WriteOptions.Unconditional` /
`WriteOptions.IfVersion(...)` singletons, whose `Observer` is null, so the production commit path could not be
instrumented from outside `src/`. Anything the leaf measured would have been a *reconstruction* of that path
from elementary store calls — a caveat that would have had to travel with every published number, because a
reader will otherwise assume the figure describes the path the product actually runs.

**Resolved, and implemented.** `GroundworkV2RuntimeCheckpointWriter` now takes an optional
`IWritePathObserver` and attaches it to every staged write through its two staging funnels (`Stage` and
`StageDelete`, so the coverage is every mutation rather than a remembered subset), and
`GroundworkV2RuntimeRegistration` resolves one with `provider.GetService<IWritePathObserver>()`.
`BatchWriteOptions` carries no observer, so per-write is the only place it can attach; the batched
unit-of-work path takes the observer from the first staged write of each chunk. Production is unchanged
when nothing registers one. The leaf therefore measures the production path directly and the
reconstruction caveat does not apply. Do not reintroduce it by timing hand-rolled store calls where the
public writer would do — that was a workaround for a constraint that no longer exists.

The v1 host's separate reason for timing elementary calls still stands and is unrelated: a named phase of
1024 durable commits is hours at 100 invocations, so the *decomposition* is about run time, not about
instrumentation.

## What remains

**The five measured operations.** The frozen spec names them `seed-fenced-executions`,
`commit-checkpoint-bundle`, `replay-equivalent-commit`, `attempt-stale-fence-commit` and
`reopen-and-read-committed-bundle`. `RuntimeCheckpointCommitWorkload` now exposes these phases through a
small provider-neutral operation surface. It retains ownership of execution identities, state changes,
outbox entries, payload sizing and fencing fixtures; `CheckpointCommitAdapter` only adapts those operations
to the process-measurement contract after correctness succeeds. Fixture setup runs outside each timed
callback, while the timed calls remain public runtime-store operations.

`ProcessMeasurement` enumerates `adapter.Operations` for **warmup** processes as well as measured ones, so
the adapter prepares the same five phases for either process kind. The separate `verify-correctness`
command remains useful for provider admission without running the timed protocol.

**Historical correctness evidence (not current-version acceptance).** The following results were produced
before this host's current probe/evidence path and must not be read as evidence for the current package family:

| provider | historical result (Groundwork `0.3.0-preview.1`, session-scoped observer) |
|---|---|
| sqlite | **passes** — result digest exactly matches the frozen `ebb92b59…`, **37 857** provider round trips |
| mongodb | **passes** — same digest, **38 751** round trips |
| postgresql | **fails in the concurrent stale-fence phase** — see elsa-workflows/elsa-foundation#1449 |
| sqlserver | fails identically to postgresql, same mechanism |

Counts made on `0.2.0-preview.2`'s retired write-path observer (4 096 sqlite / 10 240 mongodb on this same
workload) are **not comparable**: that write-only seam was blind to reads, so the ~9× increase is added
visibility, not added cost. They are historical diagnostics only; current evidence uses the provider-command
observer described above.

One diagnosis trap recorded from the migration run: `RuntimeExecutionOwnershipOptions.LeaseDuration`
defaults to one minute, and the workload heartbeats each lease before first commit — on a heavily loaded
host the acquire-to-heartbeat gap alone can exceed it, failing with "could not refresh its current
execution fence" on the slower providers while sqlite (first in line) still passes. That is a load
phantom, not a provider difference; the same run passes on a quiet host.

The two failures are not adapter bugs: `GroundworkStorageSessionSource` hands one cached session (one
physical connection) to every caller with matching access, and PostgreSQL/SQL Server refuse concurrent
commands on one connection. The concurrent phase collides first in the checkpoint writer's marker read
(fixed here — serialized on the cached session instance, because the writer's contract tests forbid
opening a unit of work before lease handling) and then in the executable store's own reads
(structural, tracked in #1449). SQLite serializes and Mongo's driver is thread-safe, which is why exactly
those two pass.

Two operational facts the runs established, both previously unverified:

- **Each checkpoint matrix child gets its own deterministic persistence scope.** The scenario stamps every
  state with the adapter-selected scope and the checkpoint writer's `EnsureTenantScope` refuses any other
  ambient scope. `CheckpointCommitAdapter` derives that scope from the immutable cohort, measurement-set,
  process kind, and process index, so the warmup and three measured children can share one configured
  provider without colliding on their fixed checkpoint identities. Reusing the same process identity also
  recreates the same logical timestamps, making an interrupted child an equivalent replay instead of a
  fingerprint conflict. `RuntimeStoreComposition` registers the selected scope *before*
  `AddGroundworkV2RuntimeStores`, whose own `AddPersistenceCore()` call registers the default scope via
  `TryAddScoped` — first registration wins, so ordering is load-bearing.
- **Use a fresh database per attempt.** The executable's ArtifactId is fixed by the frozen scenario, and a
  failed run leaves rows (including the separate coordination row) that make the next run fail differently —
  which cost a misdiagnosis before it was spotted.

The adapter now compiles and is wired end to end, but no provider has executed the timed path yet. A
correctness or measurement process still requires a current-version staged native-plan document.

`VerifyCorrectnessAsync` calls `NativePlanEvidenceStaging.PublishInto`, which copies a native-plan evidence
document out of `ELSA_BENCH_NATIVE_PLAN_STAGING` and fails unless it hashes to the requested
`--native-plan-sha256`. For checkpoint-commit, `capture-plan` now produces this document after a live probe;
provider execution remains blocked until the operator runs that command against the current provider.

The good news for this particular workload: `checkpoint-commit` declares **no** required native routes, so
the document it needs carries an empty `Routes` list because the frozen contract declares no required native
routes. `capture-plan` records that fact as `RouteContract=no-native-routes-declared`, binds the live probe,
and writes the content digest the matrix request must carry. It does not claim a provider execution plan.

`probe-provider` now reads native server identity and sanitized driver settings. Both adapters consume that
observation during correctness, and `ValidateCorrectness` requires it to equal the request exactly; stale,
hand-edited, or cross-provider metadata therefore blocks the run.

For PostgreSQL and SQL Server, the native handshake proves the server product/version but cannot prove that
the endpoint is the intended container. The launcher must therefore independently inspect the container and
export its image digest as `ELSA_BENCH_POSTGRES_CONTAINER_ATTESTATION` or
`ELSA_BENCH_SQLSERVER_CONTAINER_ATTESTATION` (`sha256:<64-hex-digest>`) before probing. The probe binds that
attestation into the sanitized provider configuration as `container_image_digest`; the exact value must be
copied into the request and evidence. The projection also includes an `options_digest` of the complete
canonical connection options, so any setting not individually named cannot silently compare equal. An
arbitrary server handshake is consequently never sufficient to
claim the frozen `real-*-container` topology.

### The composition, for reference

The `checkpoint-commit` leaf must supply `RuntimeCheckpointCommitClient`, which is seven public runtime
contracts over one adapter-owned backing:

```
IRuntimeCheckpointCommitStore, IRuntimeExecutionOwnershipService, IWorkflowExecutableStore,
IWorkflowExecutionStateStore, IActivityExecutionStateStore, IDurableValueStateStore,
IRuntimePostCommitOutboxStore
```

On v2 the composition is materially simpler than v1's four-step ordering dance: `AddGroundworkV2RuntimeStores`
(`src/Elsa/Persistence/Groundwork/V2/Runtime/GroundworkV2RuntimeRegistration.cs`) registers the complete runtime
family, admits every unit in `ElsaRuntimeV2StorageManifest`, and binds a target. `AddWorkflowRuntime()` is still
needed for `IRuntimeExecutionOwnershipService`, which has no Groundwork replacement. A direct (non-shell) host
must also drive `GroundworkStorageSessionSource` — it admits units from `IHostedService.StartAsync` /
`IShellInitializer.InitializeAsync`, so a plain `BuildServiceProvider()` leaves nothing admitted.

**Answered, in `RuntimeStoreComposition`.**
`GroundworkStorageProviderConnectionRegistration.AddGroundworkStorageProviderConnection` takes an
already-created `IStorageProviderConnection` and binds it to a target — provider packages own construction
and lifetime, so the adapter opens the connection itself and hands the instance over. Two further things a
direct host gets wrong by default, both handled there: unit admission must be driven explicitly via
`IShellInitializer.InitializeAsync`, and the two independent clients must come from two DI scopes, because
the runtime stores are scoped registrations and the workload rejects clients that share an instance.

### Traps in the contract that its own documentation does not state

Verified by reading the harness, not inherited from the v1 host:

- **The two clients must genuinely differ.** `OpenIndependentClientsAsync` returns
  `RuntimeCheckpointCommitClients(Primary, Secondary)`, and the workload enforces distinctness itself —
  `RequireIndependentClients` / `IsDistinctClient` in `RuntimeCheckpointCommitWorkload.cs`. Returning the same
  composed client twice, or two clients sharing a scoped store instance, fails the workload rather than
  producing a wrong number.
- **`checkpoint-commit` is not admission-blocked.** `BenchmarkAdapterAdmission` blocks exactly one workload —
  `iam-normalized-lookup-update`, pending a separate ratification — so this leaf is free to run. Do not read
  the empty `RatifiedIamProductionMappings` set as a general adapter allowlist; it is IAM-specific.
- **The workload's admitted physical forms include a v2-shaped one.** `WorkloadCatalog` lists
  `shared-documents-with-linked-index-tables`, `document-type-specific-tables` and
  `checkpoint-unit-of-work-with-linked-outbox` for `checkpoint-commit`. The last is the one that describes the
  v2 unit-of-work substrate; the first two are v1 shapes. Pick deliberately, because the form is bound into
  the artifact's provenance and `compare` rejects a target whose form is outside the frozen workload.
- **Warmup invokes with negative ordinals.** `ProcessMeasurement.WarmAsync` calls
  `InvokePreparedAsync(operation, -1L - i, ...)`, so warmup ordinals run -1, -2, -3… Key any row identity off
  the ordinal so warmup and measured namespaces cannot collide; a leaf that derives keys from
  `Math.Abs(invocation)` will have warmup overwrite measured rows.
- **Check the correctness storage scope against the frozen scenario.** On v1 the scenario stamped
  `TenantId = "tenant-checkpoint"` on every committed state and the bridges enforced that scope, so correctness
  had to run in it. Whether the v2 stores impose the same requirement is **not verified here** — confirm it
  against `RuntimeCheckpointCommitWorkload`'s expectations before assuming either way.

## Operating it

Every operating constraint in the v1 host's README still applies and none of it is provider-specific: artifact
and staging directories outside the worktree, identical build configuration for harness and host, build once
then stage then run, run detached, and prefer a checkout you are not editing. They are not repeated here.

Before a checkpoint run, invoke `probe-provider` with the provider connection in its environment. It prints
the native server version, the admitted topology, and the sanitized `--provider-setting` values to use when
constructing the matrix request. Then invoke `capture-plan --request <request-json> --out <staging-directory>`
for each provider. This command performs the live probe again and emits
`checkpoint-commit.<provider>.<measurement-set>.native-plan.json`; its digest is the value for `--native-plan-sha256`. The
document's `RouteContract=no-native-routes-declared` is a provenance statement derived from the frozen
checkpoint workload, not a provider-plan capture or a performance result.

Two additions for v2:

- **Verify the resolved Groundwork version, never the pin.** A performance number measured against the wrong
  assembly is worse than no number, and the pins are spread across **five** independent mechanisms:

  1. central `PackageVersion` entries in `Directory.Packages.props`
  2. per-project `VersionOverride` on individual `PackageReference` elements
  3. `GroundworkVersion` properties in other project files
  4. nested `Directory.Packages.props` files (e.g. under `tests/Elsa3/Mapping`)
  5. `CurrentV2GroundworkVersion` in `tests/Elsa/Architecture/GroundworkCoverageLedgerTests.cs`

  Bumping only some of these leaves the rest resolving the old version, and `NU1603` is suppressed, so the
  restore says nothing. Check what was actually resolved:

  ```bash
  grep -o '"Groundwork.PostgreSql/[^"]*"' <project>/obj/project.assets.json | sort -u
  ```

  The fifth entry is both a pin and the **guard** against the other four: `GroundworkCoverageLedgerTests`
  asserts that every Groundwork `PackageReference`, `VersionOverride` and `GroundworkVersion` property in the
  repository equals `CurrentV2GroundworkVersion`. So drift is not silent in CI — it fails there — but the
  constant must be updated in the same change as the bump, or the guard reports the bump as the error. Worth
  knowing before a red architecture suite sends you looking in the wrong place.

- **Connection strings arrive by environment variable, not argv.** `ArtifactSafety` rejects any request string
  containing `://` or a host/port/database keyword, so a connection string cannot travel as a request field.
  See `ProviderConnections.ConnectionEnvironmentVariable`.
