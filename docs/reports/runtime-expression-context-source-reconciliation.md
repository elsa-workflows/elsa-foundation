# Runtime Expression-Context — Source Reconciliation

Status: codebase-verification report (read-only). Re-baselines the pre-spec runtime-execution reports against current `src/` as of 2026-07-02. Not a Speckit spec, ADR, or implementation plan.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md), Active Objective 12.

Method: three independent read-only source sweeps (expression-context seam; executable-artifact + execution-state contract Slices 1–3; pipeline/bookmark/bindings/diagnostics/recovery Slices 4–9), each required to cite `file:line` evidence and to prefer source over report where they conflict ("code is truth over reports").

## Why this report exists

The Runtime Execution Seam bucket carries several pre-implementation planning reports written by comparing intent against `elsa-core` release/3.8.0 and against early Elsa 4 slices:

- [Elsa Core runtime expression-context wiring analysis](elsa-core-runtime-expression-context-wiring-analysis.md)
- [Runtime execution pre-spec handoff](runtime-execution-pre-spec-handoff.md)
- [Elsa 4 runtime execution brainstorm decisions](elsa-4-runtime-execution-brainstorm-decisions.md)
- [Elsa 4 runtime execution action plan](elsa-4-runtime-execution-action-plan.md)

Substantial runtime work has since merged (#205/#210 scoped variables, #260 SetOutput/SetName, #286 workflow-scope variable write-back, #310 checkpoint-folded write-back, #308/#317 fault handling, specs 079/080, ADRs 0020/0026/0027/0028). Several premises those reports encode are now stale. This report establishes the current baseline so the next ADR/spec starts from code, not from overtaken planning notes.

## Part A — Expression-context seam: premise re-baseline

| Premise (as stated in handed evidence) | Verdict | One-line reason |
|---|---|---|
| P1. The new runtime persists no workflow variables/inputs; only outputs resolve at execution. | **Refuted** | Variables and inputs are seeded, projected from `DurableValueState`, and written back at execution. |
| P2. JS `getVariable`/`getInput`/`getOutput` do not resolve end-to-end. | **Partly** | They resolve at input-materialization time via `IMaterializationExpressionState`; only the execution-time named/identity accessors are dead. |
| P3. The five `IWorkflowExecutionContext` JS pre/post-processors are silently dead code. | **Confirmed, sharper** | Dead — but they throw *loudly* (whole `IEnumerable<IScriptPreProcessor>` fails to resolve) if their feature is enabled, and no test enables it. |

### P1 — variables/inputs ARE persisted (refuted)

`WorkflowInvokeActivitySchedulerWorkHandler` projects and persists workflow variables and inputs from the durable-value store on every activity invocation:

- Lists durable values and projects workflow variables + inputs into the resolution context — `WorkflowInvokeActivitySchedulerWorkHandler.cs:152-170` (`RuntimeInputBindingStateProjection.ProjectWorkflowVariables` / `ProjectWorkflowInputs`).
- Captures workflow-scope variable mutations as durable-value write-back — `:257-258` (`BuildWorkflowScopeWriteBackChanges`), folded into the activity's checkpoint commit at `:375-379`.
- `SetOutput` folds `OutputName`-tagged durable values on the same boundary — `:273-275`.

The "only outputs resolve / no variable persistence" claim reflects an earlier slice, not current code.

### P2 — materialization accessors work; execution-time accessors are the dead part (partly)

The **working** path is the analysis's own *preferred* Option 2 (parameter-carrier, no DI):

- `MaterializationAccessorsPreProcessor` takes no constructor dependencies, casts the passed `IExpressionExecutionContext` to `IMaterializationExpressionState`, and registers `variables`/`input`/`output` containers plus `getVariable`/`getInput`/`getOutput`/`getOutputFrom` — `MaterializationAccessorsPreProcessor.cs:22-56`.
- The carrier is `MaterializationExpressionExecutionContext`, populated from `RuntimeInputBindingResolutionContext` (WorkflowVariables / WorkflowInputs / ActivityOutputValues) — `RuntimeActivityInputMaterializer.cs` (context constructed ~`:89`, `IMaterializationExpressionState` members ~`:247-255`).
- End-to-end tests exercise seeded variable/input → durable state → projection → expression evaluation → activity input (e.g. `WriteLineVariableInputExpressionExecutionTests`, `SeededVariableEndToEndExecutionTests`).

`IExpressionExecutionContext` has **no** transient-properties bag or typed slot (`IExpressionExecutionContext.cs:5-130`). `IMaterializationExpressionState` is the *only* carrier present today — narrower than elsa-core's general `TransientProperties`, but the same shape.

### P3 — five processors dead, and an unguarded landmine (confirmed)

- The five execution-time processors take `IWorkflowExecutionContext` by constructor: `WorkflowInputFunctionsPreProcessor` (`:10`), `WorkflowFunctionsPreProcessor` (`:9`), `VariableFunctionsPreProcessor` (`:12`), `ActivityOutputFunctionsPreProcessor` (`:10`), `CopyVariablesToWorkflowContext` (`:13`).
- All are registered `AddScoped<IScriptPre/PostProcessor, …>()` in `JavaScriptWorkflowsRuntimeFeature.cs:23-30`, alongside the working `MaterializationAccessorsPreProcessor`.
- `IWorkflowExecutionContext` is registered **nowhere** in `src/`; `WorkflowExecutionContext` is `new`'d only under `tests/`.
- `PreProcessScript` resolves `IEnumerable<IScriptPreProcessor>` (`PreProcessScript.cs:12`), so MS DI eagerly constructs every registered implementation. A missing `IWorkflowExecutionContext` therefore throws for the **whole enumerable** — taking `MaterializationAccessorsPreProcessor` down with it — on first script evaluation *if the feature is enabled*.
- **No test enables `JavaScriptWorkflowsRuntimeFeature`.** Runtime tests register `MaterializationAccessorsPreProcessor` directly and comment that the feature's other processors "require a live execution context." So the throw is unguarded by the suite: enabling the feature in a host would break all JS evaluation, and nothing would catch it first.

### Genuinely dead / missing behavior today

| Behavior | Status |
|---|---|
| Generic `getVariable`/`getInput`/`getOutput` at input-materialization time | **Works** (materialization carrier) |
| Named pascalized accessors, e.g. `getGreeting()` for variable `greeting` | Dead (`VariableFunctionsPreProcessor`, `WorkflowInputFunctionsPreProcessor`) — and not provided by the materialization path either |
| Execution-time generic accessors + workflow-identity functions (`getWorkflowInstanceId`, …) | Dead (`WorkflowFunctionsPreProcessor`) |
| Activity-output accessors at execution time | Dead (`ActivityOutputFunctionsPreProcessor`) |
| JS-side variable write-back after evaluation | Dead (`CopyVariablesToWorkflowContext`) |

## Part B — Larger executable-artifact + execution-state contract: conformance

Verified against the action plan's "first Speckit unit" (Slices 1–9) and the locked brainstorm decisions (1–10). Verdict: **substantially implemented as intended.** The recommended "first unit" is, in effect, already built.

| Area (intended) | Verdict | Anchor types |
|---|---|---|
| S1 Executable artifact — single root activity, identity/hash, resume table, artifact-only load | Implemented-as-intended | `WorkflowExecutable`, `WorkflowExecutableIdentity`, `ExecutableNode`, `WorkflowExecutableResumeTarget`, `IWorkflowExecutableStore` |
| S2 Split state model | Implemented-as-intended | `WorkflowExecutionState`, `ActivityExecution`/`ActivityExecutionState`, `SchedulerState`, `DurableValueState` (+ stores) |
| S2 ActivityExecution durable identity (Decision 3 fields; inputs/outputs excluded) | Implemented-as-intended | `ActivityExecutionState`, `ActivitySchedulingProvenance` |
| S3 Checkpoint contract, atomic envelope, persistence-policy separation, post-commit intents | Implemented-as-intended (checkpoint-name superset) | `RuntimeCheckpointNames` (+3 operational names), `RuntimeCheckpoint(Commit)`, `RuntimeCheckpointStateChangeSet`, `RuntimeCheckpointPersistenceDecision`, `RuntimePostCommitIntent` |
| S5 Bookmark resume by stable `ResumeTargetId` (not method names) | Implemented-as-intended | `BookmarkState`, `ResumeTargetAttribute`, `WorkflowExecutableResumeTarget`, `IBookmarkStateStore`, `IBookmarkResumeResolver` |
| S6 Input bindings, active-scope output register, durable capture, binding diagnostics | Implemented-as-intended | `RuntimeInputBinding`, `IRuntimeActivityOutputRegister`, `RuntimeOutputCapture`, `IRuntimeInputBindingResolver`/`Validator` |
| S7 History/incident separation, payload-capture policy | Implemented-as-intended (sensitive-value default: **partial/unverified**) | `RuntimeHistoryEvent`, `IncidentState` + `IncidentHistoryProjection`, `IRuntimePayloadCapturePolicy` |
| S8 Operational recovery + outbox, domain-retry boundary | Implemented-as-intended | `OperationalState` (lease/heartbeat/drain/interrupted), `IRuntimeRecoveryScanner`, `IRuntimePostCommitOutboxStore`, `IRuntimeDomainRetryPolicy` |
| S9 Elsa 3 migration boundary (import-only; live resume rejected) | Implemented-as-intended | `Elsa3MigrationBoundary`, `Elsa3MigrationCompatibility.RejectLiveInstanceResume<T>()` |
| S4 Pipeline slots + inspectable plan | **Scaffolded but unwired (drift/deferred)** | See below |

### The one real gap — S4 execution pipeline is scaffolded but UNWIRED

Corrected 2026-07-02 (was previously mischaracterized in this report as a "deliberate divergence"). The brainstorm pinned two middleware pipelines with stable named slots each, traversed in order. The pipeline **contract** exists — `RuntimeWorkflowPipelineSlots`, `RuntimeActivityPipelineSlots`, `RuntimePipelinePlan`, `WorkflowRuntimePipelineBuilder`/`ActivityRuntimePipelineBuilder`, `IWorkflowRuntimeMiddleware`/`IActivityRuntimeMiddleware` — but it is a **skeleton that nothing invokes**:

- The middleware are empty placeholders (`RuntimeActivityInvokeMiddleware : ActivityRuntimeMiddlewareBase;` etc.) whose base `InvokeAsync` just calls `next(context)`. No behavior.
- The builders / `BuildPlan()` are called **only from `RuntimePipelineContractTests`** (a slot-order contract test), never in production; the pipeline is registered in **no feature and no DI**.
- There is **no pipeline invoker** — nothing composes middleware into a chain and runs it.
- Real execution is **inlined** in scheduler work handlers: `WorkflowSchedulerCommandProcessor` → enqueue `RuntimeSchedulerWorkItem` → drain → `IWorkflowSchedulerWorkHandler.HandleAsync`, and `WorkflowInvokeActivitySchedulerWorkHandler` (~970 lines) performs every phase the activity slots name (load state, evaluate inputs, invoke, capture outputs, schedule, checkpoint, post-commit) as hardcoded sequential code.

This is the **same failure class** as the dead JS `IWorkflowExecutionContext` processors: types that advertise a capability that isn't wired. Here it is worse as a *false affordance* — a module can register an `IActivityRuntimeMiddleware` and it will **silently never run**, because no execution path consults the pipeline.

Scheduler vs pipeline is **not** an either/or. The scheduler (durable work-item queue + drain + checkpoint) is the correct dispatch layer and is conformant; the pipelines are the per-tick / per-activity **execution spine** the handler *should* invoke. The gap is that the step routing execution through the pipeline was never done, and handler-inlined execution filled the vacuum. Whether that is accidental drift or deliberately-deferred wiring (Slice 4 framed the slot contract as "define extension points before implementing behavior-heavy middleware") is an intent question for the steward — but either way it is **outstanding work, not a settled alternative design**.

## Reconciliation outcome

1. **Most of the "recommended first Speckit unit" is done.** The artifact + split-state + checkpoint + bookmark + bindings + recovery + Elsa-3-boundary contracts are implemented as intended. The action-plan/brainstorm reports are planning notes largely overtaken by merged code for the built portions and are marked historically-superseded there. **The exception is S4 (execution pipeline), which is scaffolded but unwired and remains genuinely outstanding** (see above).
2. **Remaining edge A — execution-time JS expression accessors (narrow unit).** The runtime has decisively converged on the parameter-carrier + durable-value model; the five `IWorkflowExecutionContext` processors are dead code and an unguarded resolution-throw landmine.
3. **Remaining edge B — route runtime execution through the workflow + activity pipelines (distinct, larger unit).** Reclassified from a "follow-up" to a first-class work unit: the pipeline contract is a false affordance until execution is wired through it. This blocks the module system from extending runtime execution (tracing, tenanting, authz, retry, custom persistence policy) and duplicates phase logic across handlers. Arguably more foundational than edge A. Sizing in progress (inlined-phase → slot mapping across the scheduler handlers).
4. **Minor:** the sensitive-value payload-capture default is unverified (S7) — confirm or specify.

## Decision framing for the next ADR (not decided here)

The central decision is no longer "construct/DI-register `IWorkflowExecutionContext` to revive dead code." It is:

- **D1. Single expression-state mechanism.** Ratify `IMaterializationExpressionState`-style parameter-carrying as *the* runtime expression-state mechanism (Option 2), consistent with what already works and with §E2.2/§E2.6 (no Design dependency at execution).
- **D2. Fate of the five processors + `IWorkflowExecutionContext`.** Retire, or re-point onto the carrier. Decide whether `IWorkflowExecutionContext`/`WorkflowExecutionContext` (unused in production) is deleted or kept.
- **D3. Keep-or-drop the missing accessor behaviors.** Named pascalized accessors, execution-time generic/identity functions, and JS-side variable write-back: port each onto the carrier, or drop with rationale. This is a feature-surface decision, not just a wiring fix.
- **D4. Guardrail.** Whatever the outcome, add a test that fails if the JS runtime feature's registered pre-processors cannot all be resolved — so the landmine can't return silently.

Gate impact: expected to stay within existing §E2.2 / §E2.6 / §E2.9 constraints (carrier is Runtime-owned, Design-free). If the team wants "runtime expression-state carrier is Design-free / parameter-threaded" pinned as a rule, route *that single item* through [Constitution Readiness](../program-goals/constitution-readiness.md); do not broaden.

## Open questions for the architect

- Are the named/identity/output execution-time JS accessors a required product surface, or can they be dropped now that generic accessors resolve at materialization time? (Drives D3 scope.)
- Should a runtime expression evaluation ever occur *outside* input materialization (e.g. mid-activity JS that needs a live, post-seed context), or is materialization-time the only evaluation point? (Determines whether a live per-execution context is needed at all.)
- Should `IExpressionExecutionContext` gain a general transient-properties carrier (elsa-core parity), or is the narrow `IMaterializationExpressionState` marker sufficient?

## Follow-up surface

- Edge A feeds a single ADR (`docs/adr/0029-…`) and, if approved, a narrow Speckit unit under `specs/` scoped to D1–D4 (retire/re-point + guardrail), *not* the already-built artifact/state contract.
- Edge B (pipeline wiring) is its own ADR + Speckit unit — "route runtime execution through the workflow + activity pipelines" — sized by the inlined-phase → slot mapping. Steward intent (is the pipeline the intended execution spine?) confirms this as unfinished-wiring work, not a settled alternative.
- Record the S4 unwired-pipeline finding and the S7 sensitive-value default in the Runtime Execution Seam bucket and [unfinished work](unfinished-work.md).
- The AGENTS.md SPECKIT pointer (still `specs/081-typed-argument-model/plan.md`; 081 is merged) should be corrected when the next unit is chosen.
