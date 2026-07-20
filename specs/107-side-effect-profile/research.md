# Research

## Existing seams reused (no reinvention)

- **The persistence-policy seam already carries the three modes.** `IRuntimeCheckpointPersistencePolicy.DecideAsync(RuntimeCheckpoint)` returns `Immediate`/`Deferred`/`Skip`, and the committer honours all three; `Deferred` already folds the checkpoint's state forward into the next flushed commit (the coalescing store owns the working set). This unit only makes the policy's *decision* for one checkpoint name profile-conditional — no new fold machinery.
- **The claim checkpoint already carries metadata.** `ActivityAttemptActivationClaimer.ClaimAsync` builds `checkpointMetadata` (scheduler work-item id, command id, reason, `CheckpointRequirement`, executable identity) and stamps it on the `RuntimeCheckpoint`. Adding `CheckpointSideEffectProfile` is one more key in that same dictionary; the policy reads `checkpoint.Metadata` it already receives.
- **CLR activity contracts are reflection-derived at publish time.** `ExecutableNodeCompiler.BuildActivityContract` reads `[ActivityOutcome]`/`[ActivityInput]`/output attributes off the activity `Type` and constructs the pinned `ActivityContract`. The profile attribute is read at the same site with the same reflection idiom — no new provider seam.
- **The contract fingerprint already has a conditional-inclusion precedent.** `ComputeFingerprint` includes `Result.SourceRepresentation` only `if (…HasValue)`. Fingerprinting the profile only when non-default follows that exact pattern, keeping default contracts byte-identical.

## Mandatory-guardrail analysis (committer)

`RuntimeCheckpointCommitter.CommitAsync` throws only when `decision.Mode == Skip` **and** `IsMandatoryCheckpoint`. `Deferred` is never blocked by Mandatory. Confirmed by reading the committer: the mandatory branch is inside `if (decision.Mode == RuntimeCheckpointPersistenceMode.Skip)`. Therefore keeping the claim checkpoint `Mandatory` is safe and desirable — it forbids a disposable `Skip` (which would lose the claim) while still permitting the batched `Deferred` a `ReplaySafe` activity wants. ADR 0032 §"Policy surface" item 3 ("do not weaken the mandatory guardrail") is preserved literally: `IsMandatoryCheckpoint` is untouched.

## Immediate-mode inertness

The default `ImmediateRuntimeCheckpointPersistencePolicy.DecideAsync` returns `Immediate` unconditionally, so under the default policy `External` and `ReplaySafe` are indistinguishable — the profile is inert. The measured 40–60-transaction hot-loop cost that ADR 0032 targets is a **Coalesced-mode** cost (each drain step commits its claim), so the optimization is correctly scoped to `CoalescingRuntimeCheckpointPersistencePolicy` alone.

## Replay-safety classification (why routing composites are ReplaySafe)

The built-in routing composites (`If`/`Sequence`/`Flowchart`/`For`/`ForEach`/`While`/`Do`/`Switch`/`Parallel`) are `IRuntimeStructuralActivity` that only schedule children and evaluate authored conditions; they produce no externally observable effect and their child scheduling is idempotent through per-`RuntimeSchedulerWorkItem` `IdempotencyKey`s (ADR 0031). Re-executing one on replay re-derives byte-identical routing state. `WriteLine` performs a console write (observable) and stays `External`. `GraphActivity` (reusable-activity boundary) wraps arbitrary author-composed children whose profiles are unknown at the boundary, so it stays `External` — the fail-safe default. This mirrors ADR 0032's correctness classification: the mandatory set is precisely the set whose re-execution or loss would be observable.

## Fingerprint churn decision (pre-release repo)

The repo is pre-release with a no-back-compat rule, so regenerating goldens is permitted. It was **not** chosen: fingerprinting only the non-default profile keeps every default-`External` contract byte-identical, so zero goldens/fixtures churn while a `ReplaySafe` declaration still moves the fingerprint through the publish-time contract gate (PR #785). This is strictly less churn for identical observable behavior.
