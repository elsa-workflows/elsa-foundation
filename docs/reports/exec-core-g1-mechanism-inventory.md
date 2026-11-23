# G1 common-mechanism inventory (native engine adapter vs BPMN adapter)

Point-in-time source inventory for execution-core slice 7 / goal §G1.a. Docs only. No extraction, no new abstraction, no production or test change.

**Main SHA:** `0944325db06b2e0376a6109be6cf430d2b3e09c9` (`origin/main` at inventory; contains G3 #4044). All `file:line` citations are on this SHA.

**Method:** read-only search of `Essentials/src` and `Essentials/tests` plus specs/ADRs named below. No test re-run. Publish/save/source-reference minting is **not** re-inventoried; see [publication enrollment evidence](https://github.com/elsa-workflows/elsa-foundation/blob/0944325db06b2e0376a6109be6cf430d2b3e09c9/docs/reports/native-bpmn-migration-and-rollback.md) and the R3 dependency report on `60d28493` (native unfenced `SaveAsync` vs BPMN `SaveWithRootWriteLeasesAsync`). That difference stays documented and is not a G1 candidate.

## Terminology used here

Goal §G1 asks for mechanisms shared by “the native engine adapter and the BPMN adapter”. Spec 1017’s plan also calls `Elsa.Workflows.Runtime` a “native adapter”. Those two uses of “adapter” are not the same. This inventory follows the **9 November owner clarification** in `docs/program-goals/unified-execution.md`: the shared abstraction is a small set of **ports**, not a generic kernel, and native/BPMN keep model-owned semantics.

| Term | What it names on this SHA | Not |
|---|---|---|
| **Shared host / ports** | `Elsa.Workflows.Runtime.Core` contracts named in that clarification (`IRuntimeExecutionProtocolDriver` / `IRuntimeExecutionProtocolSource`, `IRuntimeCheckpointCommitStore` + fence via `IRuntimeExecutionOwnershipService`, `IBookmarkResumeDispatcher`, `IRuntimeCheckpointCommitEnricher` / `IRuntimePostCommitIntentHandler`), plus the default implementations in `Elsa.Workflows.Runtime` (`RuntimeExecutionLeaseRunner`, `RuntimeCheckpointCommitter`, `WorkflowStartDispatcher`, `BookmarkResumeDispatcher`, scheduler/drain/cancel handler). | A fully generic kernel. ETL/Rules/Agents. |
| **Native engine adapter** | Sequence / Flowchart / `DispatchWorkflow` / `Elsa.Activities.Runtime`, and the **non-model** path of `RuntimeExecutionProtocolRouter` (`WorkflowSchedulerCommandRouter`). Native execution identity is `WorkflowExecution`. | The entire Runtime package. Direct BPMN. |
| **BPMN adapter** | `Elsa.Activities.Bpmn`: composite `BpmnProcess` / `BpmnProcessRuntime`; direct model `BpmnDirectExecutionDriver` (`IRuntimeExecutionProtocolDriver`); `RunBpmn` via `BpmnEmbeddingBoundary` / `BpmnEmbeddingDelivery` / `BpmnModelActivityRuntime`; `serviceTask` via `BpmnNativeWorkBoundary` / `BpmnNativeWorkDelivery`. | Native Sequence/Flowchart. DispatchWorkflow itself (that is native child dispatch that BPMN *binds*). |

BPMN has **two execution planes** (cross-model evidence map A.1). They are not one kernel:

1. **Composite:** `BpmnProcess` inside a native `WorkflowExecution`. `callActivity` binds `Elsa.DispatchWorkflow`. Same-execution native leaves via `ScheduleChildActivity`.
2. **Direct / `RunBpmn`:** separate `RuntimeModelExecution`. Direct publish **rejects** `CallActivity`. Native work is `serviceTask` → `StartNativeWork.v1`.

On this SHA there is **one** production `IRuntimeExecutionProtocolDriver`: `BpmnDirectExecutionDriver`. Native execution does not implement that port; it stays on the activity/scheduler path. The owner record that “ports lock after a second real protocol driver” is unchanged (Decisions withdrawn).

If a later reading collapsed “native adapter” with the whole Runtime package, the adapter-level rows below (DispatchWorkflow vs embedding vs native-work) would still stand. Only the “already in the shared host” rows would need re-labelling.

## Classification vocabulary

| Label | Meaning here |
|---|---|
| **identical** | Both adapters call the same host mechanism with the same semantics, shown by code plus tests on both paths. Not inferred from similar names. |
| **deliberately different** | Spec, ADR, or code states different outcomes, identity, or policy. Never a G1 extraction candidate. |
| **unknown** | Similarity of shape or name is not enough. No dual-path semantic proof on this SHA. |

G1 extraction requires **identical** plus tests on **both** native and BPMN paths. A fully generic kernel is not a delivery prerequisite.

## Summary table

| Area | Mechanism (short) | Classification | G1 extraction candidate? |
|---|---|---|---|
| 1. Dispatch / invocation | `IWorkflowStartDispatcher` wrappers (`ChildStartExecutor` vs `BpmnNativeWorkDelivery`) | deliberately different | no |
| 1. Dispatch / invocation | Direct model start (`ExecuteModel` / `start-embedded`) vs native `WorkflowExecutionCommandKind.Start` | deliberately different | no |
| 1. Dispatch / invocation | Composite `callActivity` → `DispatchWorkflow` (native child start) | n/a (BPMN binds native dispatch; not a second copy) | no |
| 1. Dispatch / invocation | Composite same-execution `ScheduleChildActivity` | n/a (not a child workflow) | no |
| 2. Result delivery / parent resume | `ParentResumeExecutor` (`ChildCompleted`) vs `BpmnEmbeddingDelivery` (`elsa.bpmn.model-completed.v1`) vs native-work result → model queue | deliberately different | no |
| 2. Result delivery / parent resume | `DispatchWorkflow.ResumeAsync` Complete(Cancelled) vs `RunBpmn` `ActivityTransition.Cancel` | deliberately different | no |
| 3. Incidents / faults | Native `IIncidentStrategy` + `IIncidentStateStore` vs BPMN `HoldForRetry`/`FinalFault` model failures vs dispatch `Faulted` outcome | deliberately different | no |
| 4. Cancellation | Dispatch child-cancel vs `CancelEmbedded.v1` vs `CancelNativeWork.v1` vs model `cancel` input | deliberately different | no |
| 4. Cancellation | Native `WorkflowExecutionCommandKind.Cancel` handler used to stop a **native** execution (dispatch child vs service worker) | unknown (enqueue path differs) | no |
| 5. Retry admission | `RetryFaultedActivity` alteration vs `RetryServiceAsync` exact incident/attempt | deliberately different | no |
| 6. Lease / fence | Execution ownership `RuntimeExecutionLeaseRunner` (native drain and model dispatch) | identical (already in host) | no (already extracted) |
| 6. Lease / fence | Checkpoint `AttachExpectedFence` | identical (already in host) | no (already extracted) |
| 6. Lease / fence | Publish root-write lease / fenced source writer | deliberately different (publish path; not re-done) | no |
| 7. Journal / checkpoint | `RuntimeCheckpointCommitter` + store infrastructure | identical infrastructure; payload participation deliberately different | no |
| 7. Journal / checkpoint | Native publication journal vs BPMN (none) | deliberately different (publish path; not re-done) | no |
| 7. Journal / checkpoint | Post-commit intent kinds (start/resume/cancel child vs StartEmbedded/ResumeNativeParent vs StartNativeWork) | deliberately different | no |

## 1. Dispatch / invocation

### 1.1 Native child start (`DispatchWorkflow` → `IWorkflowStartDispatcher`)

| | |
|---|---|
| Native call site | `Essentials/src/Activities/DispatchWorkflow/Runtime/Services/ChildStartExecutor.cs:162–196` (`_workflowStartDispatcher.DispatchAsync` after `TryAdmitAsync`, nesting-depth check, dispatch-store identity). Activity stages the start at `DispatchWorkflow.cs:226–233`. Host dispatcher: `WorkflowStartDispatcher.cs:165`. |
| BPMN call site (composite `callActivity`) | Same `ChildStartExecutor`. Binding only: `BpmnWorkBinder.cs:41–43` (`BpmnWorkBindingKind.CallProcess` → `DefaultDispatchWorkflowActivityVersionId`, `WaitForCompletion`). No second start wrapper. Direct-plane `CallActivity` is refused at publish (`BpmnDirectExecutionTests.ReviewRegressions`). |
| BPMN call site (direct `serviceTask`) | `BpmnNativeWorkDelivery.cs:107–111` (`starts.DispatchAsync` with `WorkflowExecutableStartAuthority.FromModelWork`, `AllowReferenceLessLegacy`, keyed `bpmn-native:…` identity). Intent kind `Elsa.Bpmn.StartNativeWork.v1` (`BpmnNativeWorkBoundary.cs:26`). |
| BPMN call site (`RunBpmn`) | Does **not** use `IWorkflowStartDispatcher` for the model. `BpmnEmbeddingDelivery.cs:89–94` enqueues `WorkflowExecutionCommandKind.ExecuteModel` (`start-embedded`). |
| Classification | **deliberately different** wrappers around a shared host dispatcher. DispatchWorkflow has admission, nesting, duplicate-child evidence (#1799). Native-work start is model-origin keyed work with retirement/cancel races. Direct model start is a different command kind and a different execution identity (`RuntimeModelExecution`). |
| Evidence | Spec 1017 Gate C (model work handles vs native `ExpectedBookmarkResume`). Cross-model map A.2 / A.8. `RuntimeExecutionProtocolRouter.cs:9–24` (native scheduler vs `ExecuteModel`). |
| Tests (native) | `DispatchWorkflowEndToEndTests`, `ChildStartExecutorTests`, `DispatchWorkflowCheckpointTests`. |
| Tests (BPMN composite) | `e2e-tests/bpmn/Test-BpmnCallActivity.ps1`; `CallActivityNativeChildCrashTests`; `BpmnCalledWorkflowControlEfTests`. Unit `BpmnCallActivityTests` uses a probe leaf (not real dispatch). |
| Tests (BPMN direct service) | `DirectBpmnWorkbenchTests.Direct_service_work_uses_the_real_coalescing_host_and_returns_its_native_completion`. |
| Tests (BPMN `RunBpmn`) | `RunBpmnWorkbenchTests`, `RunBpmnAdmissionCancellationTests` (admission, not dispatcher start). |

### 1.2 Protocol routing (host, not an adapter duplicate)

`RuntimeExecutionProtocolRouter.cs:21–22`: if not a model execution, `native.ProcessAsync` (`WorkflowSchedulerCommandRouter`). Model path requires `ExecuteModel` or `RunSchedulerWork`, validates input, then `materialLeases.ExecuteAsync` (`:52–58`) and `leaseRunner.RunAsync` (`:65–72`). `ModelProtocolWorkHandler.cs:9–13` is the only `ExecuteModel` scheduler handler and calls `BpmnDirectExecutionDriver.ExecuteAsync`.

Classification: **deliberately different** command kinds on one router. Not a duplicated adapter mechanism.

## 2. Result delivery / parent resume

| Path | Call site | Resume contract |
|---|---|---|
| Native waited `DispatchWorkflow` | `ParentResumeExecutor.cs:47–64` → `IBookmarkResumeDispatcher.DispatchAsync`. Stimulus `Elsa.Activities.DispatchWorkflow.ChildCompleted` (`DispatchWorkflowConstants.cs:14`). Payload `WorkflowDispatchParentResumePayload`. Enricher: `WorkflowDispatchCompletionEnricher.cs:50–100`. Parent mapping: `DispatchWorkflow.cs:269–279` **Complete** with outcome `Completed` / `Faulted` / `Cancelled` / `DispatchFailed`. |
| BPMN `RunBpmn` | `BpmnEmbeddingDelivery.cs:34–53` → `resumes.DispatchAsync` with stimulus `elsa.bpmn.model-completed.v1` (`BpmnEmbeddingBoundary.cs:20–24`). Parent mapping: `BpmnModelActivityRuntime.cs:25–29` — `Completed` → outcome `Done`; `Faulted` → fault `bpmn.model-faulted`; `Cancelled` → **`ActivityTransition.Cancel`**. |
| BPMN composite `callActivity` | Same `ParentResumeExecutor` as native dispatch. Interpreter then routes `Faulted`/`Cancelled`/`DispatchFailed` on the call-activity element (`BpmnProcessRuntime.ControlAsync` `:104–117`, only for call-activity tokens). |
| BPMN direct `serviceTask` | `BpmnNativeWorkDelivery.cs:116+` delivers `Elsa.Bpmn.NativeWorkResult.v1` onto the **model** queue (`ExecuteModel` / `native-result`), not a native parent bookmark. |

**Classification: deliberately different.** Do not merge `DispatchWorkflow` Complete(`Cancelled`) with `RunBpmn` `ActivityTransition.Cancel`. Spec 1017 US3 / FR-013 / T040 and #4028 record the latter as an outcome mapping with parent-owned cancel authority. Cross-model map A.8 items 5–6.

Shared host port `IBookmarkResumeDispatcher` (`BookmarkResumeDispatcher.cs:88`) is already extracted. The adapter stimulus, payload, and parent transition are not the same.

| Tests (native) | `ParentResumeExecutorTests`, `DispatchWorkflowEndToEndTests.Wait_mode_starts_child_then_resumes_parent_once_with_completed_result`, `NonSuccessChild_CompletesParentActivityWithOrdinarySafeOutcome`. |
| Tests (BPMN `RunBpmn`) | `RunBpmnWorkbenchTests`, `RunBpmnPendingParentResultCrashTests`, `RunBpmnChildCancelTests`. |
| Tests (BPMN composite) | `CallActivityNativeChildCrashTests`; `Test-BpmnCallActivity.ps1`. |
| Tests (BPMN service result) | `DirectBpmnWorkbenchTests`; `BpmnServiceRetryCancellationTests`. |

Test-only hold `ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME` is used by both `BpmnEmbeddingDelivery.cs:100–117` and `ParentResumeExecutor.cs:148–151`. Production hosts never set it. The comment at `ParentResumeExecutor.cs:40–43` states it is **not** a shared resume kernel. Not a G1 candidate.

## 3. Incidents / faults

| Path | Call site | What a child fault does |
|---|---|---|
| Native activity throw | Drain records `IIncidentStateStore`; at quiescence `IncidentStrategyResolutionDrainObserver.cs:18–38` applies authored `IIncidentStrategy` (default Fault/1). See `docs/runtime-fault-behavior.md`. Operator retry is `RetryFaultedActivityAlterationHandler.cs:11–23`. |
| Native `DispatchWorkflow` child terminal | `WorkflowDispatchCompletionEnricher` maps child status to dispatch `Faulted` / `DispatchFailed` / `Cancelled`. The dispatch **activity completes** with that outcome (`DispatchWorkflow.cs:271–274`). It is not a parent incident on the dispatch node. |
| BPMN composite non-call child | `BpmnProcessRuntime.FaultAsync` `:51–57` / `OutcomeAsync` `:73–101` → interpreter `OnWorkFaulted` / error boundary / `bpmn.child.faulted`. Same `WorkflowExecution`. |
| BPMN composite `callActivity` | Dispatch `Faulted`/`DispatchFailed`/`Cancelled` then BPMN error-boundary ladder (`bpmn.call-activity.*`). Business vs technical stay distinct (map A.6). |
| BPMN direct `serviceTask` | `BpmnDirectExecutionDriver.cs:315–333`: `HoldForRetry` vs `FinalFault`; model document `Failures` / `WaitingIncident`. Not `IIncidentStateStore`. Retry API: `BpmnModelRuntime.RetryServiceAsync` `:305–314`. |
| BPMN `RunBpmn` parent | `BpmnModelActivityRuntime.cs:28` maps model `Faulted` → native activity fault `bpmn.model-faulted`. |

**Classification: deliberately different.** Native incident strategy, dispatch outcomes, BPMN interpreter error disposition, and direct-model HoldForRetry are four policies. Goal G1 and map A.8 forbid flattening them.

| Tests (native) | `RetryFaultedActivityAlterationExecutionTests`, `RetryFaultedActivityAlterationHandlerTests`; drain/incident observers under `Essentials/tests/Workflows/Runtime/Tests`. |
| Tests (BPMN composite) | `NativeBackgroundActivityFlowchartEfTests.Background_fault_receipt_faults_bpmn_process_and_records_typed_incident`; `Test-BpmnCallActivity.ps1` Scenario B. |
| Tests (BPMN direct / `RunBpmn`) | `BpmnServiceRetryCancellationTests`; `RunBpmnChildFaultHeldRetryTests`. |

## 4. Cancellation

| Direction | Native / DispatchWorkflow | BPMN composite | BPMN `RunBpmn` | BPMN direct service / model |
|---|---|---|---|---|
| Parent → child workflow | `WorkflowDispatchCancellationEnricher.cs:36–60` (`CancelChildOnParentCancellation`, wait-mode only). Delivery: `ChildCancelExecutor.cs:59–74` actor envelope `WorkflowExecutionCommandKind.Cancel`. Handler: `WorkflowCancelSchedulerWorkHandler.cs:40–43`. | `BpmnProcessRuntime.Finish` `:228–229` `RequestChildSubtreeCancellation` (spec 137 / #1035). Reaches dispatch cancel when the dispatch activity is Cancelled. | `BpmnEmbeddingCheckpointEnricher.cs:40–62` / `BpmnEmbeddingBoundary.CancelKind` = `Elsa.Bpmn.CancelEmbedded.v1`. Delivery enqueues `ExecuteModel` `cancel-embedded` (`BpmnEmbeddingDelivery.cs:57–76`). | Model `BpmnModelRuntime.CancelAsync` `:349–357` (`inputKind` `cancel`). Worker: `BpmnNativeWorkDelivery.cs:34–60` `CancelNativeWork.v1` then **queue** `WorkflowExecutionCommandKind.Cancel` on the worker. |
| Child → parent | Dispatch Complete with outcome `Cancelled` (`DispatchWorkflow.cs:273`). Parent activity **completes**, it is not itself cancelled. | Interpreter routes `Cancelled` on the call-activity element. | Model `Cancelled` → `ActivityTransition.Cancel` (`BpmnModelActivityRuntime.cs:29`). Parent run ends Cancelled through the activity-cancel checkpoint. Authority stays parent-owned (#4028). | Worker Cancelled can settle model cancellation (`BpmnDirectExecutionDriver.cs:308–314`) or hold/fault per policy. |

**Classification: deliberately different.** Spec 137 vs embedding-cancel vs model-cancel vs native-work-cancel. Fire-and-forget dispatch never propagates (`WorkflowDispatchCancellationEnricher.cs:57–60`).

`WorkflowExecutionCommandKind.Cancel` **handling** of a native `WorkflowExecution` is the same host handler. Whether actor-enqueue (`ChildCancelExecutor`) and scheduler-queue (`BpmnNativeWorkDelivery.cs:58`) are equivalent admission is **unknown** (not proved on this SHA). Not a candidate.

| Tests (native) | `WorkflowDispatchCancellationTests` (18 methods), `RuntimeCancellationContractTests`. |
| Tests (BPMN `RunBpmn`) | `RunBpmnWorkbenchTests` (`cancel: true`), `RunBpmnAdmissionCancellationTests`, `RunBpmnChildCancelTests`. |
| Tests (BPMN composite) | `BpmnCalledWorkflowControlEfTests.Isolation`; `NativeBackgroundActivityFlowchartEfTests` cancel-before-claim / between-results. |
| Tests (BPMN service) | `BpmnServiceRetryCancellationTests`. |

## 5. Retry admission

| | Native | BPMN direct / `RunBpmn` | BPMN composite `callActivity` |
|---|---|---|---|
| Call site | `RetryFaultedActivityAlterationHandler.cs:50–79`: operator alteration of one **faulted activity** in a running native run; requires `WaitForIntervention`; refuses if structural parent is not Running. | `BpmnModelRuntime.RetryServiceAsync` `:305–314` → `retry-service` input. Driver `:230–256` requires `WaitingIncident`, open failure, exact incident/invocation/failed-attempt, `HoldForRetry`, and attempt budget. Creates a **new physical attempt id**. Does not restart the process. | No held-child-execution retry analogous to HoldForRetry. Outbox `FailedRetryable` sweep is transport retry, not G2.2 child retry (map B.4). |
| Classification | **deliberately different.** Spec 1017 Gate C pins technical HoldForRetry vs FinalFault and says not to infer model admission from native `ExpectedBookmarkResume`. |
| Tests (native) | `RetryFaultedActivityAlterationHandlerTests`, `RetryFaultedActivityAlterationExecutionTests`, `WorkbenchCompensationRecoveryTests` (operator retry). |
| Tests (BPMN) | `BpmnServiceRetryCancellationTests`; `RunBpmnChildFaultHeldRetryTests` (native parent + real worker). |
| Tests (composite callActivity child retry) | **none found** (outbox sweep only). |

## 6. Lease / fence handling

Publish-path leases (`ExecuteAsync` around unfenced source `SaveAsync` vs `ExecuteFencedAsync` + `SaveWithRootWriteLeasesAsync`) are already inventoried. Not repeated.

### 6.1 Execution ownership (already in host)

| | |
|---|---|
| Native call site | `WorkflowDrainOrchestrator.cs:197` `_leaseRunner.RunAsync` then `DrainCoreAsync`. Comment `:203`: “This policy belongs to native execution. It runs inside the shared ownership fence.” |
| BPMN (direct model) call site | `RuntimeExecutionProtocolRouter.cs:65–72` same `RuntimeExecutionLeaseRunner`. |
| Implementation | `RuntimeExecutionLeaseRunner.cs:9–20` (`IRuntimeExecutionOwnershipService.AcquireAsync` / heartbeat / release). Checkpoint fence: `RuntimeCheckpointCommitter.AttachExpectedFence` `:326–334` stamps `ExpectedFence = lease.ToFence()`. |
| Classification | **identical** usage of the shared ownership/fence port. Native drain termination (stop/purge) vs model `IsTerminalSettledAsync` (inside the same `leaseRunner.RunAsync` callback at `RuntimeExecutionProtocolRouter.cs:65–72`) is **deliberately different** *around* the lease, not of the lease itself. |
| Tests (native) | Native hosting conformance (spec 249 / #3056); `ClaimLeaseOverrunTests`; runtime ownership tests under `Essentials/tests/Workflows/Runtime`. |
| Tests (BPMN) | `EfModelAdmissionLeaseTests`; hosted `RunBpmn*` / `DirectBpmnWorkbenchTests` (require qualified physical admission). |

Root-write `IWorkflowExecutableRootWriteLeaseManager.ExecuteAsync` (the unfenced single-root overload that does not expose grants) is also used by model **admission enqueue** (`RuntimeExecutionProtocolRouter.cs:52–58`, lease id `model-admission:…`). Native activation uses the same `ExecuteAsync` family (`WorkflowActivationCoordinator.cs:90–94`, lease id `activation:…`). The lease API is therefore the same kind of unfenced root-write lease; what differs is **what is written under it** (model path: a scheduler-queue enqueue of `ExecuteModel` work; native activation: source-reference mint then slot switch). `ExecuteFencedAsync` (the overload that passes grants to an atomic writer) is **not** used on this admission path. Publish-path `ExecuteFencedAsync` + `SaveWithRootWriteLeasesAsync` remains the documented G1 example of a deliberate difference and is not this call.

## 7. Journal / checkpoint writes

Publication journal (`elsa_publication_records`, native Candidate then Active; BPMN writes none) is the publish-path inventory. Not repeated.

### 7.1 Execution checkpoint infrastructure (already in host)

Both native activity commits and BPMN model commits go through `RuntimeCheckpointCommitter` (`:104` fence, then store). Model payload forces Immediate persistence (`:106–117`).

| | Native / composite | Direct BPMN / `RunBpmn` / native-work |
|---|---|---|
| State written | `WorkflowExecution` / activity / bookmark / dispatch records | `ModelState` / model input receipts / `ExpectedModelExecution` / `ExpectedModelParentWait` |
| Enrichers | `WorkflowDispatchCheckpointEnricher`, cancellation/completion/control enrichers (`DispatchWorkflowRuntimeFeature.cs:90–96`) | `BpmnEmbeddingCheckpointEnricher`, `BpmnNativeWorkCheckpointEnricher` (`ActivitiesBpmnHostingFeature.cs:40–44`) |
| Follow-up journal | Post-commit intents `StartChild` / `ResumeParent` / `CancelChild` | `StartEmbedded` / `CancelEmbedded` / `ResumeNativeParent` / `StartNativeWork` / `NativeWorkResult` / `CancelNativeWork` |

**Classification:** checkpoint **infrastructure** (committer, store, fence stamp, outbox dispatcher) is **identical** and already in the host. **Participation** (what is committed, which intent kinds) is **deliberately different**. Spec 1017 Gate B.2: empty model participation preserves the historical native fingerprint.

| Tests (native) | `DispatchWorkflowCheckpointTests`, `RuntimeCheckpointCoalescingTests`, `RuntimeCancellationContractTests`. |
| Tests (BPMN) | `BpmnCalledWorkflowControlEfTests`, `BpmnUpgradeRecoveryEfTests`, embedding/native-work hosted recovery (`RunBpmnPendingParentResultCrashTests`, `CallActivityNativeChildCrashTests`). |

## G1 candidate list

Name only mechanisms that would satisfy G1 (both adapters, same semantics, tests on both paths).

**Remaining extraction candidates (duplicated in adapters, identical, dual-path tests): none.**

Every adapter-level pair in areas 1–5 and 7 (intent kinds) is deliberately different or unknown. Do not extract a shared “child start”, “parent resume”, “incident”, “cancel”, or “retry” kernel from similar names.

**Already in the shared host (would satisfy G1; do not extract again):**

1. **Execution ownership lease / heartbeat / release** — `RuntimeExecutionLeaseRunner` + `IRuntimeExecutionOwnershipService`. Native drain and model dispatch both use it. Tests on both paths (6.1).
2. **Checkpoint expected-fence stamp** — `RuntimeCheckpointCommitter.AttachExpectedFence`. Native and model commits under a lease. Tests on both paths (6.1 / 7.1).
3. **Checkpoint commit store infrastructure** — `IRuntimeCheckpointCommitStore` atomic decision (state + outbox + marker + fence). Payload participation stays model-specific (7.1).

No ETL, Agents, Rules, or future-model abstraction is proposed.

## Unknowns (stay unknown)

| Item | Why it is unknown |
|---|---|
| Actor-enqueue vs scheduler-queue `WorkflowExecutionCommandKind.Cancel` for a native execution (`ChildCancelExecutor.cs:59–74` vs `BpmnNativeWorkDelivery.cs:58`) | Same command kind and host handler; admission/idempotency/envelope vs work-item identity not dual-proved. |
| Whether `IWorkflowStartDispatcher.DispatchAsync` *alone* (stripping ChildStartExecutor admission vs FromModelWork authority) is semantically identical | Wrappers differ; no test that both adapters call the dispatcher with the same request shape. |
| Composite same-execution child complete vs dispatch parent resume as “one parent-resume mechanism” | Different identity (same `WorkflowExecution` vs child workflow). Map A.2: do not merge `BpmnProcess` with `RunBpmn`. |
| Direct-plane worker cancel → model visible outcome as a dedicated hosted mapping | Map B.3 child→parent cancel remains PARTIAL. |
| Non-SQLite / multi-host behaviour of any row | Not inspected beyond source. |

## Boundary-violation findings

Searched production `Essentials/src/Workflows/**/*.cs` for `using Elsa.Activities.Bpmn`: **none**. Shared Runtime does not depend on the BPMN module.

BPMN Interchange binds `callActivity` by **activity version id string** (`BpmnWorkBinder.cs:41–43`), not by reaching into `DispatchWorkflow` internals. `BpmnProcessRuntime` consumes `IRuntimeWorkflowControlBoundary` (a host port); the DispatchWorkflow module supplies the implementation. BPMN source has no `using Elsa.Activities.DispatchWorkflow`.

`RuntimeExecutionProtocolRouter` / `RuntimeExecutionProtocolSelector` branch on **protocol/material and `ExecuteModel`**, not on a BPMN type name (`IsModelAsync` `:23–36`; refuse native/model identity collision `:60–61`). That matches the owner “ports, not model-name switches” rule. No architecture test currently forbids a BPMN `if` inside Runtime (map A.7); none was needed for a type-level violation on this SHA.

**Observation, not a production boundary break:** DispatchWorkflow `ParentResumeExecutor.cs:148–151` reads the BPMN-named test env `ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME`. Production hosts never set it. It is a shared **test** freeze, not a shared resume kernel (`:40–43`).

No core→model or model→other-model-internals violation was found that this slice would fix. None was fixed (docs only).

## What this does not claim

- No extraction is decided or scheduled.
- Unknowns are not resolved.
- Publish-path save/lease/journal differences are not re-classified.
- Dual-path tests named above are **existence** on this SHA, not a re-run on this machine.
- G2.5 final-main re-qualification, other providers, and generic-kernel delivery are out of scope.
- ETL (#3804/#3789), #3650, R3/#3951, #4038, Studio, CI workflows, `elsa-studio` / `elsa-extensions` / `elsa-apps` are untouched.

## Runnable example / supported profile / G-e

The hosted Facts in `Essentials/tests/Activities/Bpmn/Hosting/Tests/` are the runnable example for this inventory's dual-path tests (SQLite Owned Workbench, one host). The supported profile is [unified-execution.md](../program-goals/unified-execution.md) plus [G2 Limit](exec-core-g2-child-outcome-mapping.md#limit-every-hosted-cell) and [S8 unsupported / open](exec-core-migration-rollback.md#6-unsupported--open). The recorded #3993 post-restart timeout is a `ResumeNativeParent.v1` claim-fence wait on the #3992 crash Fact, not a G1 extraction candidate and not native-worker `HoldForRetry`; see [G3 G-e](exec-core-g3-overhead-counts.md#g-e--recorded-3993-post-restart-timeout-causal).
