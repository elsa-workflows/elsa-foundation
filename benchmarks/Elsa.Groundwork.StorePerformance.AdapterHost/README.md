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
| `describe-matrix` | **done** — emits the schema-v3 exact 13-workload/17-registration catalog with separate correctness, measurement, and timing-verdict states plus the AdapterHost and harness source revisions consumed by the operator runner |
| `describe-composition` | **done** — validates a request and emits the deterministic selected-composition descriptor and lowercase SHA-256 without provider I/O or schema mutation |
| `capture-plan` | **complete** for checkpoint, bookmark, recovery, outbox, trigger, recurring, due-timer, placement, and both Secret targets; relational queue/command are implemented and awaiting final Groundwork package plus live-plan validation; MongoDB queue/command are correctness-ready but native-plan blocked; diagnostics Groundwork capture now retains composite trace-detail evidence, while EF remains correctness-only |
| optional-observer `src/` seam on `GroundworkV2RuntimeCheckpointWriter` | **done**, compiles |
| `RuntimeStoreComposition` — DI composition, unit admission, distinct clients | **done**, compiles |
| `CheckpointCommitAdapter` — correctness half, over the production commit path | **done**, compiles |
| `RecoveryScanAdapter` — bounded public recovery paging and four native routes | **implemented**, focused workload tests pass; live provider capture remains operator-driven |
| `verify-correctness` command | **done** — separate from plan capture and timed execution |
| the five measured operations | **implemented** in the workload-owned phase adapter; no timed cohort claimed |
| current-version measured cohorts | **not yet retained**; the runner admits timing only for registrations with complete or explicitly routeless evidence, keeps correctness-ready/native-plan-blocked registrations out of timing, and checks for an idle host immediately before execution |

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

The write and read adapters in this host therefore use the same provider-native observer. Other
read-dominated workloads remain adapter leaves, rather than being blocked by an absent read seam.

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

The diagnostics adapter keeps every warmup and measured child on its own deterministic persistence scope.
Its standalone `verify-correctness` command retains the frozen 64-record OpenTelemetry write shape. During
`run`, only the untimed construction of that child's private fixture uses 1,000-record writes; it then runs
the same fifteen assertions and produces the same ratified result digest before any measured callback is
admitted. This is a setup optimization, not fixture reuse across processes and not a change to measured
operation shape.

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

For PostgreSQL, SQL Server, and MongoDB, the native handshake proves the server product/version but cannot
prove that the endpoint is the intended container. The launcher must therefore independently inspect the
container and export its image digest as `ELSA_BENCH_POSTGRES_CONTAINER_ATTESTATION`,
`ELSA_BENCH_SQLSERVER_CONTAINER_ATTESTATION`, or `ELSA_BENCH_MONGO_CONTAINER_ATTESTATION`
(`sha256:<64-hex-digest>`) before probing. The probe binds that attestation into the sanitized provider
configuration as `container_image_digest`; the exact value must be copied into the request and evidence.
The projection also includes an `options_digest` of the complete canonical connection options, so any
setting not individually named cannot silently compare equal. An arbitrary server handshake is
consequently never sufficient to claim a frozen real-container topology.

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

Start by reading the machine-owned status rather than copying a workload list from this README:

```bash
python3 tools/groundwork/run-e3-medium-baseline.py status
```

For the diagnostics v1.3 budget-derivation run, use the manual performance-evidence lane rather than a
developer desktop. The workflow starts one dedicated host per provider, keeps capture, correctness, and
timing serial within that host, and uploads the native-plan evidence, four process artifacts, manifest, and
ungraded `measurement.v1.json` result. It does not compare targets or apply a budget:

```bash
gh workflow run http-workflow-performance.yml \
  --ref <branch-containing-this-workflow> \
  -f suite=groundwork-diagnostics
```

The workflow file is already registered on the default branch, so a changed branch version can be dispatched
and reviewed before integration promotion. The four provider jobs run in parallel; each provider's warmup and
three measured processes remain on one provenance-bound host.

The runner exposes separate `capture`, `correctness`, `measure`, `compare`, and `gate` commands. Every one
is a dry run until `--execute` is supplied. It obtains workload version, adapter/form admission, provider
support, topology, seed, input fingerprint, capture status, and timing status from `describe-matrix`.
Consequently, adding an adapter without updating the registry cannot silently make the Python runner claim
support, and a routed workload without capture cannot fall through to checkpoint's zero-route document.
The runner also requires both Release binaries to carry the clean current HEAD in their generated assembly
metadata. A stale host or harness therefore fails before status, capture, correctness, measurement,
comparison, or gate execution. Direct host and harness commands canonicalize `--out` and refuse the
repository, its descendants, parent trees that contain it, and symlink aliases into it before any provider
access or artifact write. The direct commands also require a clean current build, the exact provider-package
names and central versions, and the canonical Release AdapterHost handshake; an alternate `--child-command`
cannot stand in for the registered host. Optional comparison and gate result files must remain under the
admitted output tree, including on blocked-report fallback paths.

Before provider access, `capture-plan`, `verify-correctness`, and `run` recompute the same composition descriptor used by
`describe-composition`. The Python runner calls that command during `target_context`; it supplies the resulting
digest in every request and treats a supplied `--composition` as an expected-value assertion. The descriptor
contains the selected Groundwork registry's target, unit ID/name, schema version, and `SchemaSubject.Fingerprint`,
plus explicit feature/schema identities, workload/version, adapter/form, provider/version/topology, sorted safe
provider settings, and package versions. EF comparators use explicit model identities and intentionally have no
Groundwork registry units. Connection strings, credentials, binaries, and incidental files are excluded.

The runner invokes `probe-provider` from the provider connection in its environment and binds the native
server version, admitted topology, and sanitized provider settings into the generated request. Its
`capture` phase then invokes `capture-plan` and emits
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
