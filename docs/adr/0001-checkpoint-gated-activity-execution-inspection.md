# Checkpoint-Gated Activity Execution Inspection

Status: accepted (2026-07-02)

Activity execution lifecycle changes that create or advance scheduler work will commit through `RuntimeCheckpointCommit`, and activity execution inspection projections will be included in the same checkpoint before dependent scheduler work is enqueued through post-commit scheduler intents. This deliberately replaces direct state writes followed by direct queue writes at scheduler boundaries, because instance inspection must reflect committed runtime evidence and recovery must not observe scheduler work for activity state that was never durably committed.

## Acceptance note (2026-07-02)

Accepted retroactively during the [Runtime Execution Seam status audit](../reports/runtime-execution-seam-status-audit.md). The decision was left `proposed` while the work it governs was implemented. [Spec 079 activity execution inspection](../../specs/079-activity-execution-inspection/spec.md) shipped complete (51/51 tasks, all phases) on top of [spec 080 runtime checkpoint commit](../../specs/080-runtime-checkpoint-commit/spec.md) and its [ADR 0020](0020-runtime-checkpoint-commit-post-commit-work.md), realizing this rule: checkpoint-gated commit of inspection projections before dependent scheduler work is enqueued via post-commit intents. Accepting now aligns the ADR status with delivered, verified behavior rather than introducing new design.
