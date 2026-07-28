# Feature Specification: Author-Declared Side-Effect Profile Gates the Pre-Activation Claim Boundary

**Feature Branch**: `worktree-agent-a42c393bebf532809`

**Created**: 2026-07-20

**Status**: Draft

**Input**: WU-marker of the runtime engine-performance effort under the [Runtime Execution Seam](../../docs/program-goals/runtime-execution-seam.md) bucket. Implements [ADR 0032](../../docs/adr/0032-runtime-checkpoint-cadence-is-policy-driven-per-workflow.md) decisions R1 + R2 (ratified 2026-07-20). Adds an author-declared `SideEffectProfile` to the pinned activity contract and makes the pre-activation attempt-claim checkpoint's mandatory immediate flush conditional on that profile, so a hot loop of pure activities can drop below one durable commit per activity under a coalescing cadence.

## Context

The measured coalesced commit floor for a workflow is ≈ (CLR activity count + terminal). Under the coalescing checkpoint cadence ([ADR 0031](../../docs/adr/0031-runtime-burst-execution-sticky-single-writer-drain-with-in-process-fast-path.md) / burst, ADR 0032 / cadence) most intra-run checkpoints defer, but `RuntimeCheckpointNames.ActivityAttemptClaimed` sits unconditionally in `CoalescingRuntimeCheckpointPersistencePolicy.MandatoryFlushCheckpointNames`, and `ActivityAttemptActivationClaimer.ClaimAsync` stamps it `CheckpointRequirement=Mandatory` for every transient CLR activity. That is the pre-activation durability flush realizing spec-095 FR-019: *commit a logical invocation identity and complete materialized input snapshot before constructing or invoking user activity code*. Each CLR activity therefore forces one immediate commit — the ~1-commit-per-activity floor.

That boundary protects **at-most-once attempt visibility**: crash attribution, poison/retry accounting via `ActivityAttemptActivationClaim` state metadata. For an activity that is pure/deterministic between checkpoint boundaries, re-executing it on replay produces byte-identical state and no observable effect, so the claim boundary is *diagnostic fidelity, not correctness*. ADR 0032's ratification left one blocker open: the runtime had no "this activity performs an external side-effect" marker, so relaxed cadence was safe only where the author already knew a span was replay-safe. This unit adds that marker (R1) and makes the claim boundary conditional on it (R2). It is the only lever that gets a hot loop of pure activities below one commit per activity.

## Ratified decisions implemented

- **R1** — A `SideEffectProfile { External, ReplaySafe }` on the pinned, fingerprinted `ActivityContract`. Default `External` (fail-safe: an unmarked activity keeps the mandatory boundary). A profile change is a contract change and participates in the contract fingerprint.
- **R2** — The claimer stamps the resolved profile onto the `ActivityAttemptClaimed` checkpoint as a new `RuntimeMetadataKeys.CheckpointSideEffectProfile` metadata key (contract = source of truth, metadata = transport). The coalescing policy removes `ActivityAttemptClaimed` from the unconditional mandatory-flush set and decides per checkpoint: `External` or absent ⇒ `Immediate`; `ReplaySafe` ⇒ `Deferred` (the claim state still enters the coalesced working set and folds forward atomically at the next flush — `Deferred` ≠ `Skip`).

## Author-facing API decision

CLR activity contracts are declared today **by attributes on the activity class** (`[ActivityOutcome]`, `[ActivityInput]`, `[ActivityStructure]`, `[ActivityChildSlot]`), read by reflection in `ExecutableNodeCompiler.BuildActivityContract` and folded into the pinned runtime `ActivityContract`. ADR 0032 deliberately left the declaration shape open. This unit picks the **class attribute** idiom to match that path exactly:

```csharp
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class If : StructuralActivity, IRuntimeStructuralActivity { ... }
```

Absence of the attribute is equivalent to `SideEffectProfile.External` — the fail-safe default. This is preferred over a contract-builder call because there is no per-activity builder seam for CLR activities; their contract is entirely reflection-derived at publish time, so an attribute is the single idiomatic declaration surface (mirroring how outcomes and inputs are declared).

## Scope boundary

- **In scope**: the `SideEffectProfile` contract member + fingerprint participation; the `[ActivitySideEffectProfile]` attribute + reflection in the publish-time compiler; the claimer stamping the profile; the coalescing policy's conditional decision for `ActivityAttemptClaimed`; classifying the built-in routing composites `ReplaySafe`; the spec-095 FR-019 amendment.
- **Out of scope (preserved unchanged)**: Immediate mode (the default runtime policy flushes everything immediately regardless of profile — the profile is inert there); every other mandatory checkpoint name (terminal / suspend / bookmark / incident / `ActivityCancelled`) stays unconditional; the reusable-activity boundary (`GraphActivity` / `GraphActivityProvider`) stays `External` (an author-composed boundary's replay-safety cannot be assumed); the fold/commit-store machinery (`Deferred` already folds forward — no change needed).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A ReplaySafe hot loop collapses to one coalesced commit (Priority: P1)

Under the coalescing cadence, a run of activities all declared `ReplaySafe` defers every pre-activation claim checkpoint and folds them forward into the next flushed commit, so the durable commit count drops to the mandatory-boundary count (e.g. the terminal completion) instead of one-per-activity.

**Why this priority**: This is the entire point of the unit — the only lever that gets a pure hot loop below one commit per activity.

**Independent Test**: Decide the persistence mode of an `ActivityAttemptClaimed` checkpoint stamped `ReplaySafe`; assert `Deferred`. Drive a coalesced crash-convergence run of a `ReplaySafe` segment; assert recovery replays from the last flushed boundary with byte-identical final committed state.

**Acceptance Scenarios**:

1. **Given** the coalescing policy and an `ActivityAttemptClaimed` checkpoint whose `CheckpointSideEffectProfile` metadata is `ReplaySafe`, **When** the policy decides, **Then** the decision is `Deferred`.
2. **Given** a coalesced segment of `ReplaySafe` activities that crashes mid-segment, **When** the run recovers, **Then** it replays from the last flushed boundary and converges to byte-identical final committed state.

### User Story 2 - External / unmarked activities keep byte-identical behavior (Priority: P1)

An activity with no profile, or one declared `External`, keeps the mandatory immediate flush of its claim checkpoint exactly as today, so its logical-invocation identity and materialized input snapshot are durable before its body runs.

**Why this priority**: Correctness — the fail-safe default must not weaken. An external effect must never be re-run on replay.

**Independent Test**: Decide the mode for an `ActivityAttemptClaimed` checkpoint stamped `External` and for one with no profile metadata; assert `Immediate` in both cases. Run the spec-095 attempt/poison suites unchanged.

**Acceptance Scenarios**:

1. **Given** an `ActivityAttemptClaimed` checkpoint stamped `External`, **When** the coalescing policy decides, **Then** the decision is `Immediate`.
2. **Given** an `ActivityAttemptClaimed` checkpoint with no `CheckpointSideEffectProfile` metadata (older pinned executable), **When** the policy decides, **Then** the decision is `Immediate` (absent ⇒ fail-safe).
3. **Given** an `External` activity in a coalesced segment, **When** it is dispatched, **Then** its claim is durable before its body runs (its checkpoint flushed immediately) and attempt/poison attribution is unchanged.

### User Story 3 - A profile change moves the contract fingerprint; defaults stay stable (Priority: P1)

`SideEffectProfile` is part of the pinned contract, so changing it changes the contract fingerprint. Existing default-`External` contracts keep a byte-identical fingerprint so no pinned executable, golden, or fixture churns.

**Why this priority**: The profile must be tamper-evident through the publish-time contract gate (PR #785), and a pre-release repo should not regenerate every golden for a default nobody set.

**Independent Test**: Build two contracts identical but for the profile; assert different `SchemaFingerprint`. Build an `External` contract and a contract constructed without specifying a profile; assert identical `SchemaFingerprint`, and identical to the fingerprint computed before this change for the same inputs.

**Acceptance Scenarios**:

1. **Given** two otherwise-identical contracts, one `External` and one `ReplaySafe`, **When** their fingerprints are computed, **Then** they differ.
2. **Given** a contract with the default profile, **When** its fingerprint is computed, **Then** it equals the fingerprint of the same contract built by the profile-unaware constructor path, so default contracts do not churn.
3. **Given** a pinned `ReplaySafe` contract round-tripped through JSON, **When** it is deserialized, **Then** the fingerprint validates (the JSON carries the profile) and equals the original.

## Requirements *(mandatory)*

- **FR-001**: The pinned runtime `ActivityContract` (`Elsa.Activities.Runtime.Core.Models.ActivityContract`) MUST carry a `SideEffectProfile` member defaulting to `External`, accepted by both the public and the JSON constructors, and round-tripping through serialization.
- **FR-002**: `SideEffectProfile` MUST participate in the contract's `SchemaFingerprint` such that changing the profile changes the fingerprint, while a default-`External` contract's fingerprint is byte-identical to the pre-change fingerprint (fingerprint only the non-default profile — see plan D3).
- **FR-003**: A CLR activity MUST be able to declare `ReplaySafe` via `[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]` on the activity class; absence MUST resolve to `External`. `ExecutableNodeCompiler.BuildActivityContract` MUST read the attribute by reflection and fold the resolved profile into the pinned contract. Boundary-clone / re-stamp sites (`ActivityTemplatePlacer`) MUST carry the profile through unchanged.
- **FR-004**: `ActivityAttemptActivationClaimer` MUST stamp the resolved profile onto the `ActivityAttemptClaimed` checkpoint's metadata under `RuntimeMetadataKeys.CheckpointSideEffectProfile`. The claim checkpoint MUST remain `CheckpointRequirement=Mandatory` (the committer's `IsMandatoryCheckpoint` guard forbids only `Skip`, never `Deferred`, so the guardrail is preserved for `External` while `Deferred` is enabled for `ReplaySafe` — no profile-conditional stamping of the Mandatory requirement is needed).
- **FR-005**: `CoalescingRuntimeCheckpointPersistencePolicy` MUST NOT hold `ActivityAttemptClaimed` in the unconditional mandatory-flush set. For an `ActivityAttemptClaimed` checkpoint it MUST decide `Immediate` when the profile metadata is `External` or absent, and `Deferred` when it is `ReplaySafe`. All other mandatory names (terminal / suspend / bookmark / incident / `ActivityCancelled`) MUST stay unconditional `Immediate`. The synthetic coalesced-flush marker MUST still force `Immediate`.
- **FR-006**: The built-in pure in-workflow routing composites (`If`, `Sequence`, `Flowchart`, `For`, `ForEach`, `While`, `Do`, `Switch`, `Parallel`) MUST be declared `ReplaySafe`. `WriteLine` MUST stay `External`. The reusable-activity boundary MUST stay `External`.
- **FR-007**: Immediate mode MUST be unaffected: the default `ImmediateRuntimeCheckpointPersistencePolicy` flushes every checkpoint immediately regardless of profile, so profile is inert outside the coalescing cadence.

## Invariants that MUST survive

- **Spec-095 FR-019 identity**: the logical invocation identity + input snapshot is still *written* into the working set before user code runs for every profile; only its durable *flush timing* becomes profile-conditional. For `External` the flush is still pre-activation.
- **Committer mandatory guardrail**: `IsMandatoryCheckpoint` stays; a mandatory checkpoint can never be `Skip`ped. `Deferred` is a batched write, not a loss.
- **Attempt/poison attribution for External**: crash attribution and poison/retry accounting are byte-identical for `External`/unmarked activities.
- **Replay safety**: an `External` activity's claim is durable before its body runs, so its effect is never re-run on replay.

## Success Criteria *(mandatory)*

- **SC-001**: A `ReplaySafe` `ActivityAttemptClaimed` checkpoint decides `Deferred`; an `External`/absent one decides `Immediate`.
- **SC-002**: A coalesced `ReplaySafe` segment that crashes recovers by replay from the last flushed boundary with byte-identical final committed state; an `External` activity's claim is durable before its body runs.
- **SC-003**: Changing a contract's profile changes its fingerprint; default-`External` fingerprints are unchanged from before this unit.
- **SC-004**: Attempt/poison attribution is unchanged for `External`. The full runtime, Groundwork persistence, activities-runtime, and publishing contract-gate test projects pass.

## Expected commit-count math (Coalesced mode)

| Run | Mandatory boundaries | Claim commits before | Claim commits after | Total before → after |
|---|---|---|---|---|
| 2-node Flowchart → WriteLine (WriteLine **External**) | 1 (terminal) | 2 (Flowchart + WriteLine claims) | 1 (WriteLine only) | **3 → 2** |
| 2-node Flowchart → WriteLine (WriteLine **ReplaySafe**) | 1 (terminal) | 2 | 0 | **3 → 1** |
| 10-pure-activity hot loop (all **ReplaySafe**) | 1 (terminal) | 10 | 0 | **11 → 1** |

Immediate mode is unchanged in all rows (every checkpoint flushes; profile is inert).
