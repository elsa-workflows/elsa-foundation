# Research: Step workflow state

Inspected initiative baseline: `18dd3aefb98bb0d3c8916ad6b71be69a9fc3699f`. This includes merged S-7 part 2 source `2e1d3b958a4121360d2dd4694bb00980f60e1f1e` and Jint recursion repair `487e10f2ddf01a2db06297b51b8267e37da75097`. This is source research, not an execution receipt.

## R1 — Extend the accepted transaction

**Decision**: use the existing inline leaf session lifetime and checkpoint preparation, not a second unit of work.

`Essentials/src/Activities/Runtime/Elsa.Activities.Runtime/Services/StepVariables/StepVariableSessions.cs` creates a fresh session per leaf invoke/resume and excludes structural activities. `StepVariableCommit.cs` prepares immutable workflow/container-frame changes without persisting. `WorkflowInvokeActivitySchedulerWorkHandler.cs` applies writes on suspension/completion, then projects captures. `WorkflowResumeBookmarkSchedulerWorkHandler.cs` mirrors those boundaries. Fault/cancel/disposal/projection refusal discard successful staged changes.

**Rationale**: ADR 0090 A-3 requires atomic effects per evaluation, and ADR 0032 determines when the resulting checkpoint becomes durable. Changing persistence cadence per setter would create a competing model. The current modes are Durable, Balanced and Ephemeral under ADR 0080 D2; Immediate/Coalesced in older ADR 0032 prose are historical. Preserve Ephemeral's RecordCompletion exception for never-persisted runs. Combining output and identity changes with S-7's workflow record must preserve its root-frame update instead of taking one record in preference to the other.

**Alternatives considered**: direct store writes leak partial state; returning a second state transaction fails atomicity. General structural/background sessions would widen S-7's explicitly bounded scope and are unnecessary for the requested script conversion. Retain those unsupported placements explicitly; successful suspension remains a commit, not rollback.

## R2 — Reuse existing state slots and semantics

**Decision**: prepare existing intrinsic-shaped changes for output and identity.

`Essentials/src/Workflows/Runtime/Elsa.Workflows.Runtime/Services/Values/WorkflowIntrinsicExecutor.cs` contains `ExecuteSetOutputAsync`, `ExecuteIdentityEffectAsync`, `NewWorkflowOutputChange`, `ReadOptionalString` and `ApplyInstanceName`. Output writes use `output:<name>` / `durable-output:<name>`, carry type/policy/protection metadata and refuse withheld payloads. Identity writes combine header fields with `RuntimeWorkflowStateSeed.BuildIdentityChanges` durable projections; whitespace normalizes to null. The output bag does not enforce a declared-output-only rule. `RuntimeWorkflowOutputStateProjection.cs` supplies existing output inspection.

**Output envelope choice**: use Any/Single for JSON assignments. New unprotected slots default to InstanceInline; replacement keeps prior destination policy/storage minima, combines source protection through ValuePolicyCombiner, and uses the required existing encoder or refuses incompatible/unavailable encoding. A prior type is not a declared output schema. This is an explicit choice for S-13: the current intrinsic helper consumes the incoming envelope and does not preserve old-slot policy automatically.

**Rationale**: one authoritative header/output channel avoids inspection/materialization drift. Reuse policy-aware intrinsic preparation rather than the simpler seed-output helper. Keep namespace distinctions: `RuntimeOutputCaptureCompiler.cs` emits `variable:*` / `graph-variable:*`, whereas workflow outputs use `output:*`. Same-name slots do not overlap. Deduplicate by physical target, not display label or timestamp ties.

**Alternative**: a separate dictionary on workflow state would duplicate durable outputs and require a new persistence/projection/recovery model.

## R3 — Correlation belongs to the workflow; wait keys belong to waits

**Decision**: change header and identity projection only, producing no bookmark mutation.

`Essentials/src/Activities/Primitives/Activities/Event.cs` captures its authored `CorrelationId` input in registration metadata. `Essentials/src/Workflows/Runtime/Core/Models/GlobalBookmarkStimulusLookupModels.cs` matches retained bookmark correlation metadata. `WorkflowCreateBookmarkSchedulerWorkHandler.cs` copies registration/payload metadata into the bookmark. These do not dynamically re-read current workflow correlation.

**Rationale**: this directly implements ADR 0090 A-3's no-rebinding rule. A later Event sees a changed workflow key only if its own input binds it. Do not introduce automatic correlation inheritance as a side effect of S-13.

## R4 — Recovery includes state-only effects

**Decision**: extend successful-commit and recorded-completion eligibility consistently for variable, output and identity effects.

`WorkflowInvokeActivitySchedulerWorkHandler.cs` currently suppresses `RecordedCompletion` in a capture-conflict path when variable-session records exist: replaying only the captures would omit those writes. The analogous S-13 predicate must include output/identity-only intents. Audit every recorded completion, reschedule and operator acceptance path, including resume, not only the happy completion.

`Essentials/src/Workflows/Runtime/Core/Models/RuntimeCheckpointCommit.cs` already combines workflow, activity, bookmark and durable-value changes. `Persistence/EntityFrameworkCore/Stores/EfRuntimeCheckpointCommitStore.cs` stages them with the replay marker in one provider transaction. Existing coalescing stores retain working-state versus durable-flush distinctions.

**Alternative**: automatically accepting a captured result despite missing state effects violates the accepted transaction even if the final result/captures look correct. Prefer disabling partial acceptance to inventing new persistence semantics.

## R5 — Narrow author capabilities and body reads

**Decision**: `context.WorkflowState` / `workflowState` expose only output JSON get/set and identity get/set; explicit native declaration and pinned script effects enforce placement.

`Essentials/src/Activities/Scripting/Activities/RunJavaScript.cs` already passes a narrow variable adapter through `Expressions/JavaScript/Core/Contracts/IJavaScriptScriptEvaluator.cs`; Jint's script evaluator installs body surfaces separately from ordinary expression evaluation. Follow that path. `BackgroundEligibility.cs` recognizes existing variable signals only: add an independent workflow-state signal and test publish plus runtime rejection. A blanket marker on every script must not erase support for known graph-safe read-only scripts; analyze literal bodies, conservatively classify unavailable bodies, and install a refusing runtime capability for unsupported use where needed.

**Rationale**: no arbitrary context proxy/.NET interop is needed. Existing `workflow.*` expression reads stay pure/pinned. A dedicated body root avoids silently changing that contract. Methods retain setter `undefined` returns and allow missing/null output reads to remain distinct. The CLR contract takes explicit JSON, preserving precision without introducing CLR type inference or another value system.

**Alternative**: output/member mutation tracking would duplicate S-8's complex variable bridge without an accepted requirement. Output changes are explicit assignments only. New per-slot version/conflict semantics are not needed under serialized inline evaluation and are not added.

## R6 — Converter lowering and source behavior

**Decision**: extend `Extensions/Elsa3/src/Mapping/Services/Elsa3ExpressionRewriter.cs` using its existing script mode, lexical scopes, direct-call checks, root collision checks and no-result setter handling. Lower all supported same-body correlation/name getters to session reads; input expressions retain current behavior.

Current conversion refuses these three setters and `setInput`/`setResult`. Correlation reads lower to `args.workflowCorrelationId` through a `WorkflowInstanceExpressionParameterBinding`; that would be stale after a same-body write. `setVariable` conversion already demonstrates exactly-once argument traversal and no-result semantics. S-7's final computed setter handling and native Jint sequencing are the baseline; do not regress short-circuit/optional access behavior.

Elsa 3's [common functions](https://github.com/elsa-workflows/elsa-core/blob/main/core/src/modules/Elsa.Expressions.JavaScript/Handlers/ConfigureEngineWithCommonFunctions.cs) were read at blob `1a3face4a290788131391566a4f3327e24b85967`. Its correlation/name setters are void actions and getters read current execution state. The [execution context](https://github.com/elsa-workflows/elsa-core/blob/4fdb8296858bdd22144a47d8cd91063b43c40304/core/src/modules/Elsa.Workflows.Core/Contexts/WorkflowExecutionContext.cs) has ordinary string auto-properties: source blank strings remain blank, unlike native intrinsic normalization. Refuse literal normalization-changing imports and emit a once-evaluated sticky requireExactIdentity guard for computed values. A warning cannot make that behavior change faithful. Literal import refusal uses ELS3-STATE-IDENTITY-NORMALIZATION in Mapping; the generic runtime guard uses VF-STATE-005 with native guidance so the import boundary remains intact. Its [Run JavaScript body](https://github.com/elsa-workflows/elsa-core/blob/main/core/src/modules/Elsa.Expressions.JavaScript/Activities/RunJavaScript/RunJavaScript.cs) keeps result and outcome channels separate. Current upstream common functions do not declare `setOutput`; S-13 implements the workflow-output helper explicitly named by the accepted initiative contract, without claiming upstream provides arbitrary activity-output writes. Fixture acceptance must target that named compatibility surface rather than infer new helper overloads.

**Alternative**: lowering to separate intrinsic activities reorders nested calls/branches and breaks atomicity. Leaving correlation reads pinned violates read-your-writes. Blanket name replacement breaks local functions, escaped helper values and shadowed roots. Preserve stable refusal and actionable guidance for unsupported forms.

## R7 — Reusable/graph overlap

**Decision**: retain graph/reusable state-write refusals; do not claim graph intrinsic delivery.

`Elsa3ReusableOutputWriteRewrite.cs` explains why an Elsa 3 reusable output maps to a synthesized graph variable/output mapping rather than the root workflow output bag. `Elsa3ExpressionRewriter.cs` and `Elsa3ReusableVariableWriteAnalyzer.cs` retain S-7's graph body restrictions. `WorkflowIntrinsicExecutor.ResolveVisibleFrame` also refuses closed graph variable writes. New state capability placement checks must respect that boundary rather than leak root output/identity through it.

Graph intrinsic compilation/runtime and reusable caller-output acceptance remain separate work. Delivery forum pointers must link the existing graph issue without claiming its broader acceptance criteria complete.

## R8 — Protection and presentation

**Decision**: reuse existing precision/storage/capture policies, conservatively propagate protected reads, and refuse tainted writes to public identity/output names. Keep sticky host refusal and value-free diagnostics.

S-7's `StepScriptVariables` / `RunJavaScript` already keep caught host refusals fatal and mask exceptions that could quote protected data. Output projection contains type/policy metadata; identity fields have no protected envelope. Enforce protection at final preparation too, since a protected read can happen after a state assignment. No new declassification policy or payload-history store is needed.

Show authored effects in `Studio/.../ActivityPropertiesPanel.tsx` and resulting state through existing instance inspection surfaces (`workflowInstanceInspection.ts`, `WorkflowInstanceTimeline.tsx`, `workflow-editor/WorkflowInstanceInspectionFacts.tsx`). Concrete UI placement should follow the existing design and shared primitives. Composed host evidence and independent visual acceptance are part of implementation, not satisfied by a plan or an author-written receipt.

## Research outcome

The accepted ADR contract can express the requested bounded behavior. There is no proposed ADR amendment. The exact surface, refusal scope and validation obligations are ready for coordinator spec approval. This research does not approve implementation, complete the coordinator's resolved-base gate, or claim runtime/UX/CI execution.
