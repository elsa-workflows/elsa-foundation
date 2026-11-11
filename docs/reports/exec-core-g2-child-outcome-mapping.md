# G2.4 child-outcome mapping (native ↔ BPMN)

Point-in-time mapping table for execution-core slice 8 / goal §G2.4. Tests and docs only. No production change. Native and BPMN semantics are not merged.

**Main SHA at inventory:** `1769c5b5a8dae4fc3706c587278f1ffb047073e7` (`origin/main` when this slice started; contains G1 #4047 `e962de590`). File:line citations below are on that SHA unless a later test in this slice is named.

**B4 slice HEAD:** `aa2dcb2739734412fc9d8e307a4f395392ef8b87` (hosted native-child cancel Facts). **D4 / A2 HEAD:** `6bf5c963912db41e3d40dcf8febef308b7e0c64f` (hosted `RunBpmnNativeBusinessErrorTests`). The PR head may add only this pin.

**Method:** read existing G2 tests and reports first (`#4028`, `#3995`, `#3992`, `#3993`, `#4047`, G3 counts, BPMN/native hosting and e2e). A row is **PROVEN** only when a dedicated test body actually runs both sides of the named boundary (no probe/fake for the other model). **OPEN** means searched; no such current proof. **unknown** means the intended mapping is not pinned by a dual-path test or by a contract that applies to that child type. Equivalence is not inferred across planes.

## Limit (every hosted cell)

SQLite Owned Workbench, **one in-process host**, Root composition unless a cited test says otherwise. No PostgreSQL, SQL Server or MySQL cell. Direct-plane `CallActivity` is refused at publish (composite only). `ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME` is not used by the new tests; the two production-side call sites (`BpmnEmbeddingDelivery`, `ParentResumeExecutor`) are unchanged and no third site is added.

## Planes (do not collapse)

| Direction | Child | Parent mapping site | Parent transition |
|---|---|---|---|
| **(a)** native parent → BPMN child via `RunBpmn` | Direct `RuntimeModelExecution` | `BpmnModelActivityRuntime.ResumeAsync` | model `Completed` → activity outcome `Done`; model `Faulted` → activity fault `bpmn.model-faulted`; model `Cancelled` → `ActivityTransition.Cancel` (Root parent workflow becomes `Cancelled`) |
| **(b)** BPMN `callActivity` → `DispatchWorkflow` → native child | Separate native `WorkflowExecution` | `DispatchWorkflow.ResumeAsync` then `BpmnProcessRuntime.ControlAsync` | child status → dispatch **completes** with `Completed` / `Faulted` / `Cancelled` / `DispatchFailed`. The dispatch node is not itself cancelled or incidented. BPMN then routes failure outcomes through the error-boundary ladder (`bpmn.call-activity.*`) |

`DispatchWorkflow` Complete(`Cancelled`) is **not** `RunBpmn` `ActivityTransition.Cancel`. Spec 1017 T040 owns (a); spec 231 / 133 owns (b).

## Mapping table

| Row | Direction | Child outcome | Parent outcome | Status | Evidence |
|---|---|---|---|---|---|
| A1 | (a) RunBpmn | child **success** (model `Completed`) | `RunBpmn` activity `Completed` with outcome `Done`; Root parent `Completed`; no parent incident | **PROVEN** | `RunBpmnWorkbenchTests.A_real_native_RunBpmn_wait_restarts_and_consumes_the_same_model_lifecycle` (`cancel: false`) in `Essentials/tests/Activities/Bpmn/Hosting/Tests/RunBpmnWorkbenchTests.cs`, landed `#3410` `9936c891ed90a2c4c9b524d06eaa85317c6eefe7`. Also `InstantBpmnWorkbenchTests.First_decision_completion_returns_to_the_original_native_parent_after_restart` (same SHA) and `RunBpmnPendingParentResultCrashTests` `#3992` `eb4c6a6162130123621ee9a2d326256fd6d8c97e` (success mapping after pending-parent-resume crash). |
| A2 | (a) RunBpmn | child **BUSINESS error** (native worker **Completed** + outcome `BusinessError` → interpreter `OnWorkFaulted` with `ErrorCode = binding.BusinessErrorCode`) | matching coded error boundary: model `Completed`, `RunBpmn` `Done`, Root parent `Completed`, no parent or model incident; worker stays `Completed` / `BusinessError` (not A3 `Faulted`). Unmatched (no boundary): model `TerminalSettled` / `Faulted`, parent `Faulted` once with blocking `ActivityReturnedFault` / `bpmn.model-faulted`. Spec 1017 FR-013 / US4: unmatched business error must not become success | **PROVEN** (D4) | `RunBpmnNativeBusinessErrorTests` in `Essentials/tests/Activities/Bpmn/Hosting/Tests/RunBpmnNativeBusinessErrorTests.cs`, `6bf5c963912db41e3d40dcf8febef308b7e0c64f`. Hosted Owned Workbench, SQLite, one host, Root. Emitter is first-party Finish intrinsic Completing with `BusinessError` (Workbench cannot load a test-only CLR without production changes; `/design/activities/definitions` has no Elsa.Finish). Native-work profile admits the coded error **boundary** → end shape and refuses a recover `userTask`. Historical ETL receipt at `d55964a76139349eeff722501df473401cce204d` is not this proof. `#3993` remains A3. |
| A3 | (a) RunBpmn | child **TECHNICAL failure** (native worker `Faulted` → model `Faulted` / `HoldForRetry` / `FinalFault`) | held: parent stays `Running`, `RunBpmn` `Suspended`, no parent incident; `FinalFault` or exhausted hold: parent `Faulted` once with blocking `ActivityReturnedFault` / `bpmn.model-faulted`; model `TerminalSettled` / `Faulted` | **PROVEN** | `RunBpmnChildFaultHeldRetryTests` in `Essentials/tests/Activities/Bpmn/Hosting/Tests/RunBpmnChildFaultHeldRetryTests.cs`, landed `#3993` `428d9840cc92dd0c0eb00e2d88d380f540e848dc` (held retry → parent `Done`; `FinalFault`; exhausted `HoldForRetry`). Binding `service.business-error` is present but unused for a `Faulted` worker (`BpmnDirectExecutionDriver` sets `ErrorCode` only when the worker **Completed** with `BusinessError`). |
| A4 | (a) RunBpmn | child **cancel** (operator `POST /bpmn/executions/{id}/cancel`) | model `Cancelled` → `RunBpmn` `Cancelled`; Root parent `Cancelled` once; not `Completed`, not `Faulted`; `cancel-embedded` stays parent-owned | **PROVEN** | `RunBpmnChildCancelTests.A_child_model_cancel_maps_to_native_parent_Cancelled_once_and_survives_a_pending_parent_resume_crash` in `Essentials/tests/Activities/Bpmn/Hosting/Tests/RunBpmnChildCancelTests.cs`, landed `#4028` `0eff60275f7e0b43b0d8621b4315cbeb565f2a8a`. |
| B1 | (b) callActivity → native child | child **success** (`WorkflowExecutionStatus.Completed`) | dispatch activity `Completed` with outcome `Completed`; BPMN routes normal outbound; parent `Completed`; no parent incident | **PROVEN** | `CallActivityNativeChildCrashTests` in `Essentials/tests/Activities/Bpmn/Hosting/Tests/CallActivityNativeChildCrashTests.cs`, landed `#3995` `9e52948dd5239e4715e137c14e2546a56c2ab92a` (wait-phase and pending-parent-resume, native Event/WriteLine child). Also e2e `e2e-tests/bpmn/Test-BpmnCallActivity.ps1` Scenario A, original `#1032` `57bd65a58e283a5f5baa7b3be0f2c3ad5a53475e` (still present on this main). Unit `BpmnCallActivityTests.NonFailureOutcome_RoutesNormalOutbound` is a **probe** (not dual-model). |
| B2 | (b) callActivity → native child | child **BUSINESS error** (BPMN error / modelled business outcome) | **no product path, needs design** | **LIMITATION** | Nothing carries a business outcome from a **native-root** child across `DispatchWorkflow` (status only: `Completed`/`Faulted`/`Cancelled`/`DispatchFailed`). `AuthoredFailureCode` is read only for BPMN-root children (`BpmnProcessRuntime.cs:137`). Do not merge with A2, B3 (`Faulted`), or BPMN-root `BpmnCalledWorkflowControlEfTests`. Recorded as a limitation, not an untested shipped path. |
| B3 | (b) callActivity → native child | child **TECHNICAL failure** (native `Fault` → child `Faulted`) | dispatch **completes** with outcome `Faulted` (not a parent incident on the dispatch node); catch-all error boundary fires; parent `Completed` incident-free; child keeps its original blocking incident | **PROVEN** | `e2e-tests/bpmn/Test-BpmnCallActivity.ps1` Scenario B, original `#1032` `57bd65a58e283a5f5baa7b3be0f2c3ad5a53475e`. Probe `BpmnCallActivityTests.FailureOutcome_WithErrorBoundary_FiresBoundary_NoIncident` (`Faulted`) is not dual-model. Uncaught `bpmn.call-activity.faulted` is probe-only (`FailureOutcome_NoCatcher_FaultsCompositeWithPinnedCode`); not claimed as hosted native-root proof. |
| B4 | (b) callActivity → native child | child **cancel** (operator `CancelWorkflow` on the native child) | dispatch **completes** with outcome `Cancelled`. With a catch-all error boundary: parent `Completed` via the recover path, no parent incident, normal outbound does not run. Without a catcher: parent `Faulted`, `BpmnProcess` `Faulted`, blocking `ActivityReturnedFault` incident on the process node; dispatch node still `Completed`/`Cancelled` (not itself cancelled). Probe in-memory `BpmnProcess.Fault.Code` is `bpmn.call-activity.cancelled`; hosted instance REST redacts incident metadata without inspect-values (same surface as `#3993`) | **PROVEN** (this slice) | `CallActivityNativeChildOutcomeTests` in `Essentials/tests/Activities/Bpmn/Hosting/Tests/CallActivityNativeChildOutcomeTests.cs`. Probe `BpmnCallActivityTests` Cancelled rows remain probes and are not this proof. |

## What the B4 slice added

Two Owned-Workbench Facts for **B4** only. Existing tests were not deleted or weakened. No third `ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME` call site. No G3 / benchmark change.

| Test | Mapping shown |
|---|---|
| `A_cancelled_native_child_under_callActivity_routes_the_error_boundary_and_does_not_look_like_success` | Child `Cancelled` ≠ success: recover path runs, `node-call` outcome `Cancelled`, parent `Completed` incident-free |
| `A_cancelled_native_child_under_callActivity_without_a_catcher_faults_the_composite_with_the_cancelled_code` | Uncaught child `Cancelled` → parent `Faulted` / `BpmnProcess` `Faulted` / blocking `ActivityReturnedFault`; dispatch completes rather than cancelling. When REST metadata exposes a code it must be `bpmn.call-activity.cancelled`. |

## What D4 added (owner 11 Nov: prove A2; record B2 as a limitation)

Two Owned-Workbench Facts for **A2**. Tests and this report only. No production code (including no `"BusinessError"` constant extraction). SQLite / one host / Root remains explicit. Existing tests were not deleted or weakened.

| Test | Mapping shown |
|---|---|
| `A_native_worker_Completed_BusinessError_under_RunBpmn_takes_the_matching_coded_boundary` | Worker `Completed` + `BusinessError` under a coded error boundary matching `BusinessErrorCode`: model `Completed`, parent `Done`, no incident. Native-work profile shape is service + interrupting coded boundary → end (a recover `userTask` is refused at publish). |
| `A_native_worker_Completed_BusinessError_under_RunBpmn_without_a_matching_boundary_faults_the_model` | Same worker, no boundary: model `Faulted` / parent `bpmn.model-faulted` once. Worker inspection stays `Completed` / `BusinessError` with no `faultCode` (not A3). Spec 1017 FR-013 / US4: unmatched business error is not success. |

**Follow-up (not this PR):** name `"BusinessError"` as a shared constant and document it. The driver and `RunEtl` still share the bare string.

## OPEN rows (remain visible)

| Row | Why it stays OPEN | Why this slice did not add a test |
|---|---|---|
| B3 uncaught technical (no boundary) | Hosted native Fault child without a catcher is not in `Test-BpmnCallActivity.ps1` (Scenario B has a boundary). Probe-only. | Not required to close B3 (boundary path is PROVEN). Left visible as a narrower sub-gap, not as a missing B3 row. |

## Limitations

| Row | Record |
|---|---|
| B2 callActivity native business error | **No product path, needs design.** Nothing carries a business outcome from a native-root child across `DispatchWorkflow`. `AuthoredFailureCode` is BPMN-root only (`BpmnProcessRuntime.cs:137`). Not an untested shipped path. |

## Deliberate non-merges

1. RunBpmn cancel (`ActivityTransition.Cancel` → Root workflow `Cancelled`) vs callActivity cancel (dispatch **Complete**(`Cancelled`) → BPMN error ladder).
2. Technical `Faulted` worker (`HoldForRetry`/`FinalFault`, `ErrorCode` null) vs Completed-`BusinessError` (coded boundary). `#3993` is the former.
3. Native-root dispatch vs BPMN-root `AuthoredFailureCode` (`BpmnCalledWorkflowControlEfTests`).
4. Probe `BpmnCallActivityTests` vs real `DispatchWorkflow` host.

## G2.5 / G3 / G1

G2.5 re-run on the milestone's final main commit is **not** this slice. Named tests exist on `1769c5b5a` plus this slice's new class; they were not all re-executed here. G3 overhead (`docs/reports/exec-core-g3-overhead-counts.md`) and G1 inventory (`docs/reports/exec-core-g1-mechanism-inventory.md`) are unchanged. No extraction.

**Runnable example** for the G2 hosted cells: `Essentials/tests/Activities/Bpmn/Hosting/Tests/` — `RunBpmnChildFaultHeldRetryTests` (#3993, plane (a) A3, no restart), `RunBpmnPendingParentResultCrashTests` (#3992, plane (a) A1 after crash), `RunBpmnChildCancelTests` (#4028, A4), `CallActivityNativeChildCrashTests` / `CallActivityNativeChildOutcomeTests` (plane (b)). There is no separate sample app.

**Supported profile** is the Limit above plus [unified-execution.md](../program-goals/unified-execution.md) (SQLite, built-in host, Root native parent, existing BPMN human/service-task paths) and [S8 unsupported / open](exec-core-migration-rollback.md#6-unsupported--open). Do not infer other providers or Sequence/Flowchart parents.

**G-e (recorded #3993 post-restart timeout):** not a G2.2 path. Causal write-up in [G3 G-e](exec-core-g3-overhead-counts.md#g-e--recorded-3993-post-restart-timeout-causal).

## Findings

No production defect was observed on A2. Matching coded boundary completes the model and resumes `RunBpmn` with `Done`; the worker is `Completed` / `BusinessError`, not `Faulted`, and there is no parent or model incident. Unmatched (no boundary) faults the model and the Root parent once with blocking `ActivityReturnedFault` / `bpmn.model-faulted`, which is the declared spec 1017 FR-013 profile rather than silent success. That parent incident is the model-fault mapping (same surface as A3 `FinalFault`), not a technical fault of the native worker. Native-work profile publish refuses a recover `userTask`; the matching Fact uses the admitted coded-boundary → end shape. `/design/activities/definitions?search=Elsa.Finish` is empty (persisted CLR rows); Finish is submitted from `/design/activities/catalog` as an intrinsic. B2 is a limitation (no native-root business-error path across `DispatchWorkflow`), not a discovered mis-mapping. B4 findings above are unchanged.

## Out of scope (honoured)

No production code. No ETL `#3804`/`#3789`, `#3650`, `#3204`, `#4038`, R3 `#3951`. No issue closed. No generic-kernel extraction. No third hold-file call site. No new benchmark or `ef-command-budgets.json` row.
