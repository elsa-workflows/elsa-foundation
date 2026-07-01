# Runtime Execution Pipeline Wiring — Sizing

Status: read-only sizing input for a prospective Runtime Execution Seam work unit ("route runtime execution through the workflow + activity pipelines"). Not a spec, ADR, or implementation plan. Companion to [runtime expression-context source reconciliation](runtime-expression-context-source-reconciliation.md).

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

## The key insight: two separable moves, not one refactor

The most important sizing result is that "wire the pipeline" decomposes into two moves with very different cost/value:

- **Move 1 — make the pipeline real (cheap, high value).** Inject a pipeline executor at the single handler-dispatch point in `WorkflowSchedulerDrainer` and invoke `pipeline.InvokeAsync(context, () => handler.HandleAsync(workItem))`. The built-in slots stay no-ops and the handlers are unchanged, so there is **zero behavior change** — but a module-registered `IActivityRuntimeMiddleware`/`IWorkflowRuntimeMiddleware` now **actually runs**. This alone kills the false affordance and gives the module system a real runtime-execution insertion point. Small, low-risk.
- **Move 2 — decompose handlers into slots (expensive, incremental).** Lift each inlined phase out of the handlers into its slot-bound middleware so built-in behavior lives in the pipeline, not the handler. This is the large surface (~4,500 LOC across ~10 handlers) and carries all the hazards below.

Move 1 delivers the extensibility the framework's modular charter needs. Move 2 is internal cleanliness and can be done incrementally, one handler at a time, or deferred. **You can make the pipeline the true execution spine (Move 1) without the big refactor.**

## Intended contract (locked by `RuntimePipelineContractTests`)

- **Workflow pipeline:** `LoadState`(100) → `Scheduling`(200) → `Checkpoint`(300) → `PostCommit`(400). Context: `WorkflowRuntimePipelineContext(WorkflowExecutionState, SchedulerState?)`.
- **Activity pipeline:** `LoadState`(100) → `InputEvaluation`(200) → `Invoke`(300) → `OutputCapture`(400) → `Scheduling`(500) → `Checkpoint`(600) → `PostCommit`(700). Context: `ActivityRuntimePipelineContext(WorkflowExecutionState, ActivityExecutionState, SchedulerState?)`.

The contract test already locks slot-name+order registration, distinct workflow/activity context types, all-slots-filled, and unknown-slot rejection. Placeholder middleware exist for every slot as pass-throughs.

## Handler inventory (Move-2 difficulty)

| Handler | ~LOC | Pipeline | Move-2 difficulty |
|---|---:|---|---|
| `WorkflowStartSchedulerWorkHandler` | ~186 | Workflow | Easy (validate → enqueue root; no faults) |
| `WorkflowStartActivitySchedulerWorkHandler` | ~180 | Activity | Easy (Scheduled→Running; single checkpoint) |
| `WorkflowScheduleActivitySchedulerWorkHandler` | ~170 | Activity | Easy (create scheduled state) |
| `WorkflowCreateBookmarkSchedulerWorkHandler` | ~200 | Activity | Easy (suspend + bookmark; no write-back) |
| `WorkflowCheckpointSchedulerWorkHandler` | ~200 | Workflow | Easy (aggregate state → commit) |
| `WorkflowCancelSchedulerWorkHandler` | ~120 | Workflow | Easy (bulk cancel → commit) |
| `WorkflowCompleteActivitySchedulerWorkHandler` | ~200 | (router) | Medium (state-machine router; keep as routing) |
| `WorkflowResumeBookmarkSchedulerWorkHandler` | ~603 | Activity | Hard (resume reflection + fault arms) |
| `WorkflowParentActivityCompletionSchedulerWorkHandler` | ~843 | Activity | Hard (parent callbacks + scope capture + fault) |
| `WorkflowInvokeActivitySchedulerWorkHandler` | ~967 | Activity | Very hard (all hazards) |

Six easy handlers (~1,400 LOC) are low-risk; the three hard handlers (~2,400 LOC) hold every hazard.

## Extraction hazards (why Move 2 is non-trivial)

1. **Atomic checkpoint-commit folding (#310).** Durable-value write-back (#286 variables, #260 SetOutput) must land in the *same* commit as the completion/suspension state change. → carry on the pipeline context; the `Checkpoint` middleware assembles the final commit.
2. **Fault boundaries (7 arms across 3 handlers).** Incident + checkpoint are transactional per fault arm. → keep fault handling inside the `Invoke` body or a dedicated wrapping middleware; do not split a phase's fault path across slots.
3. **Control-leaf intents (#260/#308: Finish / Correlate / SetName / SetOutput).** Captured during `Invoke`, applied to `WorkflowExecutionState` at `Checkpoint`. → mutable intent state on the context.
4. **Container scope-completion capture (#210 / ADR 0027).** Only on composite completion, not on suspend/child-schedule. → completion-mode flag on the context.
5. **Inspection toggle.** Presence/absence of `IRuntimeActivityExecutionInspectionAccumulator` selects checkpoint-commit vs direct-persist; both paths must carry the same write-back. → normalize in the `Checkpoint` middleware.

## Recommended sequencing

- **Unit 1 (make it real).** Dispatch-wrapper + build the two contexts from the work item + a pipeline executor + DI registration + a guardrail test asserting a registered middleware actually runs (mirrors the JS-processor D4 guardrail against re-orphaning). Low risk, no behavior change.
- **Unit 2+ (decompose, incremental).** Extract the two shared slots first — `LoadState` and `Checkpoint`, which repeat across nearly every handler — then per-handler specifics. Order easiest→hardest: Start → Cancel → Checkpoint → CreateBookmark → ResumeBookmark → ParentCompletion → **InvokeActivity last** (long pole, very high risk; only after the pattern is proven).

Rough effort for wrapper + `LoadState` + `Checkpoint` slots is on the order of 2–3 weeks; full `InvokeActivity` decomposition is the long pole and should be last.

## Open decisions for the pipeline ADR

- **Wrap vs replace.** Option B (pipeline wraps the handler; handler = `Invoke`-slot body) first, Option A (per-slot middleware replaces handler internals) incrementally? Recommended: B then A.
- **Scope of Move 2.** Do built-in phase behaviors become framework middleware in fixed slots, or stay in handlers with only cross-cutting slots exposed to modules? (i.e., is Move 1 sufficient for now?)
- **Guardrail.** Extend the contract test to assert execution actually flows through the pipeline, so it cannot silently re-orphan.

## Follow-up surface

Feeds a dedicated ADR + Speckit unit ("route runtime execution through the workflow + activity pipelines"), sequenced after or alongside the expression-context unit. Recorded in the [Runtime Execution Seam](../program-goals/runtime-execution-seam.md) bucket.
