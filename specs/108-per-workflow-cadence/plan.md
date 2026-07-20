# Implementation Plan: Per-Workflow Checkpoint Cadence (ADR 0032 R5)

## Technical context

Base: main at PR #850 (`bc6bf7543`). The coalescing machinery (policy, session, fold, decorated stores, `WorkflowsRuntimeCheckpointPersistenceFeature`) is live; the drain orchestrator branches on the presence of `IRuntimeCoalescingDrainScopeFactory`; PR #850 added the host-projection cadence inspector with a documented per-run-stamp limitation. Note: this worktree's base predates spec-107 (`SideEffectProfile` on `ActivityContract`); this unit is independent of it and touches none of its surfaces, so the two merge cleanly.

## Design decisions

### D1 — Per-execution override seam: skip the session at the drain scope, not inside the policy/store

The coalescing machinery deactivates and re-creates sessions mid-drain (attempt boundaries, caps), and the store decorators key on the ambient session. Making the *policy* cadence-aware per execution would leave the session/queue-overlay machinery active while deciding Immediate per checkpoint — a fragile hybrid. The clean choke point is `WorkflowDrainOrchestrator.DrainCoreAsync`: when the host is Coalesced, resolve the run's effective cadence *before* `Begin(...)` and, for an Immediate resolution, take the exact Immediate path (live-drain delivery scope included). One decision per drain, no per-checkpoint machinery change, and the mandatory guardrail is untouched by construction.

### D2 — Resolver identity source: per-run stamp, else the envelope's artifact-id breadcrumb, else the state's pin

`RuntimeSchedulerDrainRequest` is id-only, but `DrainCoreAsync` holds the full `WorkflowExecutionCommandEnvelope`. On the first drain of a new run no `WorkflowExecutionState` row exists yet — but every dispatched command carries the pinned artifact id: `WorkflowStartDispatcher.CreateDispatchMetadata` (and the resume dispatchers) stamp `runtime.artifactId` into command metadata. Resolution order in `RuntimeCheckpointCadenceResolver.ResolveAsync`: (1) the run's own stamp on `WorkflowExecutionState.SystemMetadata` (authoritative for the run's lifetime, no executable load); (2) the pinned executable located via the envelope breadcrumb or the state's `PinnedExecutable`; (3) host default.

### D3 — Reachability matrix (honest)

| Authored \ Host | Immediate host (no coalescing services) | Coalesced host |
| --- | --- | --- |
| *(none)* | Immediate | Coalesced (host cap) |
| Immediate | Immediate | **Immediate** (session skipped per execution) |
| Coalesced (cap *n*) | **Immediate (clamped)** — the coalescing session, decorated stores, and policy are registered only by `AddCoalescingRuntimeCheckpointPersistence`; an Immediate host has none of them, so authored-Coalesced degrades to extra durability, never to a pretend-coalesced run | Coalesced (authored cap *n*; host cap when the author names none) |

Making authored-Coalesced reachable on an Immediate host would require the feature to always register the coalescing services and default per-workflow — a host-composition change deliberately left out of this unit (the feature's `Immediate` mode currently registers nothing, preserving the byte-for-byte default path).

### D4 — Stamp location: `SystemMetadata` on the started state change

`WorkflowExecutionState` is first constructed by `WorkflowCheckpointSchedulerWorkHandler.BuildWorkflowStartedStateChange`, which already loads the pinned executable for the root variable frame. The handler resolves the effective cadence from that executable (`IRuntimeCheckpointCadenceResolver.Resolve(executable)`) and stamps `runtime.checkpointCadence` (+cap). `PreserveSystemMetadata` carries the stamp across the completed/rebuild transitions, mirroring `InstanceName`. No schema change — `SystemMetadata` is the established extensible per-run bag.

### D5 — Cadence is behavioral hash content, written only when authored

Replay-safety travels with the artifact (R5), so the cadence feeds `WorkflowExecutableHasher`'s behavioral payload — but only when authored, keeping every existing artifact hash and characterization golden byte-identical.

### D6 — DI shape: optional resolver, constructor-overload discipline preserved

`IRuntimeCheckpointCadenceResolver` is a core-registered scoped service. The orchestrator takes it as an optional parameter on the full constructor only, so immediate-mode DI still selects the WU-2 overload and coalescing-mode DI picks up the resolver; a null resolver on a coalescing host coalesces with the host cap (pre-R5 behavior).

## Changed components

| File | Change |
| --- | --- |
| `src/Elsa/Workflows/Design/Core/Models/WorkflowCheckpointCadenceOptions.cs` | New authoring record (Mode alias + optional cap). |
| `src/Elsa/Workflows/Design/Core/Models/WorkflowStrategyOptions.cs` | `CheckpointCadence` property (CommitStrategyType idiom). |
| `src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutableCheckpointCadence.cs` | Compiled cadence on the artifact. |
| `src/Elsa/Workflows/Runtime/Core/Models/WorkflowExecutable.cs` | `CheckpointCadence` property + ctor param (default null; old JSON deserializes). |
| `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableCompiler.cs` | `CompileCheckpointCadence` (validate alias fail-fast, compile onto the executable). |
| `src/Elsa/Workflows/Publishing/Api/Services/WorkflowExecutableHasher.cs` | Conditional `checkpointCadence` object in the behavioral payload. |
| `src/Elsa/Workflows/Runtime/Core/Models/ResolvedCheckpointCadence.cs` | Effective-cadence value type. |
| `src/Elsa/Workflows/Runtime/Core/Contracts/IRuntimeCheckpointCadenceResolver.cs` + `Services/RuntimeCheckpointCadenceResolver.cs` | The per-execution resolver (D2/D3). |
| `src/Elsa/Workflows/Runtime/Services/WorkflowDrainOrchestrator.cs` | D1 seam: resolve before `Begin`, `DrainImmediateAsync` extraction, authored cap pass-through. |
| `src/Elsa/Workflows/Runtime/Contracts/IRuntimeCoalescingDrainScopeFactory.cs` + `Services/Coalescing/RuntimeCoalescingDrainScopeFactory.cs` | `Begin(workflowExecutionId, int? maxSegmentCheckpoints)` per-run cap override. |
| `src/Elsa/Workflows/Runtime/Core/Constants/RuntimeMetadataKeys.cs` | `CheckpointCadence` / `CheckpointMaxSegmentCheckpoints` stamp keys. |
| `src/Elsa/Workflows/Runtime/Services/WorkflowCheckpointSchedulerWorkHandler.cs` | D4 stamp at workflow-started + carry-forward. |
| `src/Elsa/Workflows/Runtime/Api/Coalescing/RuntimeCheckpointCadenceInspector.cs` | Prefer the per-run stamp; limitation note replaced. |
| `src/Elsa/Workflows/Runtime/Api/Handlers/GetWorkflowInstanceRequestHandler.cs` | Pass the instance state to the inspector. |
| `src/Elsa/Workflows/Runtime/Extensions/RuntimeCoreServiceCollectionExtensions.cs` | Resolver registration. |

## Test strategy

Design round-trip (`CheckpointCadenceDraftRoundTripTests`), publish compilation + hash significance + fail-fast validation (`WorkflowExecutableCompilerTests`), resolver unit matrix (`RuntimeCheckpointCadenceResolverTests`), end-to-end drain-path matrix + per-run stamp + mandatory guardrail (`RuntimeCheckpointCoalescingTests`), instance-view stamp preference (`WorkflowInstancesRequestHandlerTests`). Full projects: Elsa.Workflows.Runtime.Tests, Elsa.Workflows.Runtime.Api.Tests, Elsa.Workflows.Design.Tests, Elsa.Workflows.Design.Api.Tests, Elsa.Workflows.Publishing.Api.Tests, Elsa.Persistence.Groundwork.Tests.
