# Implementation Plan: Author-Declared Side-Effect Profile

**Spec**: [spec.md](spec.md)

## Technical context

- Language/stack: C# / .NET, Elsa activity-contract core + runtime checkpoint policy + publish-time compiler.
- Load-bearing part (why it needs a Speckit spec, not thin glue): the profile becomes part of the **pinned, fingerprinted** contract and changes the durability cadence of a correctness-adjacent checkpoint. Getting the fingerprint participation and the fail-safe default wrong would either churn every golden or silently defer an external effect's durability.
- No new document kind, no storage-manifest change: the profile rides existing checkpoint metadata; `Deferred` reuses the coalescing fold that already exists.

## Design decisions

### D1 — Profile lives on the runtime contract, transported by checkpoint metadata

`SideEffectProfile` is a member of the pinned `Elsa.Activities.Runtime.Core.Models.ActivityContract` (source of truth). The claimer resolves it from `ExecutableNode.ActivityContract.SideEffectProfile` and stamps `RuntimeMetadataKeys.CheckpointSideEffectProfile` onto the `ActivityAttemptClaimed` checkpoint (transport). The coalescing policy reads only the checkpoint (it already receives `RuntimeCheckpoint`), so no new carrier and no policy dependency on the contract store.

### D2 — Author-facing declaration is a class attribute (matches the CLR idiom)

CLR activity contracts are entirely reflection-derived at publish time (`ExecutableNodeCompiler.BuildActivityContract` reads `[ActivityOutcome]`/`[ActivityInput]`/`[ActivityStructure]`). There is no per-activity contract-builder seam. So the profile is a class attribute `[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]`, read the same way. Absence ⇒ `External`. ADR 0032 left this open; this is the decision.

### D3 — Fingerprint only the non-default profile

`ActivityContract.ComputeFingerprint` includes `sideEffectProfile` in the canonical JSON **only when it is not `External`**. This satisfies both fingerprint requirements at once: a change to `ReplaySafe` moves the fingerprint (it appears in the canonical shape), and every existing default-`External` contract keeps a byte-identical canonical shape — so no pinned executable, golden, or fixture churns and no goldens must be regenerated. This mirrors the file's existing conditional inclusion of `SourceRepresentation`. (The pre-release no-back-compat rule would have permitted regenerating goldens instead; fingerprinting-only-non-default is chosen because it is strictly less churn for identical behavior.)

The **serialized document shape** is made consistent with the fingerprint the same way: the `SideEffectProfile` property carries `[JsonIgnore(Condition = WhenWritingDefault)]`, so a default-`External` contract omits the property entirely and its serialized `workflowExecutable`/contract document is byte-identical to before this unit (the `Fixtures/vN/workflowExecutable.json` golden stays valid). Only a `ReplaySafe` contract emits the field. Without this the property serialized as `sideEffectProfile: 0` and churned the golden; the fix keeps "the default is invisible" true in both the fingerprint and the wire shape.

### D4 — Keep the claim checkpoint Mandatory; only the flush timing is profile-conditional

`RuntimeCheckpointCommitter.IsMandatoryCheckpoint` forbids only `Skip` (a mandatory checkpoint carrying post-commit work throws on skip); it does **not** forbid `Deferred`. So the claimer keeps stamping `CheckpointRequirement=Mandatory` unconditionally: for `External` the policy returns `Immediate` (unchanged), for `ReplaySafe` the policy returns `Deferred` (allowed, because Mandatory only blocks Skip). This preserves the committer's guardrail for `External` and enables batching for `ReplaySafe` without any profile-conditional Mandatory stamping. `Deferred ≠ Skip`: the claim state enters the coalesced working set and folds forward atomically at the next flush — nothing is lost.

### D5 — Immediate mode is inert; the optimization is Coalesced-only

The default `ImmediateRuntimeCheckpointPersistencePolicy` returns `Immediate` for everything, so the profile has no effect there — `External` and `ReplaySafe` behave identically under the default policy. The commit saving lives exactly where the measured 40–60-transaction cost lives: the coalescing cadence. Only `CoalescingRuntimeCheckpointPersistencePolicy` reads the profile.

### D6 — Built-in classification and the reusable boundary

The pure in-workflow routing composites are `ReplaySafe` (they schedule children / evaluate conditions, no external effect; re-running the routing on replay is idempotent through per-work-item keys). `WriteLine` stays `External` (console write is an observable effect). The reusable-activity boundary (`GraphActivity` / `GraphActivityProvider`) stays `External`: an author-composed boundary wraps arbitrary children whose replay-safety cannot be assumed, so the fail-safe default holds there.

## Changed components

| File | Change |
|---|---|
| `Activities/Runtime/Core/Models/ActivityContract.cs` | Add `SideEffectProfile` enum; add member + both-constructor params (default `External`); conditional fingerprint participation (D3); enum validation. |
| `Activities/Runtime/Core/Attributes/ActivitySideEffectProfileAttribute.cs` (new) | Class attribute carrying the profile (D2). |
| `Workflows/Publishing/Api/Services/ExecutableNodeCompiler.cs` | Reflect the attribute in `BuildActivityContract`, fold into the pinned contract. |
| `Workflows/Publishing/Api/Services/ActivityTemplatePlacer.cs` | Carry `contract.SideEffectProfile` through the boundary re-stamp clone. |
| `Workflows/Runtime/Core/Constants/RuntimeMetadataKeys.cs` | Add `CheckpointSideEffectProfile` key + `External`/`ReplaySafe` value constants. |
| `Activities/Runtime/Services/ActivityAttemptActivationClaimer.cs` | Thread the profile into `ClaimInvokeAsync`/`ClaimStructuralCallbackAsync`/`ClaimAsync`; stamp the metadata key; keep Mandatory (D4). |
| `Activities/Runtime/Services/WorkflowInvokeActivitySchedulerWorkHandler.cs` | Pass `executableNode.ActivityContract.SideEffectProfile`. |
| `Activities/Runtime/Services/WorkflowParentActivityCompletionSchedulerWorkHandler.cs` | Pass `parentExecutableNode.ActivityContract.SideEffectProfile`. |
| `Workflows/Runtime/Services/Coalescing/CoalescingRuntimeCheckpointPersistencePolicy.cs` | Remove `ActivityAttemptClaimed` from the unconditional set; conditional `Immediate`/`Deferred` on the profile metadata. |
| `Activities/{ControlFlow/If,ControlFlow/For,ControlFlow/ForEach,ControlFlow/While,ControlFlow/Do,ControlFlow/Switch,ControlFlow/Parallel,Sequence,Flowchart}/Activities/*.cs` | `[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]`. |
| `specs/095-value-flow-redesign/spec.md` | Amend FR-019 (and FR-020/FR-022 wording) so the pre-activation *flush* is profile-conditional; the claim/identity is always written. |

## Test strategy

Extend `RuntimeCheckpointCoalescingPolicyTests` (conditional `ActivityAttemptClaimed` decision: `External`⇒Immediate, absent⇒Immediate, `ReplaySafe`⇒Deferred; the other mandatory names stay Immediate). Prove the store-level buffer-vs-flush routing the deferral relies on in `RuntimeCheckpointCoalescingTests` (a `Deferred` ReplaySafe `ActivityAttemptClaimed` buffers into the overlay — nothing durable — and folds forward into the terminal flush; the existing `Immediate` boundary test covers the External case). Add contract-fingerprint tests (profile changes the fingerprint; default is stable and equals the profile-unaware path; JSON round-trip validates). Add a compiler test (attribute → pinned contract profile). Run the spec-095 attempt/poison suites unchanged. Run the four full test projects and report totals.

**Crash-convergence test placement note**: the WU pointed at `GroundworkCoalescingCrashConvergenceTests`, but that suite's fixture drives a bare `test/activity` node with **no** `ActivityContract`, so it never exercises the CLR attempt-claim path this unit changes. The honest, direct proof of the ReplaySafe deferral is the store-level buffer-vs-flush test added to `RuntimeCheckpointCoalescingTests` (which owns the `RuntimeCoalescingSession` / `CoalescingRuntimeCheckpointCommitStore` fixtures). The crash-convergence *mechanism* the deferred claim now rides — buffer → fold-forward → crash-before-flush → queue retains segment entry → replay converges — is already covered end-to-end by `GroundworkCoalescingCrashConvergenceTests` for deferred checkpoints generally, and a ReplaySafe claim is now routed through that exact path.
