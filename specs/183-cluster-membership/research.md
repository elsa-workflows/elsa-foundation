# Research: Cluster Membership

Inventory behind [spec.md](./spec.md). Everything here was read from `origin/main` at `0232aaf87` on 2026-09-27.
Paths are repository-relative. Sections are cited by name, not line.

## Existing liveness and identity mechanisms

The question the spec had to answer first: is there already a notion of live hosts, heartbeats or node registration
that membership must build on? There are heartbeats and leases, but every one of them is keyed by an execution or a
work item. None is keyed by a host.

| Mechanism | Unit | Holder identity | Clock | Where |
|---|---|---|---|---|
| Execution lease, heartbeat and fencing token | one workflow execution | `RuntimeExecutionOwnershipOptions.OwnerId`, default `inproc:{machine}:{pid}`; lease 1 minute | acting host's `TimeProvider` | `src/essentials/Workflows/Runtime/Core/Contracts/IRuntimeExecutionOwnershipService.cs`; `src/essentials/Workflows/Runtime/Services/Executions/RuntimeExecutionOwnershipService.cs`; `src/essentials/Workflows/Runtime/Core/Models/ExecutionLivenessState.cs` (`RuntimeExecutionLease`, `RuntimeHeartbeat`, `RuntimeExecutionFence`); `src/essentials/Workflows/Runtime/Core/Models/RuntimeExecutionOwnershipOptions.cs` |
| Commit fence | one checkpoint commit | lease id, owner id and token must equal the stored lease; expired or released leases are refused | committing host's clock, for the expiry check only | `src/essentials/Workflows/Runtime/Services/Executions/RuntimeExecutionFenceValidator.cs`; `src/essentials/Workflows/Runtime/Core/Exceptions/RuntimeStaleFencingTokenException.cs` |
| Recovery scan | interrupted executions | optional owner filter over lease and heartbeat owner ids | request's `Now`; lease and heartbeat timeouts default to 5 minutes | `src/essentials/Workflows/Runtime/Core/Models/RuntimeRecovery.cs`; `src/essentials/Workflows/Runtime/Core/Models/RuntimeResumption.cs`; `src/essentials/Workflows/Runtime/Services/Recovery/RuntimeRecoveryCandidateSelector.cs` |
| Placement lease | one workflow execution | `ExecutionPlacementOptions.NodeId`, default `node:{machine}:{pid}`; `PlacementToken` strictly increasing; lease 30 seconds | claimant's `TimeProvider`; the store judges expiry at the caller-supplied `now` | `src/essentials/Workflows/Runtime/Distributed/Contracts/IExecutionPlacementStore.cs`; `.../Contracts/IExecutionPlacementService.cs`; `.../Models/ExecutionPlacementLease.cs`; `.../Options/ExecutionPlacementOptions.cs`; EF store `.../Persistence/EntityFrameworkCore/Stores/EfExecutionPlacementStore.cs` |
| Placement pump | this node's leases and inbox | `NodeId` | `TimeProvider`; sweep 10 seconds, failure backoff up to 5 minutes | `src/essentials/Workflows/Runtime/Distributed/Services/ExecutionPlacementPumpTask.cs` |
| Transport visibility lease | one command item | `NodeId` as lease holder, with a lease token | caller-supplied `now` | `src/essentials/Workflows/Runtime/Distributed/Contracts/IExecutionCommandTransport.cs` (`LeaseAsync`, `AckAsync`) |
| Scheduler work claim | one scheduler work item | claim owner | `VisibilityTimeout` 1 minute; absolute `MaxDispatchDuration` 30 minutes | `src/essentials/Workflows/Runtime/Core/Models/RuntimeSchedulerWorkClaim.cs` |
| `[SingleNodeTask]` | one task type | whoever takes a distributed lock | lock provider | `src/essentials/Tasks/Services/TaskExecutor.cs`; `src/essentials/Locking/Core/IDistributedLockProvider.cs`; default `src/essentials/Locking/FileSystem` (machine-local) |
| Host health probes | the process | none | none | `src/apps/Elsa.Foundation.Host/Health/HealthEndpoints.cs` (`/health/live`, `/health/ready`) |

Findings:

- **Nothing is per host.** No table, store or service lists the hosts of a fleet, their status, restarts or what they
  can read. ADR 0078's Context says the same.
- **Two unreconciled per-process identities.** The placement `NodeId` and the execution `OwnerId` are derived the same
  way but with different prefixes, and nothing in the tree sets one from the other. `WorkflowsRuntimeDistributedFeature`
  configures `ExecutionPlacementOptions.NodeId` only. Both include the process id, so a restarted host gets new
  identities rather than a new incarnation of the same one.
- **Every lease uses host clocks.** All stamps and expiry checks go through the acting host's `TimeProvider`, which is
  what lets `TwoNodeAcceptanceTests` drive a fake clock. No store stamps or compares with a database clock. The
  placement store compares a lease stamped by one host with another host's `now`, so cross-host clock agreement is
  already assumed, but only for routing. The commit fence is decided by token equality, and its expiry check compares
  a lease with the clock of the host that holds it. No document in `src/` or `docs/adr/` states a clock-skew
  assumption.
- **Liveness is split by design.** The distributed provider's documentation, `DistributedWorkflowExecutionActorProvider`
  and `src/essentials/Workflows/Runtime/Distributed/README.md` ("Placement is routing; fencing is safety"), keeps
  placement best-effort and puts safety in the fence. Membership inherits that split: it serves routing and counting,
  and is never consulted at the commit (spec, FR-035).

How the spec builds on each:

| Mechanism | Membership's relation |
|---|---|
| Execution lease and fence | Unchanged. Invariant 2. |
| Placement lease | Unchanged. B7 makes the claim decision a member query and takes `NodeId` from the host id. |
| Transport visibility lease | Unchanged. Invariant 4: the durable queue stays the record. |
| Scheduler claim and dispatch deadline | Unchanged. They cover hung work, which membership does not measure. |
| `DistributedRuntimeIdentityConstraints` | Its limits (128 UTF-16 code units, well-formed, ordinal) are repeated for the host id, because a foundation contract cannot reference the runtime leaf. |
| `TimeProvider` everywhere | Kept. The skew allowance makes the existing implicit assumption explicit. |
| `[SingleNodeTask]` and health probes | Unrelated; neither is a registry. |

## Registration precedents

- **Replacing the actor provider.** `WorkflowsRuntimeDistributedFeature.ConfigureServices` registers the in-process
  provider as a concrete type and then calls `services.Replace` for `IWorkflowExecutionActorProvider`. The runtime's
  composition root (`src/essentials/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`)
  registers the in-process default with `TryAdd`.
- **Order-independent ownership of a replacement contract.** `ExecutionPlacementStoreBackend`
  (`src/essentials/Workflows/Runtime/Distributed/Contracts/ExecutionPlacementStoreBackend.cs`) records which backend
  owns `IExecutionPlacementStore` and re-checks exclusive ownership with an options validator run at startup
  (`ValidateOnStart`). It is the precedent for FR-001's "fail at startup, whatever the order".
- **Replacement-contract marker.** There is no shared marker attribute. Each domain declares its own, for example
  `ExecutionPlacementStoreReplacementContractAttribute`; `Elsa.Foundation.Identity.Core` has a
  `ReplacementContractAttribute` of its own.
- **Host-container composition.** `AddEfPendingMigrationActivationGuard`
  (`src/essentials/Modularity/EntityFramework/Extensions/ModularityEntityFrameworkServiceCollectionExtensions.cs`) is
  composed on the host container, not a shell, because feature management is host-level (ADR 0076, D9). Workbench
  (`src/apps/Elsa.Workbench/Program.cs`) calls it before `AddCShellsAspNetCore`. This is the precedent for Open
  Question 3.

## Where the in-process default can live

`Core_projects_contain_no_implementation_shaped_types`
(`tests/essentials/Architecture/ArchitectureGuardTests.CoreImplementationShape.cs`) fails the build on any non-abstract,
non-record class in a `.Core` project that implements a service interface or takes injected services. Its baseline is
a ratchet: entries may only be removed. So the in-process provider cannot sit in the contract package. Framework
constitution §2.1 gives it a home: a Layer 2 helper library holds "lightweight default implementations", is never
referenced by Layer 1, and Layer 3 implementations may reference it. A consumer's implementation (spec 181's gate)
references it and registers the default with `TryAdd`, so a host composes nothing to get it (spec, FR-018).

Proposed packages, for the plan to confirm; the names are not part of the spec:

| Package | Layer | Holds |
|---|---|---|
| `Elsa.Cluster.Core` | 1 | the contract, models, the query vocabulary, events; spec 182's shared finalization check lives here too (spec 182, Decisions, Q16) |
| `Elsa.Cluster.InProcess` | 2 | the in-process default and its `TryAdd` registration |
| `Elsa.Cluster.EntityFrameworkCore` | 3 | the EF provider, its feature, `[EfModule]`, contexts and migrations |
| `Elsa.Cluster.Conformance` (test support) | tests | the provider-neutral kit of FR-038 to FR-041 |

The readability report source (B3) reads spec 180's family declarations, which live beside `[EfModule]` in the
persistence layer, so it belongs with the code that reads them, not in the cluster packages. It references
`Elsa.Cluster.Core`, which §2.1 allows.

The existing guards that would cover the new projects: `Core_projects_do_not_reference_heavy_packages` and
`Core_projects_do_not_reference_implementation_projects` (`tests/essentials/Architecture/ArchitectureGuardTests.cs`) for
FR-002, and `EfCoreDependencyGuardTests` (`tests/essentials/Architecture/EfCoreDependencyGuardTests.cs`), which must
admit the EF provider's project and its reviewed EF closure, as it does for Runtime placement.

## B3: what "loaded" means

`LoadedEfModuleAssemblySource` (`src/essentials/Modularity/EntityFramework/IEfModuleAssemblySource.cs`) enumerates
every assembly in every `AssemblyLoadContext`, read on every evaluation rather than cached, because a Nuplane package
graph loads into a context of its own and a reconcile can load a module between two calls. The readability source uses
the same enumeration. Two consequences the spec relies on:

- An old shell generation's collectible context stays in the enumeration until it is unloaded, so FR-021's
  intersection keeps the report narrow until the old readers are really gone, and FR-022 keeps a removed family until
  its last declaration unloads.
- An assembly in the default context is never unloaded. A host that bundles version 1 and receives version 2 through
  Nuplane reports the intersection until it restarts. That is conservative, and spec 181's status names the host.

**Amended 2026-09-29** (spec, Decisions; FR-021). The first bullet did not hold for Nuplane's host-integrated packages,
and neither bullet named them. Nuplane loads each host-integrated package graph into a
`HostIntegratedPackageGraphLoadContext` that is not collectible, and its reconcile skips non-collectible contexts when
it unloads a superseded package (nuplane `src/Nuplane.Loading/PackageLoader.cs`, `UnloadUnreferencedContexts`, at the
commit `0.0.11-preview.93` was packed from). An upgraded module's previous release therefore stayed in the enumeration
for the life of the process: the report intersected [1] with [1, 2] until a restart, the new version never finalized,
and the activation guard's `EfModuleCatalog` found the module declared twice. An upgrade reloads only the packages that
changed: the new release loads into a graph of its own and binds to the dependencies still published from the old
graph, so the old graph's context keeps serving current assemblies and cannot be dropped as a whole.

Both enumerations now subtract what the host's `ISupersededAssemblySource` (`Elsa.Persistence.Schema`) names, and both
hosts compose one through `AddEfSchemaReadability`: `NuplanePackageGenerations` (`src/essentials/Cluster/Readability`).
It names an assembly *replaced* when it is in a load context whose type `Nuplane.Loading` defines, and Nuplane's
`IPackageAssemblyCatalog` lists a loaded assembly of the same name for the active package set but not this one; and
*retired* when it is replaced and no CShells shell generation that has not been disposed composes a feature from a
replaced assembly in the same load context. The readability report subtracts the retired set, so both generations are
intersected until the last shell generation running the old one is disposed, and the host publishes again at that
disposal, since nothing else republishes a report whose declarations did not change. The guard subtracts the replaced
set, because the generation an apply builds composes the active package set.

Two alternatives were weighed. Nuplane's active set alone drops the old generation before the shell generation running
it has drained, and drops a family entirely while its replacement is still loading or failed to load, because the
catalog then lists neither generation; that is the direction that credits a version a live reader cannot read. The
finalization gates are per shell container and registered only after admission, so they cannot speak for a generation
about to publish, nor for a family no shell enables. Nuplane exposing which contexts are current would not remove the
need for the CShells half, so no upstream change was needed.

The same stale generation reached the new release's migrations. `EfModuleBinding` named its migrations assembly, and EF
Core's `MigrationsAssembly` resolves a name with `Assembly.Load` from its own load context before it looks at an
assembly object (Microsoft.EntityFrameworkCore.Relational 10.0.10, `Migrations.Internal.MigrationsAssembly`'s
constructor). EF Core is loaded in the previous release's graph context, so the name reached the previous release, whose
migrations carry `[DbContext]` for the previous release's context type and matched nothing: under `Validate` a reload
onto a release with an unapplied migration activated over the unmigrated database. `EfModuleBinding`, the activation
guard's context and the persistence tool's now pass the module's assembly itself (`MigrationsAssembly(Assembly)`, which
EF Core reads only when no name is set), so no module's migrations are resolved by name across load contexts.

Shells are activated lazily on `Elsa.Foundation.Host` unless `Elsa:Boot:EagerShellActivation:Enabled` is set
(`src/apps/Elsa.Foundation.Host/Shells/EagerShellActivationHostedService.cs`), and reloaded after a Nuplane reconcile
by `ShellReloadOnPackagesChanged`. `Elsa.Workbench` registers `NullShellReloader`, so a new package version there
takes effect only after a restart.

## B2: the anatomy of an EF module to follow

`Workflows.Runtime.Distributed.Placement` is the closest template: a small coordination table with compare-and-set
writes on four engines.

- Declaration: `[assembly: EfModule(...)]` in
  `src/essentials/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/AssemblyInfo.cs`, with a sibling
  `[ManifestExtension("efModules", ...)]`.
- Names: `ExecutionPlacementEfModule` (history module name, table name); history table from
  `EfMigrationsHistory.TableName` (`src/essentials/Persistence/EntityFramework/EfMigrationsHistory.cs`).
- Provider contexts for SQLite, SQL Server, PostgreSQL and MySQL (`ExecutionPlacementProviderContexts.cs`), and one
  migrations folder per engine under `Migrations/ExecutionPlacement/`.
- Feature: `DistributedRuntimeExecutionPlacementEntityFrameworkCoreFeature`, with `Provider`, `ConnectionString`,
  `ConnectionName`, `Schema` and `Pooling` settings, `[UsesEfModule]`, and `AddEfModuleMigrations<TContext>(Provider)`.
- Writes: compare-and-set on an explicit revision through `EfDistributedCompareAndSwap` and `EfWriteRetry`
  (`src/essentials/Persistence/EntityFramework/EfWriteRetry.cs`), with no provider SQL. `EfExecutionPlacementStore`
  also settles a write that committed but was reported as lost, instead of replaying it.
- Ordinal comparison of identifiers: the placement store keys each row by a provider-neutral digest
  (`EfDistributedIdentity`) rather than comparing the identifier column. `EfOrdinalCollation`, in the persistence
  README's "Ordinal string columns", is the alternative for a column that is compared or ordered directly.
- Connections: `EfConnectionDefaults.ResolveConnectionString`. The persistence README's "Shared persistence resources"
  says distributed stores do not inherit a shared resource automatically.
- Tests: `tests/essentials/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/Tests` on SQLite, and
  `.../ProviderTests` (placement and transport smoke tests) for the container engines.

A possible entry shape, for the plan to confirm: host id, incarnation, status, joined at, heartbeat at, expiry period,
displaced flag, report (JSON) and report revision, row revision, and a schema version stamp. Primary key: host id and
incarnation. One index on host id for displacement at join.

## Heartbeat loop

`BackoffSweepPumpTask` (`src/essentials/Tasks/Schedules/BackoffSweepPumpTask.cs`) widens its interval geometrically on
consecutive failures, up to `MaxBackoffInterval`. The placement pump's ceiling is 5 minutes, ten times its lease. A
heartbeat built on it unchanged would stop renewing long before it noticed, which is why FR-027 forbids widening past
the expiry period. The EF provider can still run as an `IRecurringTask`, as `WorkflowsRuntimeDistributed` does with
`DependsOn = "Tasks"`, or as its own hosted service; the in-process default runs neither.

## Clocks

- .NET's `Stopwatch` uses `CLOCK_MONOTONIC` on Linux, which does not advance while the system is suspended. A VM that
  is paused and resumed can therefore measure less elapsed time than really passed. FR-007 takes the larger of
  wall-clock and monotonic elapsed time: the wall clock catches a suspend once NTP corrects it, and the monotonic clock
  catches a wall clock stepped backwards.
- FR-008's ordering holds because the member stamps its heartbeat time from its own clock at the start of the
  heartbeat and measures its lapse from that same start, so the only difference between its lapse and a reader's
  expiry verdict is the offset between the two clocks, which the allowance covers.
- A database clock was considered and not chosen: no store in the tree uses one, EF Core offers no provider-neutral
  way to stamp or compare with it without provider SQL, and it would take away the fake clock every distributed test
  relies on.

## Test precedents for the conformance kit

- `tests/essentials/Workflows/Runtime/Distributed/Tests/TwoNodeAcceptanceTests.cs` and `NodeHarness.cs`: two node
  containers in one process over shared in-memory state and a fake clock, including a node killed mid-drain whose late
  commit is fenced. That is invariant 2 and invariant 4 today, and the shape tier 2 of the kit generalizes.
- `tests/essentials/Workflows/Runtime/Distributed/Tests/DistributedWorkflowExecutionActorProviderTests.cs` and
  `WorkflowsRuntimeDistributedFeatureTests.cs`: provider replacement and registration.
- Container legs, one `ProviderTests` project per EF module, for example
  `tests/essentials/Workflows/Runtime/Distributed/Persistence/EntityFrameworkCore/ProviderTests`.

## Noticed and left alone

- `src/essentials/Workflows/Runtime/Distributed/README.md`, "In-memory defaults and durable persistence stores": its
  first sentence says `WorkflowsRuntimeDistributedEntityFrameworkCorePersistence` replaces both placement and
  transport; the next sentence, and the feature's own description, say it replaces placement only.
- Failover that reclaims a lapsed member's leases at once (ADR 0078, "Draining and failover"; Consequences) belongs
  to B7 (#2103); the owner confirmed this on #2093 (2026-09-27, spec.md Decisions and Out of Scope).
