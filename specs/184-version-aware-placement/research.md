# Research: Version-Aware Placement, Draining and Failover

Inventory behind [spec.md](./spec.md). Everything here was read from the tree on 2026-09-27, with PR #2109's specs 180
to 183 merged in. Paths are repository-relative. Sections are cited by name, not line.

## Placement and routing today

| Mechanism | Where | What it does |
|---|---|---|
| Placement lease | `src/essentials/Workflows/Runtime/Distributed/Models/ExecutionPlacementLease.cs` | `WorkflowExecutionId`, `OwnerId`, `PlacementToken` (strictly increasing per execution), `AcquiredAt`, `ExpiresAt`. The type's own remarks: routing only, never consulted at commit. |
| Placement store | `src/essentials/Workflows/Runtime/Distributed/Contracts/IExecutionPlacementStore.cs` | Replacement contract. `TryClaimAsync` grants when unplaced or expired, renews when the live lease is the claimant's, denies otherwise. `ReleaseAsync` releases only on a matching owner and token. `ListOwnedAsync` lists one owner's live leases. In-memory default (`Services/InMemoryExecutionPlacementStore.cs`) and EF store (`Persistence/EntityFrameworkCore/Stores/EfExecutionPlacementStore.cs`). |
| Placement service | `src/essentials/Workflows/Runtime/Distributed/Services/ExecutionPlacementService.cs` | Stamps claims with `NodeId` and `now + LeaseDuration`. |
| Placement options | `src/essentials/Workflows/Runtime/Distributed/Options/ExecutionPlacementOptions.cs` | `NodeId` default `node:{machine}:{pid}`; `LeaseDuration` 30 s, also the transport visibility window. |
| Actor provider | `src/essentials/Workflows/Runtime/Distributed/Services/DistributedWorkflowExecutionActorProvider.cs` | `GetAgentAsync` claims placement; owned → local in-process actor; otherwise `ForwardingWorkflowExecutionActor`. `PassivateAsync` releases the placement lease the node holds. |
| Forwarding actor | `src/essentials/Workflows/Runtime/Distributed/Services/ForwardingWorkflowExecutionActor.cs` | Appends the command to the durable transport and answers with the "accepted for routing" dispatch status, naming the owning node in metadata. |
| Placement pump | `src/essentials/Workflows/Runtime/Distributed/Services/ExecutionPlacementPumpTask.cs` | Every sweep: renews each lease `ListOwnedAsync` returns, through `TryClaimAsync`; then lists executions with visible transport backlog, claims each through `TryClaimAsync`, leases its items to `NodeId`, dispatches, and acknowledges delivered ones. Sweep 10 s; failure backoff up to 5 min (`BackoffSweepPumpTask`). |
| Transport | `src/essentials/Workflows/Runtime/Distributed/Contracts/IExecutionCommandTransport.cs` | `SendAsync`, `LeaseAsync(executionId, ownerId, now, leaseDuration, maxItems)`, `AckAsync` with the lease token, `ListPendingExecutionIdsAsync`, `CountPendingAsync`. No operation releases an item lease early. |
| Feature | `src/essentials/Workflows/Runtime/Distributed/WorkflowsRuntimeDistributedFeature.cs` | `NodeId` setting ("Defaults to a machine/process-derived value"), lease, sweep and batch settings; registers the in-memory defaults, the distributed provider and the pump. |

Findings:

- **Nothing asks whether the node can run the execution.** The claim decision is lease state alone.
- **Renewal can re-grant.** The pump renews through `TryClaimAsync`, which grants a released or expired lease to the
  caller. A lease another member released on the renewer's behalf would come straight back to it. The spec's FR-014
  needs a renew-only compare-and-set instead.
- **There is no early release of a transport item lease.** A leased item becomes visible only when its lease
  expires. Hand-off (FR-019) and reclaim (FR-024) need a holder- and token-matched release.
- **The actor activation request carries no requirement.** `WorkflowExecutionActorActivationRequest`
  (`src/essentials/Workflows/Runtime/Core/Models/WorkflowExecutionActorModels.cs`) has `RequiredCapabilities`, but
  that flag set describes actor-provider features (`InProcessMailbox`, `DistributedPlacement`, `LeaseFencing`, ...),
  not what an executable needs. The spec's placement requirement is a different thing and should not reuse it.
- **The pump's claim path has no pinned executable in hand.** It sees execution ids from
  `ListPendingExecutionIdsAsync`, then claims, then leases. FR-016 requires the requirement before the claim, so the
  plan must resolve the pin from durable state or from the pending envelope without leasing.

## Execution lease, fence and recovery

| Mechanism | Where | Notes |
|---|---|---|
| Ownership contract | `src/essentials/Workflows/Runtime/Core/Contracts/IRuntimeExecutionOwnershipService.cs` | Acquire, heartbeat, release, `EnsureCurrentAsync`. |
| Ownership service | `src/essentials/Workflows/Runtime/Services/Executions/RuntimeExecutionOwnershipService.cs` | `AcquireAsync` always writes a new lease with the highest token plus one, whatever lease is live: it takes over. |
| Ownership options | `src/essentials/Workflows/Runtime/Core/Models/RuntimeExecutionOwnershipOptions.cs` | `OwnerId` default `inproc:{machine}:{pid}`; `LeaseDuration` 1 min. Registered with `TryAddSingleton` in `src/essentials/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs`. |
| Fence | `src/essentials/Workflows/Runtime/Services/Executions/RuntimeExecutionFenceValidator.cs` | Refuses no lease, an expired lease, or a lease id, owner or token that differs from the stored one. |
| Recovery selection | `src/essentials/Workflows/Runtime/Services/Recovery/RuntimeRecoveryCandidateSelector.cs` | Candidate when the lease is due (the earlier of its expiry and acquisition plus the lease timeout) or the heartbeat is older than the heartbeat timeout. An optional owner filter exists on `RuntimeRecoveryScanRequest` (`src/essentials/Workflows/Runtime/Core/Models/RuntimeRecovery.cs`); nothing passes one. |
| Recovery sweep | `src/essentials/Workflows/Runtime/Services/Recovery/RuntimeResumptionService.cs`; feature `src/essentials/Workflows/Runtime/Resumption/WorkflowsRuntimeResumptionFeature.cs` | Lease and heartbeat timeouts 5 min by default; sweep 10 s. |

Findings:

- **Because acquisition takes over, reclaiming an execution lease means only making the execution a recovery
  candidate.** The re-drive's own `AcquireAsync` issues the greater token, and the fence refuses the old owner. That is
  why FR-025 can forbid reclaim from writing a lease or a token.
- **The recovery selector needs a new eligibility signal.** Its owner filter narrows a scan but does not make a
  lease eligible before it is due. FR-024's "a recovery candidate at once" needs a departed-owner input, supplied
  through a runtime-core contract implemented by the distributed leaf (FR-027).

## Identity

- `NodeId` (`node:{machine}:{pid}`) and `OwnerId` (`inproc:{machine}:{pid}`) are derived the same way with different
  prefixes, and nothing sets one from the other; spec 183's research records the same finding ("Existing liveness and
  identity mechanisms"). Both change at every restart, so today a restarted host's earlier leases can only expire.
- `WorkflowsRuntimeDistributedFeature.ConfigureServices` sets `ExecutionPlacementOptions.NodeId` from the `NodeId`
  setting when it is non-blank, and never touches `OwnerId`.
- Column limits: the placement lease's `OwnerId` and the transport item's `LeaseOwnerId` are 128 characters
  (`Persistence/EntityFrameworkCore/Configuration/ExecutionPlacementLeaseEntityConfiguration.cs`,
  `ExecutionCommandTransportItemEntityConfiguration.cs`); the runtime's `LeaseOwnerId` and `HeartbeatOwnerId`
  projections are wider (`src/essentials/Workflows/Runtime/Persistence/EntityFrameworkCore/Configuration/RuntimeOperationalStateEntityConfigurations.cs`).
  A host id within `DistributedRuntimeIdentityConstraints` (128 UTF-16 code units) fits all of them, so FR-001 needs no
  schema change. An incarnation-qualified identity would not fit the first two.
- Retiring the `NodeId` setting: CShells binds a key only when a property of that name exists, and a setter that
  throws is caught and logged rather than failing the shell. Spec 171 records both behaviours of the pinned CShells
  version under its "Post-migration" requirements, in the paragraph after FR-058 ("Slice 7 MUST keep `MigratePolicy`
  ..."), which is why FR-003 refuses at configuration time and keeps the property.
- Neither `src/apps/Elsa.Workbench/shells.baseline.json` nor `docker/compose/elsa-workbench.shells.json` sets `NodeId`;
  both compose `WorkflowsRuntimeDistributed` with the durable placement and transport features.

## Requirements

- `WorkflowExecutable` (`src/essentials/Workflows/Runtime/Core/Models/WorkflowExecutable.cs`) carries
  `RuntimeRequirements`, `StorageDriverRequirements` and its nodes; `RuntimeRequirementCheckSubject.FromExecutable`
  (`src/essentials/Workflows/Runtime/Core/Models/RuntimeRequirementCheckResult.cs`) projects them.
- `RuntimeRequirement` and `RuntimeStorageDriverRequirement` are in
  `src/essentials/Activities/Runtime/Core/Models/RuntimeActivityDescriptor.cs`: a consumer key with a schema version,
  and a driver key.
- `RuntimeRequirementChecker` (`src/essentials/Workflows/Runtime/Services/Executables/RuntimeRequirementChecker.cs`)
  reads three registries: every `IRuntimeActivityConsumerCapability` (consumer key and supported schema versions),
  `IRuntimeDurableValueStorageDriverRegistry` (`DriverKeys`), and `IWellKnownTypeRegistry` for the type alias each CLR
  node declares. It reports `Missing`, `UnsupportedSchema` or `MissingActivityType`.
- `WellKnownTypeRegistry.TryGetTypeOrDefault`
  (`src/essentials/Serialization/SystemText/Services/WellKnownTypeRegistry.cs`) is a registry lookup only, with no
  `Type.GetType` fallback. So the aliases the registry lists (`ListTypes` with `TryGetAlias`) are exactly the aliases
  the checker accepts, which is what lets FR-008 require the section and the checker to agree.
- Current consumers: `WorkflowArtifactReconciler`
  (`src/essentials/Workflows/Runtime/Reconciliation/Services/WorkflowArtifactReconciler.cs`) rejects an artifact with an
  unmet requirement, and Publishing's `RuntimeRequirementPreflight`
  (`src/essentials/Workflows/Publishing/Api/Services/RuntimeRequirementPreflight.cs`) uses the same checker.
- The pin: command payloads carry `PinnedExecutable` (`WorkflowExecutableIdentity`), for example
  `RuntimeStartActivityCommandPayload`, `RuntimeCheckpointCommandPayload` and `WorkflowExecutionStartCommandPayload`
  under `src/essentials/Workflows/Runtime/Core/Models`.

## Timings

| Setting | Default | Source |
|---|---|---|
| Placement lease | 30 s | `ExecutionPlacementOptions.LeaseDuration` |
| Placement sweep | 10 s, backoff to 5 min | `ExecutionPlacementPumpOptions` |
| Transport visibility | 30 s | the placement lease duration |
| Execution lease | 1 min | `RuntimeExecutionOwnershipOptions.LeaseDuration` |
| Recovery lease and heartbeat timeouts | 5 min | `WorkflowsRuntimeResumptionFeature` |
| Membership heartbeat, expiry, skew | 10 s, 30 s, 5 s | spec 183, FR-006 (Decisions, Q21) |

Observation: with these defaults, a crashed host that does not restart is judged expired after 35 s, while its
placement leases expire after at most 30 s. Departure-driven reclaim therefore gains nothing for placement leases in
that case; it gains for execution leases (one minute) and transport items, and it makes failover independent of lease
length, so a deployment can lengthen leases to cut renewal traffic. The restart case (User Story 3) and a graceful
stop (User Story 6) are where it gains most, because neither waits for any expiry.

## Attention

`WorkflowsRuntimeAttentionFeature` (`src/essentials/Workflows/Runtime/Attention/WorkflowsRuntimeAttentionFeature.cs`)
registers `WorkflowRuntimeAttentionContributor` over `IWorkflowRuntimeAttentionQuery`
(`WorkflowRuntimeAttentionContracts.cs`). FR-017's warning belongs there, beside the runtime's other attention items.

## Test precedents

- `tests/essentials/Workflows/Runtime/Distributed/Tests/TwoNodeAcceptanceTests.cs` and `NodeHarness.cs`: two node
  containers in one process over shared state and a controllable clock, including a node killed mid-drain whose late
  commit is fenced. User Stories 3 to 5 extend this shape; each node container then configures its own host id,
  because FR-001 takes the routing identity from membership.
- Spec 183's conformance kit (FR-044 to FR-060) is where FR-010 and FR-032 add their cases.

## Noticed and left alone

- `WorkflowExecutionActorActivationRequest.RequiredCapabilities` names actor-provider features and is always
  `None` in the pump. It is unrelated to placement requirements; reusing the name would confuse the two.
- Scheduler work claims and durable-timer claims carry owner ids too (`ClaimOwnerId`). They are not per-execution
  leases in this spec's sense and keep their own visibility timeouts.
