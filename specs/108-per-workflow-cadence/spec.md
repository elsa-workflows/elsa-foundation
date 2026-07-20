# Feature Specification: Per-Workflow Checkpoint Cadence Authoring + Per-Run Cadence Stamp

**Feature Branch**: `worktree-agent-af7227eeb0862453e`

**Created**: 2026-07-20

**Status**: Implemented

**Input**: WU-config-surface of [ADR 0032](../../docs/adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) (accepted; this unit implements the R5 resolution), under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket. Cadence is authored on the workflow via `WorkflowStrategyOptions`, flows design → publish → executable, and the runtime resolves it per execution with the precedence: mandatory-boundary set (never relaxable) > per-workflow authored cadence > host default. This also upgrades PR #850's documented limitation: the instance read model now reports the cadence a run *actually executed under* via a per-run stamp, not the host's currently-configured cadence.

## Context

The host-level cadence default already exists: `WorkflowsRuntimeCheckpointPersistenceFeature` (shells.json `Mode` = `Immediate` | `Coalesced`, `MaxSegmentCheckpoints`) selects the checkpoint-persistence policy at DI time via `AddCoalescingRuntimeCheckpointPersistence`, and `WorkflowDrainOrchestrator.DrainCoreAsync` branches on whether the coalescing drain-scope factory is registered. That makes cadence host-global: every workflow on a Coalesced host coalesces, and the instance read model (PR #850's `RuntimeCheckpointCadenceInspector`) can only report the host's *current* configuration — an instance that ran before a host reconfiguration is reported under the wrong cadence, a limitation the inspector's own XML doc declared.

ADR 0032 R5 ratified the fix: the per-workflow carrier is `WorkflowStrategyOptions` (which already carries `CommitStrategyType` as a stable string alias — the idiom this unit follows), compiled into the pinned executable so replay-safety travels with the versioned artifact. A runtime-side per-definition store as primary carrier was REJECTED. Per §E2.2 the runtime must not read Design stores; it reads the pinned executable only.

## Scope boundary

- **In scope**: the authoring shape on `WorkflowStrategyOptions` (`CheckpointCadenceOptions`: `Mode` alias + optional `MaxSegmentCheckpoints`); publish-time validation and compilation onto `WorkflowExecutable` (behavioral-hash-significant); the per-execution runtime resolver and the drain-orchestrator seam that skips the coalescing session for authored-Immediate runs; the per-run effective-cadence stamp on `WorkflowExecutionState.SystemMetadata`; the inspector/instance-view upgrade to prefer the stamp.
- **Out of scope (preserved unchanged)**: the mandatory-boundary set and its commit-layer guardrail (`MandatoryFlushCheckpointNames`, `IsMandatoryCheckpoint`); the coalescing session/fold/store-decorator machinery; the WU-marker side-effect-profile unit (R1/R2, spec 107 on main — this worktree's base predates it, so no `ActivityContract` surfaces are touched); per-section (sub-graph) cadence, which ADR 0032 anticipates but R5 does not require.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A workflow authors its own cadence and it survives design → publish → executable (Priority: P1)

An author sets `strategyOptions.checkpointCadence` (`mode: "Immediate" | "Coalesced"`, optional `maxSegmentCheckpoints`) on the draft state. Draft replace persists it; promotion carries it onto the immutable version; the executable compiler validates the alias fail-fast at publish and compiles it into the pinned artifact, where it participates in the behavioral content hash (a cadence change is a distinct artifact identity).

**Why this priority**: the artifact is the only carrier the runtime is allowed to read (§E2.2/§E2.6); without faithful transport the rest of the unit has no source of truth.

### User Story 2 - An authored-Immediate workflow runs Immediate on a Coalesced host (Priority: P1)

A durability-critical workflow authored `Immediate` executes with per-checkpoint durable commits even when the host default is `Coalesced`: the drain orchestrator resolves the effective cadence per execution and skips establishing the coalescing session for that run. An authored-`Coalesced` cap overrides the host cap for that run's sessions.

**Why this priority**: this is the R5 precedence rule made real — per-workflow authored cadence over host default.

### User Story 3 - The instance view reports the cadence the run actually executed under (Priority: P2)

When a run starts, the runtime stamps the resolved effective cadence (`runtime.checkpointCadence`, plus `runtime.checkpointMaxSegmentCheckpoints` when coalesced) into the run's durable `SystemMetadata`. The instance read model prefers this stamp; reconfiguring the host default no longer retroactively changes what an older instance reports. Unstamped legacy instances fall back to the host projection (PR #850 behavior).

### User Story 4 - Mandatory checkpoints flush regardless of any authored cadence (Priority: P1)

No authored cadence can relax a mandatory boundary: a bookmark-suspend, terminal, incident, or attempt-claim boundary flushes durably inside the segment exactly as before, even under the most relaxed authored `Coalesced` cadence.

## Requirements *(mandatory)*

- **FR-001**: `WorkflowStrategyOptions` MUST carry an optional `CheckpointCadence` selection (`WorkflowCheckpointCadenceOptions`: string alias `Mode` ∈ {`Immediate`, `Coalesced`}, optional positive `MaxSegmentCheckpoints`), following the `CommitStrategyType` stable-alias idiom. Null/blank mode = the workflow authors no cadence.
- **FR-002**: The full-state draft replace and promotion paths MUST round-trip the authored cadence byte-faithfully (it rides the existing `WorkflowDefinitionState.StrategyOptions` serialization; no new persistence surface).
- **FR-003**: `WorkflowExecutableCompiler` MUST validate the alias at publish (unrecognised mode or non-positive cap fails compilation with a typed `WorkflowExecutableCompilationException`) and compile the authored cadence onto `WorkflowExecutable.CheckpointCadence` (`WorkflowExecutableCheckpointCadence`).
- **FR-004**: The authored cadence MUST be part of the behavioral content hash (`WorkflowExecutableHasher`), written only when authored so unauthored workflows hash byte-identically to before this field existed (existing artifact ids and goldens stable).
- **FR-005**: The runtime MUST resolve the effective cadence per execution with precedence: per-run stamp > per-workflow authored cadence (read off the pinned executable via `IWorkflowExecutableStore`) > host default. The runtime MUST NOT read any Design store (§E2.2).
- **FR-006**: On a Coalesced host, an authored-Immediate run MUST execute without a coalescing session (per-checkpoint commits). On an Immediate host, an authored-Coalesced cadence clamps to Immediate (see the reachability matrix in [plan.md](plan.md)) — the coalescing services are simply not registered, and the resolver must not pretend otherwise.
- **FR-007**: An authored `MaxSegmentCheckpoints` MUST override the host cap for that run's coalescing sessions without mutating the host options singleton.
- **FR-008**: The workflow-started checkpoint MUST stamp the resolved effective cadence into `WorkflowExecutionState.SystemMetadata` (`runtime.checkpointCadence`, `runtime.checkpointMaxSegmentCheckpoints`), and every later state rebuild MUST carry the stamp forward (the `PreserveSystemMetadata` path).
- **FR-009**: `RuntimeCheckpointCadenceInspector` MUST prefer the per-run stamp and fall back to the host projection for instances predating it; its XML-doc limitation note is updated accordingly. The instance response schema is unchanged in shape (same `checkpointCadence` / `maxSegmentCheckpoints` / `inspectionGranularity` properties, now per-run-accurate), so the spec-092 OpenAPI contract needs no change.
- **FR-010**: The mandatory-boundary guardrail is untouched: no code path introduced by this unit may run between `IsMandatoryCheckpoint` and the commit, and the cadence resolver has no influence on per-checkpoint persistence decisions — it only selects whether a session exists for the drain.

## Invariants that MUST survive

- Mandatory checkpoints (`MandatoryFlushCheckpointNames` + `CheckpointRequirement = Mandatory` metadata) always flush durably, under every authored/host cadence combination.
- A host with no resolver registered (or an envelope with no resolvable identity) behaves byte-identically to pre-R5: host default decides, coalescing with the host cap.
- The crash-safety contract of coalescing (durable queue advanced only after the folded commit lands) is unchanged — this unit never touches the session/fold path, only whether a session is begun.
- Executables persisted before this unit deserialize with `CheckpointCadence = null` (constructor default) and resolve to the host default.

## Success Criteria *(mandatory)*

- **SC-001**: Design round-trip — authored cadence survives draft replace and promotion (typed state read-back).
- **SC-002**: Publish compilation — cadence lands on the executable, hash-distinct per mode, invalid aliases fail publication.
- **SC-003**: Runtime resolution matrix — authored-Immediate under a Coalesced host performs the Immediate-host commit count; authored-none uses the host default (single folded commit); the resolver unit matrix covers stamp-preference, authored-cap override, and the Immediate-host clamp.
- **SC-004**: Per-run stamp — the instance view reports the stamped cadence, not current host config, after a simulated host reconfiguration; legacy unstamped instances fall back to the host projection.
- **SC-005**: Guardrail — a mandatory bookmark boundary flushes durably under an authored relaxed cadence.
