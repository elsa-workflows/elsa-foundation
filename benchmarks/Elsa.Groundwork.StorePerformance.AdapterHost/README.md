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
| `checkpoint-commit` adapter leaf | **not started** — see "What remains" |
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
   not an Elsa one.

A third point worth recording, because it is easy to design the wrong thing around it:
`GroundworkV2RuntimeCheckpointWriter` stages its writes with the static `WriteOptions.Unconditional` /
`WriteOptions.IfVersion(...)` singletons, whose `Observer` is null. **The production commit path therefore
cannot be instrumented from outside `src/`.** The adapter must time the elementary store calls a named phase
is composed of, issuing them with its own observer-bearing `WriteOptions` — which is what the v1 host's README
already prescribed for a different reason (a phase of 1024 durable commits is hours at 100 invocations).

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

## Operating it

Every operating constraint in the v1 host's README still applies and none of it is provider-specific: artifact
and staging directories outside the worktree, identical build configuration for harness and host, build once
then stage then run, run detached, and prefer a checkout you are not editing. They are not repeated here.

Two additions for v2:

- **Verify the resolved Groundwork version, never the pin.** This repo pins Groundwork in four independent
  mechanisms (central `Directory.Packages.props`, per-project `VersionOverride`, `GroundworkVersion`
  properties, and nested `Directory.Packages.props` files), and `NU1603` is suppressed, so a stale resolve is
  silent. A performance number measured against the wrong assembly is worse than no number:

  ```bash
  grep -o '"Groundwork.PostgreSql/[^"]*"' <project>/obj/project.assets.json | sort -u
  ```

- **Connection strings arrive by environment variable, not argv.** `ArtifactSafety` rejects any request string
  containing `://` or a host/port/database keyword, so a connection string cannot travel as a request field.
  See `ProviderConnections.ConnectionEnvironmentVariable`.
