# Runtime Execution Seam Status Audit

Status: point-in-time reconciliation of the [Runtime Execution Seam](../program-goals/runtime-execution-seam.md) bucket against the actual `specs/` tree and `src/`.

Date: 2026-07-02.

> **Scope + freshness.** This is a **spec-delivery inventory** (which of specs 065–081 are delivered/in-progress) — complementary to, not overlapping with, the execution-behavior re-baseline in [runtime expression-context source reconciliation](runtime-expression-context-source-reconciliation.md) (which covers the expression-context seam and the execution-state/pipeline contract). It is a snapshot: since it was written, spec 006's `Composition.Design` (workflow-as-activity, T028/T029) landed via PR #358, and the expression-context objective has advanced to ADR 0030 — see the bucket's Reconciliation checkpoint for the current runtime picture.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

## Why This Exists

The bucket file previously framed the seam as unplanned pre-spec work needing an incoming architect to author a first Speckit spec. A reconciliation pass found that most of the seam had already been spec'd, implemented, and merged across specs 065–081, while the bucket linked only four specs (006, 073, 079, 080). This report records the verified status so future sessions do not re-derive it or re-open closed questions.

## Method

- Read `spec.md`, `tasks.md`, and `plan.md` (where present) for each runtime-relevant spec.
- Counted completed vs. open tasks in `tasks.md` by checkbox state.
- Spot-checked `src/` for the presence or absence of the core types each spec adds or removes.
- Cross-checked the brainstorm/decision reports and two ADRs for decided-but-untracked content.

Task counts and source spot-checks below were confirmed directly, not inferred from summaries.

## Spec Status

| Spec | Status | Evidence |
|---|---|---|
| 065 Remove execution pool | Delivered | tasks.md complete; `IWorkflowExecutionAgentProvider` is sole execution-ownership seam |
| 066 Storage driver boundary | Delivered | tasks.md complete; `IDurableValueStateStore`/`DurableValueState` own durability; legacy driver contracts removed |
| 067 Remove direct executor | Delivered | tasks.md complete; inline `IWorkflowExecutor`/`SequentialWorkflowExecutor` removed; agent + scheduler are sole path |
| 068 Composed activity execution | Delivered | tasks.md complete; proves executable + activity seams compose to run a pinned artifact |
| 069 Request-affine execution | Implementation complete; T010 open | tasks.md T001–T009 `[x]`, T010 `[ ]` (commit/PR/merge only) |
| 070 Workflow root-activity contract | Delivered | tasks.md 14/14 `[x]`; verified `ActivityNode` (`src/Elsa/Workflows/Design/Core/Models/ActivityNode.cs`) and `ExecutableNode` (`src/Elsa/Workflows/Runtime/Core/Models/ExecutableNode.cs`) carry no generic `Composition`/`Edges`/`StartNodeIds`; `ExecutableChildSlot` remains only as an opaque projection |
| 071 Activity-owned composite structure | Draft spec only | `spec.md` present, `Status: Draft`; no `plan.md`/`tasks.md`; likely largely subsumed by 070 (see below) |
| 073 Flowchart scoped execution | Delivered | merged (#94); clean-slate Flowchart execution model |
| 076 Workflow test runs | Delivered | merged (#101); ephemeral compile-and-run artifact |
| 077 Workflow instance inspection | Delivered | tasks.md 31/31 `[x]`, all phases incl. Polish complete |
| 079 Activity execution inspection | Delivered | tasks.md 51/51 `[x]`, all 7 phases complete |
| 080 Runtime checkpoint commit | Delivered | merged (#113); `IRuntimeCheckpointCommitStore` |
| 006 Activity construction seam | In progress | tasks.md 37/55 `[x]`; 18 open, incl. workflow-as-activity composition wiring (T028/T029, new Composition.Runtime/.Design projects) |
| 081 Typed argument model | Backend delivered | merged (#330); Studio Phase 2 unstarted, tracked outside this bucket |

Spec-number collisions exist (parallel branch numbering not yet reconciled): 071, 073, and 079 each have a runtime sibling and an unrelated non-runtime sibling (`071-groundwork-host-configurable-runtime-store-poc`, `073-diagnostics-structured-logs`, `079-secrets-module`). Only the runtime siblings are in scope here.

## Corrections To Prior Reports

- The "graph-shaped workflow boundary is open" finding in [unfinished-work.md](unfinished-work.md) and the roadmap notes is **resolved by 070** and should no longer be treated as open. (Handled in the bucket update; the unfinished-work row should be closed on its next refresh.)
- Earlier summaries described 077 and 079 as "in progress"; both are task-complete as of this audit.

## Decided-But-Untracked Content

Two bodies of settled decisions were not represented in the bucket's objectives and are now linked and turned into Remaining objectives:

1. **Seven addendum decisions** — [elsa-4-runtime-execution-addendum-topics.md](elsa-4-runtime-execution-addendum-topics.md): volatile wait vs. durable suspension, activity-completion propagation, event-driven generators vs. triggers, pause/unpause control-plane semantics, runtime terminology/glossary, wait-registration/post-commit-intent correlation, actor-style distributed execution. Locked, but no spec or implementation surface. This is the largest genuinely-undesigned area remaining.
2. **Fifteen serialization/value-persistence decisions** — [elsa-4-runtime-serialization-brainstorm-decisions.md](elsa-4-runtime-serialization-brainstorm-decisions.md): unified value declaration model, durability vocabulary, ephemeral-by-default outputs. Partially reflected; no dedicated spec yet.

The [action plan](elsa-4-runtime-execution-action-plan.md)'s 9 slices predate the delivered specs and should be cross-checked against this audit before any slice is treated as unstarted.

## Governance Loose Ends

- [ADR 0001 checkpoint-gated activity execution inspection](../adr/0001-checkpoint-gated-activity-execution-inspection.md) is still `Status: proposed` although the 079 work it justifies is fully shipped. Needs accept-or-revise.
- [ADR 0020 runtime checkpoint commit post-commit work](../adr/0020-runtime-checkpoint-commit-post-commit-work.md) is the accepted basis for the delivered 080 work.

## What This Report Does Not Do

- It does not re-verify the internal correctness or test quality of the delivered specs; it confirms task-completion state and the presence/absence of key types.
- It does not decide the fate of 071 or author any new spec.
- It does not close the `unfinished-work.md` rows itself; that belongs to the next refresh of that report.
