# Elsa Core Runtime Execution Layer Analysis

Status: source-backed analysis for execution-layer planning. This is not a design decision, Speckit spec, or implementation plan.

Program goal state: [Runtime Execution Seam](../program-goals/runtime-execution-seam.md).

Parent report: [Elsa Core runtime broken windows brainstorm](elsa-core-runtime-broken-windows-brainstorm.md), topics 4-7: execution pipeline shape, input evaluation, activity output lifecycle, and data-flow links.

Related decisions: [Elsa 4 runtime serialization brainstorm decisions](elsa-4-runtime-serialization-brainstorm-decisions.md).

## Inspection Scope

Elsa 3 source inspected from local checkout `/Users/sipke/Projects/Elsa/elsa-core`.

- Repository: `https://github.com/elsa-workflows/elsa-core.git`
- Branch: `release/3.8.0`
- Commit: `20c1064ca5ce705baba934cf77239b8db2ccdc56`
- Working tree note: the checkout had unrelated local changes in modular server/platform integration files. Runtime execution files referenced below were inspected read-only.

This report focuses on the workflow runner, workflow and activity pipelines, scheduling, activity execution identity, bookmarks and resume, state extraction and commit, incidents, recovery, execution logs, dispatch outbox behavior, and the implications for bringing Elsa 3 execution behavior into Elsa 4.

## Executive Finding

The better frame is confirmed: build Elsa 4's execution layer from clean contracts, using Elsa 3 as behavioral evidence.

Elsa 3 already contains valuable execution concepts worth preserving: a workflow pipeline, an activity pipeline, explicit scheduled work items, per-activity execution identity, persisted bookmarks, state extraction at commit boundaries, persisted diagnostics separate from workflow state, and operational recovery for interrupted executions.

But Elsa 3 should not be imported as-is. Its runtime execution boundary still depends on a materialized `WorkflowGraph` loaded from workflow definitions during start and resume, and persisted `WorkflowState` mixes durable runtime state, active call stacks, scheduler state, callbacks, input/output dictionaries, property bags, incidents, bookmarks, and operational markers. The core behavior is usable evidence; the current object model is too coupled to become Elsa 4's canonical runtime seam.

The maintainer concern about workflow/activity middleware complexity is confirmed, with one correction. The complexity is not the presence of two pipelines by itself. The real complexity is that behavior-critical stages are encoded as ordered middleware classes plus side-effecting context mutation, which makes the runtime harder to inspect, test, and customize safely. Elsa 4 can keep distinct workflow and activity pipelines if they have named semantic phases, clear contracts, and observable execution state.

## Elsa 3 Execution Map

Elsa 3 core registers a default workflow pipeline of exception handling plus activity scheduling, and a default activity pipeline of logging, exception handling, execution logging, notifications, and final activity invocation.

Source refs:

- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:57`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:64`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:155`
- `src/modules/Elsa.Workflows.Core/ShellFeatures/WorkflowsFeature.cs:181`

Runtime extends the workflow pipeline with operational concerns: execution cycle tracking, dispatch outbox, heartbeat, engine exception handling, persistent variables, and the default scheduler.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Extensions/WorkflowExecutionPipelineBuilderExtensions.cs:17`
- `src/modules/Elsa.Workflows.Runtime/Extensions/WorkflowExecutionPipelineBuilderExtensions.cs:31`
- `src/modules/Elsa.Workflows.Runtime/Extensions/WorkflowExecutionPipelineBuilderExtensions.cs:37`
- `src/modules/Elsa.Workflows.Runtime/Extensions/WorkflowExecutionPipelineBuilderExtensions.cs:45`

Runtime service registration spans local runtime/client, dispatchers, bookmark stores, queue stores, trigger stores, execution log stores, bookmark manager/resumer, workflow starter/restarter, commit-state handling, interrupted recovery, quiescence, drain, and dispatch outbox services.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/ShellFeatures/WorkflowRuntimeFeature.cs:55`
- `src/modules/Elsa.Workflows.Runtime/ShellFeatures/WorkflowRuntimeFeature.cs:85`
- `src/modules/Elsa.Workflows.Runtime/ShellFeatures/WorkflowRuntimeFeature.cs:123`
- `src/modules/Elsa.Workflows.Runtime/ShellFeatures/WorkflowRuntimeFeature.cs:143`
- `src/modules/Elsa.Workflows.Runtime/ShellFeatures/WorkflowRuntimeFeature.cs:210`
- `src/modules/Elsa.Workflows.Runtime/ShellFeatures/WorkflowRuntimeFeature.cs:232`
- `src/modules/Elsa.Workflows.Runtime/ShellFeatures/WorkflowRuntimeFeature.cs:297`

## Start And Resume

Starting a workflow through `DefaultWorkflowStarter` resolves a workflow graph from `IWorkflowDefinitionService`, then creates a runtime client request using a definition version handle, input, variables, trigger/activity handle, properties, and parent workflow ID.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Services/DefaultWorkflowStarter.cs:11`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultWorkflowStarter.cs:27`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultWorkflowStarter.cs:53`

`LocalWorkflowClient` creates workflow instances and runs existing instances. On run, it reloads the workflow graph from the stored definition version ID, creates run options, and calls `IWorkflowRunner.RunAsync(workflowGraph, workflowState, options)`.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Services/LocalWorkflowClient.cs:32`
- `src/modules/Elsa.Workflows.Runtime/Services/LocalWorkflowClient.cs:59`
- `src/modules/Elsa.Workflows.Runtime/Services/LocalWorkflowClient.cs:135`
- `src/modules/Elsa.Workflows.Runtime/Services/LocalWorkflowClient.cs:165`
- `src/modules/Elsa.Workflows.Runtime/Services/LocalWorkflowClient.cs:179`
- `src/modules/Elsa.Workflows.Runtime/Services/LocalWorkflowClient.cs:209`

This is the most important boundary issue for Elsa 4. Elsa 3 runtime execution is not definition-free: even resuming a persisted instance loads a `WorkflowGraph` from the management definition service. Elsa 4 should not inherit that coupling. It should execute a runtime-owned executable artifact and separately support importing/migrating Elsa 3 authored definitions.

## Workflow Runner

`WorkflowRunner` has two main paths:

- New execution: create a `WorkflowExecutionContext`, schedule the workflow root, then run the workflow context.
- Resumed execution: create a `WorkflowExecutionContext` from `WorkflowState`, restore state, schedule a bookmark/activity handle/interrupted contexts/existing scheduler/workflow root, then run.

Source refs:

- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:72`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:119`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:157`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:174`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:214`

The runner sends lifecycle notifications, transitions status to executing, invokes the workflow pipeline, extracts workflow state, sends finish/executed notifications, commits state, and returns `RunWorkflowResult`.

Source refs:

- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:222`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:229`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:234`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:239`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowRunner.cs:245`

Elsa 4 implication: preserve the high-level lifecycle, but make "run executable artifact with runtime state" the primary contract. Definition resolution, authored document migration, and compile/publish should sit outside this execution contract.

## Workflow Scheduling

`DefaultActivitySchedulerMiddleware` owns the main loop. It transitions a pending workflow to executing, optionally commits at `WorkflowExecuting`, then repeatedly dequeues scheduled activity work items and invokes activities until the scheduler is empty or cancellation is requested. If all activities completed, it finishes the workflow; otherwise the workflow suspends.

Source refs:

- `src/modules/Elsa.Workflows.Core/Middleware/Workflows/DefaultActivitySchedulerMiddleware.cs:27`
- `src/modules/Elsa.Workflows.Core/Middleware/Workflows/DefaultActivitySchedulerMiddleware.cs:35`
- `src/modules/Elsa.Workflows.Core/Middleware/Workflows/DefaultActivitySchedulerMiddleware.cs:39`
- `src/modules/Elsa.Workflows.Core/Middleware/Workflows/DefaultActivitySchedulerMiddleware.cs:44`
- `src/modules/Elsa.Workflows.Core/Middleware/Workflows/DefaultActivitySchedulerMiddleware.cs:50`

Scheduling helpers can schedule the workflow root, an activity, an existing activity execution context, or a bookmark resume. Bookmark resume locates the existing activity execution context by bookmark activity-instance ID and assigns a delegate for callback/autocomplete/noop behavior.

Source refs:

- `src/modules/Elsa.Workflows.Core/Extensions/WorkflowExecutionContextExtensions.cs:20`
- `src/modules/Elsa.Workflows.Core/Extensions/WorkflowExecutionContextExtensions.cs:53`
- `src/modules/Elsa.Workflows.Core/Extensions/WorkflowExecutionContextExtensions.cs:63`
- `src/modules/Elsa.Workflows.Core/Extensions/WorkflowExecutionContextExtensions.cs:78`
- `src/modules/Elsa.Workflows.Core/Extensions/WorkflowExecutionContextExtensions.cs:130`

Elsa 4 implication: scheduled work items are a good concept to preserve, but scheduling should reference executable nodes and activity executions, not design-time activity objects.

## Activity Execution

Elsa 3 calls the per-activity runtime object `ActivityExecutionContext`. For Elsa 4 planning, use the term `ActivityExecution`.

`ActivityInvoker` either reuses an existing activity execution context or creates a new one, marks it dirty, adds it to the workflow context, then invokes the activity pipeline.

Source refs:

- `src/modules/Elsa.Workflows.Core/Services/ActivityInvoker.cs:16`
- `src/modules/Elsa.Workflows.Core/Services/ActivityInvoker.cs:26`
- `src/modules/Elsa.Workflows.Core/Services/ActivityInvoker.cs:32`
- `src/modules/Elsa.Workflows.Core/Services/ActivityInvoker.cs:43`

The default activity invoker middleware checks cancellation, evaluates input properties, checks `CanExecuteAsync`, enters execution, optionally commits at `ActivityExecuting`, transitions to running, executes the activity, handles completion, burns resumed bookmarks, increments execution count, sends completion notifications, and optionally commits at `ActivityExecuted`.

Source refs:

- `src/modules/Elsa.Workflows.Core/Middleware/Activities/DefaultActivityInvokerMiddleware.cs:32`
- `src/modules/Elsa.Workflows.Core/Middleware/Activities/DefaultActivityInvokerMiddleware.cs:49`
- `src/modules/Elsa.Workflows.Core/Middleware/Activities/DefaultActivityInvokerMiddleware.cs:57`
- `src/modules/Elsa.Workflows.Core/Middleware/Activities/DefaultActivityInvokerMiddleware.cs:70`
- `src/modules/Elsa.Workflows.Core/Middleware/Activities/DefaultActivityInvokerMiddleware.cs:91`
- `src/modules/Elsa.Workflows.Core/Middleware/Activities/DefaultActivityInvokerMiddleware.cs:128`

`ActivityExecutionContext` carries the durable identity and runtime linkage that Elsa 4 likely needs in cleaner form: ID, timestamps, status, parent/scheduling IDs, workflow IDs, call-stack depth, variables, metadata, properties, transient properties, journal data, activity state, scheduled work, bookmarks, exception, and fault counts.

Source refs:

- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:32`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:63`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:86`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:113`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:154`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:240`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:315`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:402`

Elsa 4 implication: `ActivityExecution` should be a first-class runtime concept, not just an implementation context. The minimum durable identity likely includes execution ID, executable node ID, activity ID/type snapshot, workflow instance ID, scheduling activity execution ID, parent/owner execution ID when relevant, call-stack/branch identity, status, timestamps, and fault summary.

## Input Evaluation And Activity State

Elsa 3 evaluates activity inputs before execution. Evaluation uses expression evaluators and memory references, then stores values in memory and often in `ActivityState` unless the input is sensitive or marked non-serializable.

Source refs:

- `src/modules/Elsa.Workflows.Core/Middleware/Activities/DefaultActivityInvokerMiddleware.cs:113`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:20`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:64`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:85`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.InputEvaluation.cs:144`

This confirms the earlier serialization brainstorm concern: evaluated inputs in Elsa 3 are not cleanly separated from historical/loggable activity state. They can become persisted in activity execution records through log persistence.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:22`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:25`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:31`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:57`

Elsa 4 implication: preserve expression-based input binding behavior, but keep input bindings durable and evaluated input values ephemeral unless a declared durable value, audit policy, or explicit checkpoint policy captures them.

## Activity Outputs

Elsa 3 records activity outputs in an in-memory `ActivityOutputRegister` stored on workflow transient properties. It can record outputs by activity ID/output name and by activity execution context ID/output name. Lookup by activity ID returns the last matching record, which is ambiguous in loops and parallelism; lookup by activity execution ID is precise.

Source refs:

- `src/modules/Elsa.Workflows.Core/Contexts/WorkflowExecutionContext.cs:640`
- `src/modules/Elsa.Workflows.Core/Contexts/WorkflowExecutionContext.cs:650`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:8`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:21`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:65`
- `src/modules/Elsa.Workflows.Core/Models/ActivityOutputRegister.cs:91`

Completed activity execution contexts are cleared from the workflow context. A source comment notes that scripts directly accessing activity output can break, and capturing the output into variables is the workaround.

Source refs:

- `src/modules/Elsa.Workflows.Core/Contexts/WorkflowExecutionContext.cs:668`
- `src/modules/Elsa.Workflows.Core/Contexts/WorkflowExecutionContext.cs:679`

Activity outputs can also appear in activity execution records/log history if log persistence includes them, but that is diagnostic persistence, not durable runtime value state.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:22`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:26`
- `src/modules/Elsa.Workflows.Runtime/Entities/ActivityExecutionRecord.cs:54`
- `src/modules/Elsa.Workflows.Runtime/Services/StoreActivityExecutionLogSink.cs:17`

Elsa 4 implication: this confirms the locked decision. Raw activity outputs can be execution-local. If an output must cross suspension, branch boundaries, or uncertain execution scopes, it should be captured into a declared durable value. Data links can compile to bindings over active execution scope, but ambiguous output references need execution identity.

## Workflow State And Checkpoints

`WorkflowState` is broad. It stores identity/status, `IsExecuting`, bookmarks, incidents, completion callbacks, active activity execution context states, scheduled activity states, execution log sequence, workflow input, workflow output, properties, and timestamps.

Source refs:

- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:9`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:49`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:59`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:64`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:70`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:80`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:86`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:91`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:96`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:101`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:106`
- `src/modules/Elsa.Workflows.Core/State/WorkflowState.cs:111`

`WorkflowStateExtractor` extracts state from `WorkflowExecutionContext`, selectively persists workflow input, output, properties, active activity execution contexts, callbacks, and scheduled activities. It also applies persisted state back onto a workflow execution context during resume.

Source refs:

- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:13`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:47`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:69`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:105`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:190`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:226`
- `src/modules/Elsa.Workflows.Core/Services/WorkflowStateExtractor.cs:265`

`DefaultCommitStateHandler` is the practical checkpoint boundary. It extracts workflow state, persists bookmarks, activity execution logs, workflow execution logs, variables, the workflow instance, clears in-memory execution logs, clears completed activity execution contexts, executes deferred tasks, and sends `WorkflowStateCommitted`.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Services/DefaultCommitStateHandler.cs:19`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultCommitStateHandler.cs:25`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultCommitStateHandler.cs:31`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultCommitStateHandler.cs:35`

Elsa 4 implication: checkpointing should be explicit in the execution model. Do not let commit behavior be an incidental side effect of middleware conventions. Separate durable runtime state, scheduler state, durable values, bookmarks, diagnostics, incidents, and outbox markers.

## Bookmarks And Resume

Activities create bookmarks with activity ID, node ID, activity execution context ID, callback method name, auto-burn, auto-complete, hash, metadata, and payload. Bookmarks can be cleared for an activity.

Source refs:

- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:402`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:426`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:471`
- `src/modules/Elsa.Workflows.Core/Contexts/ActivityExecutionContext.cs:537`

`WorkflowResumer` translates activity/stimulus hashes or bookmark IDs into bookmark filters, locks the filter, loads matching bookmarks, creates workflow clients by workflow instance ID, and dispatches `RunInstanceAsync` with the bookmark ID and input.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Services/WorkflowResumer.cs:23`
- `src/modules/Elsa.Workflows.Runtime/Services/WorkflowResumer.cs:43`
- `src/modules/Elsa.Workflows.Runtime/Services/WorkflowResumer.cs:70`
- `src/modules/Elsa.Workflows.Runtime/Services/WorkflowResumer.cs:87`
- `src/modules/Elsa.Workflows.Runtime/Services/WorkflowResumer.cs:113`

Elsa 4 implication: keep bookmarks as durable resume handles, but do not require callback method names on activity classes to be the durable resume contract. A compiled executable artifact should provide stable resume targets or handlers that can be versioned and validated.

## Incidents, Faults, And Recovery

Activity exception handling calls `context.Fault(e)` and resolves an incident strategy. `Fault` transitions the activity to faulted, creates an incident from activity identity/type/message/exception/timestamp, records it on the workflow, and increments fault counts on the execution and ancestors.

Source refs:

- `src/modules/Elsa.Workflows.Core/Middleware/Activities/ExceptionHandlingMiddleware.cs:25`
- `src/modules/Elsa.Workflows.Core/Middleware/Activities/ExceptionHandlingMiddleware.cs:39`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.cs:317`
- `src/modules/Elsa.Workflows.Core/Extensions/ActivityExecutionContextExtensions.cs:326`

Incident strategy can fault the workflow or continue with incidents. The default resolver chooses workflow-level strategy first, then app options, then the default fault strategy.

Source refs:

- `src/modules/Elsa.Workflows.Core/Services/DefaultIncidentStrategyResolver.cs:24`
- `src/modules/Elsa.Workflows.Core/Services/DefaultIncidentStrategyResolver.cs:38`
- `src/modules/Elsa.Workflows.Core/IncidentStrategies/FaultStrategy.cs:15`
- `src/modules/Elsa.Workflows.Core/IncidentStrategies/ContinueWithIncidentsStrategy.cs:15`

Workflow exception middleware turns workflow-level exceptions into workflow incidents and faults the workflow. Runtime engine exception handling logs execution/incident data but intentionally does not change workflow state.

Source refs:

- `src/modules/Elsa.Workflows.Core/Middleware/Workflows/ExceptionHandlingMiddleware.cs:40`
- `src/modules/Elsa.Workflows.Core/Middleware/Workflows/ExceptionHandlingMiddleware.cs:52`
- `src/modules/Elsa.Workflows.Core/Middleware/Workflows/EngineExceptionHandlingMiddleware.cs:26`
- `src/modules/Elsa.Workflows.Core/Middleware/Workflows/EngineExceptionHandlingMiddleware.cs:37`

Interrupted recovery is operational. `RestartInterruptedWorkflowsTask` finds stale running instances where `IsExecuting` is true and restarts them. `InterruptedRecoveryScanner` finds instances with `WorkflowSubStatus.Interrupted` and requeues them. `DefaultWorkflowRestarter` redispatches a workflow instance request.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Tasks/RestartInterruptedWorkflowsTask.cs:25`
- `src/modules/Elsa.Workflows.Runtime/Tasks/RestartInterruptedWorkflowsTask.cs:58`
- `src/modules/Elsa.Workflows.Runtime/Services/InterruptedRecoveryScanner.cs:40`
- `src/modules/Elsa.Workflows.Runtime/Services/InterruptedRecoveryScanner.cs:42`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultWorkflowRestarter.cs:10`

Elsa 4 implication: distinguish domain failure/incident handling from host-level recovery and requeue. Operational recovery should not be modeled as activity retry unless an activity or policy explicitly requests retry semantics.

## Diagnostics And Execution Logs

Activity execution records are stored separately from workflow instance state. The mapper captures activity identity, node ID, workflow instance ID, type/version/name, selected activity state, outputs, properties, metadata, exception, status, fault count, timestamps, scheduling IDs, workflow ID, and call-stack depth. It serializes snapshots using safe/payload serializers and compression.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Entities/ActivityExecutionRecord.cs:9`
- `src/modules/Elsa.Workflows.Runtime/Entities/ActivityExecutionRecord.cs:44`
- `src/modules/Elsa.Workflows.Runtime/Entities/ActivityExecutionRecord.cs:54`
- `src/modules/Elsa.Workflows.Runtime/Entities/ActivityExecutionRecord.cs:75`
- `src/modules/Elsa.Workflows.Runtime/Entities/ActivityExecutionRecord.cs:100`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:20`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:31`
- `src/modules/Elsa.Workflows.Runtime/Services/DefaultActivityExecutionMapper.cs:63`

`StoreActivityExecutionLogSink` persists only dirty activity execution contexts and clears their dirty flag. `StoreWorkflowExecutionLogSink` extracts workflow execution log records and stores them separately.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Services/StoreActivityExecutionLogSink.cs:17`
- `src/modules/Elsa.Workflows.Runtime/Services/StoreActivityExecutionLogSink.cs:19`
- `src/modules/Elsa.Workflows.Runtime/Services/StoreActivityExecutionLogSink.cs:25`
- `src/modules/Elsa.Workflows.Runtime/Services/StoreWorkflowExecutionLogSink.cs:14`
- `src/modules/Elsa.Workflows.Runtime/Services/WorkflowExecutionLogRecordExtractor.cs:9`

Elsa 4 implication: diagnostics and audit/history should remain separate from executable runtime state. Activity execution history can persist selected input/output snapshots, but those snapshots should not become the mechanism by which downstream runtime execution reads values.

## Dispatch Outbox

Elsa 3 has a transactional workflow dispatch outbox for effects requested during workflow execution. `TransactionalWorkflowDispatcher` writes dispatch commands to the outbox when there is a current workflow execution context. `WorkflowDispatchOutbox` saves the item and records its ID in the workflow state's property bag. After `WorkflowStateCommitted`, a handler processes the outbox. The processor verifies the owner workflow committed the outbox marker before delivery.

Source refs:

- `src/modules/Elsa.Workflows.Runtime/Services/TransactionalWorkflowDispatcher.cs:18`
- `src/modules/Elsa.Workflows.Runtime/Services/TransactionalWorkflowDispatcher.cs:49`
- `src/modules/Elsa.Workflows.Runtime/Services/WorkflowDispatchOutbox.cs:13`
- `src/modules/Elsa.Workflows.Runtime/Extensions/WorkflowDispatchOutboxStateExtensions.cs:20`
- `src/modules/Elsa.Workflows.Runtime/Handlers/ProcessWorkflowDispatchOutbox.cs:13`
- `src/modules/Elsa.Workflows.Runtime/Services/WorkflowDispatchOutboxProcessor.cs:116`
- `src/modules/Elsa.Workflows.Runtime/Services/WorkflowDispatchOutboxProcessor.cs:126`

Elsa 4 implication: this is an important reliability pattern to preserve conceptually. It should be modeled as post-commit side-effect delivery, not as ordinary workflow variable/state persistence.

## Compatibility Risks

- Elsa 3 persisted `WorkflowState` shape is broad and object-heavy. Elsa 4 should not promise transparent resume of arbitrary Elsa 3 instances unless a dedicated migration/runtime-compatibility project is approved.
- Existing Elsa 3 instances may depend on activity callback method names in bookmarks. Moving to compiled resume targets needs migration rules for definitions and a clear incompatibility boundary for live instances.
- Existing users may observe or query `ActivityExecutionRecord` shapes. Elsa 4 can keep a history/audit concept but should not treat Elsa 3 log records as canonical runtime state.
- Elsa 3 direct activity-output access can be ambiguous and can break after completed contexts are cleared. Elsa 4 should preserve migration guidance: capture values that must survive scope/suspension into declared durable values.
- Elsa 3 dispatch outbox stores markers in the workflow state property bag. Elsa 4 should preserve the reliability guarantee but can choose a cleaner state/outbox contract.
- Elsa 3 runtime reloads workflow definitions on resume. Elsa 4 executable artifact versioning must decide whether a running instance resumes against the exact compiled artifact snapshot, a compatible patched artifact, or a migrated artifact.

## Confirmed Or Corrected Concerns

Confirmed:

- Workflow and activity middleware are behavior-critical and difficult to reason about as ordered linked middleware alone.
- Input evaluation and activity state persistence are intertwined.
- Activity output is execution-local unless captured into durable state, despite diagnostic/history persistence being possible.
- Direct output references by activity ID are ambiguous under loops and parallelism.
- Runtime start/resume crosses into definition/graph services, which is not the desired Elsa 4 runtime seam.

Corrected:

- Two pipelines are not inherently wrong. Workflow orchestration and activity execution have different context and lifecycle needs. The problem to fix is unclear semantic ordering and implicit mutation, not simply "two pipelines".
- Activity output is not absent from persistence everywhere. It can be persisted to activity execution history, but that history is not a safe runtime value source.
- Interrupted recovery is not equivalent to activity retry. It is host/runtime requeue of workflow instances.

## Clarification Questions

- Should Elsa 4 explicitly refuse live Elsa 3 instance resume and only support definition/document migration, or do we need a separate long-tail compatibility runtime?
- Should every workflow instance pin to a compiled executable artifact snapshot, or can it resolve a compatible newer artifact on resume?
- What is the minimum durable `ActivityExecution` identity model Elsa 4 needs for loops, parallelism, workflow-as-activity, and audit queries?
- Should bookmark resume targets be generated stable IDs, compiled handler names, activity node slots, or another artifact-level contract?
- Which commit boundaries must be first-class: workflow start, activity executing, activity executed, suspension, bookmark creation, output capture, incident creation, dispatch outbox enqueue, and workflow finish?
- Should execution history be optional, always-on with retention, or controlled per workflow/activity/output by policies?
- How much of Elsa 3's dispatch outbox behavior is runtime-core versus host/operations module?

## Design-Option Areas For The Brainstorm

- Executable artifact contract: define what the runtime consumes instead of `WorkflowGraph` and what identity/version data is pinned into workflow instances.
- Workflow execution state model: split durable runtime state, scheduler queue, active activity executions, bookmarks, incidents, values, and outbox markers.
- `ActivityExecution` model: choose stable IDs, parent/scheduling relationships, branch/iteration identity, timestamps, status, fault summary, and history projection.
- Pipeline phases: keep workflow and activity pipelines, but define named slots such as ingress, load/resume, evaluate bindings, before activity execute, invoke, after activity execute, scheduler advance, checkpoint, diagnostics, and post-commit.
- Checkpoint contract: make commit boundaries explicit and decide what each boundary may persist.
- Bookmark contract: replace callback-method persistence with executable-artifact resume targets.
- Output/value model: keep raw outputs scoped to active execution; require declared durable values for cross-suspension and ambiguous scopes.
- Diagnostics model: project runtime events into audit/history records without making history a runtime dependency.
- Recovery model: separate incidents, domain retries, host cancellation, interrupted requeue, and drain/quiescence.
- Post-commit effects: preserve outbox-style reliability for workflow dispatch and similar runtime effects.

## Suggested Next Step

Use this report to begin the Elsa 4 execution-layer plan with a narrow first slice:

1. Define the runtime-owned executable artifact boundary.
2. Define `WorkflowExecution` and `ActivityExecution` durable identity/state.
3. Define explicit checkpoint and bookmark contracts.
4. Define workflow and activity pipeline phases as extension slots.
5. Map Elsa 3 behavior to Elsa 4 compatibility decisions, especially definition migration versus live instance resume.
