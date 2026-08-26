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
| `probe-provider` | **partial** — opens the connection; does not yet read back sanitized provider configuration |
| `capture-plan` | **not started** |
| `checkpoint-commit` adapter leaf | **not started** — see "What remains". Handed to another session; the optional-observer `src/` change on `GroundworkV2RuntimeCheckpointWriter` is approved and belongs with it |
| a measured cohort | **not run**, and not runnable on a loaded machine by construction |

Nothing here has produced a measurement. No number in this project has been measured on v2.

## The finding that shapes the rest of the work

`ProcessMeasurement.ExecuteAsync` refuses any measured process whose observer is missing or inexact:

```csharp
observer = adapter.RoundTripObserver;
if (observer is null || !observer.IsExact)
    throw new PerformanceContractException(... "adapter command counts or synthetic estimates are not admissible.");
```

Groundwork v2 exposes exactly two observer seams, and only one of them counts provider commands:

- `IWritePathObserver` (`Groundwork.Kernel`) — "one provider command observed while executing a write path".
  This is a true provider-native round-trip counter. It is threaded through `WriteOptions.Observer`, and the
  batched unit-of-work path honours it too, taking the observer from the first staged write of each chunk
  (`PostgreSqlStorageSession.ApplyUpsertBatch` raises one event per multi-row statement). `WritePathObserver`
  in `Groundwork.Store` already exposes a `RoundTrips` count in exactly this shape.
- `IStorageAccessObserver` (`Groundwork.Store`) — "one auditable use of **privileged** storage access". It is
  an audit hook, not a command counter, and it does not fire per provider command.

**There is no read-path observer.** `IStorageSession.Query(QueryRequest, QueryRenderOptions?)` takes no
observer, and no read-side observer interface exists (`grep "interface I.*Observer"` over `src/` in
groundwork-v2 returns exactly the two above).

Two consequences, and they set the order of the remaining work:

1. **Write-dominated workloads are measurable on v2 today.** `checkpoint-commit` is the right first leaf, for
   the same reason it was the right first leaf on v1.
2. **Read-dominated workloads are blocked upstream**, not merely unwritten. `runtime-bookmark-lookup`,
   `runtime-trigger-binding-stimulus-lookup`, `runtime-queue-drain`, `runtime-outbox-drain`,
   `runtime-recurring-schedule-selection` and `iam-normalized-lookup-update` cannot produce an admissible
   measured artifact until groundwork-v2 grows a read-path round-trip seam. That is a groundwork-v2 change,
   not an Elsa one, and it is filed as **valence-works/groundwork-v2#63**. #1425 is therefore two pieces of
   work: this write-dominated half, which is buildable now, and a read half that no amount of work in this
   repo unblocks.

   If that seam lands, note where to instrument it: each relational provider funnels essentially every
   command through one private factory — `PostgreSqlStorageSession.Command(string sql)` takes 32 of that
   session's 33 command creations. Instrumenting the chokepoint makes completeness a property of the design;
   instrumenting per call site means auditing ~44-51 sites per provider, where a miss silently undercounts and
   the harness cannot detect it.

A third point, and its resolution.

`GroundworkV2RuntimeCheckpointWriter` stages its writes with the static `WriteOptions.Unconditional` /
`WriteOptions.IfVersion(...)` singletons, whose `Observer` is null, so the production commit path could not be
instrumented from outside `src/`. Anything the leaf measured would have been a *reconstruction* of that path
from elementary store calls — a caveat that would have had to travel with every published number, because a
reader will otherwise assume the figure describes the path the product actually runs.

**Resolved: Sipke has approved giving the checkpoint writer an optional observer as a `src/` change.** The
leaf therefore measures the production path directly and the reconstruction caveat does not apply. Do not
reintroduce it by timing hand-rolled store calls where the public writer would do — that was a workaround for
a constraint that no longer exists.

The v1 host's separate reason for timing elementary calls still stands and is unrelated: a named phase of
1024 durable commits is hours at 100 invocations, so the *decomposition* is about run time, not about
instrumentation.

## What remains

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

The open question is how the adapter binds its chosen `IStorageProviderConnection` into that registration for a
given target; the v2 store tests construct stores directly rather than through DI, so the DI binding still has to
be established.

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
