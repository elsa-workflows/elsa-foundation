# G2.4 child-outcome mapping (native ↔ BPMN)

Point-in-time mapping table for execution-core slice 8 / goal §G2.4. Tests and docs only. No production change. Native and BPMN semantics are not merged.

**Main SHA at inventory:** `1769c5b5a8dae4fc3706c587278f1ffb047073e7` (`origin/main` when this slice started; contains G1 #4047 `e962de590`). File:line citations below are on that SHA unless a later test in this slice is named.

**This slice HEAD:** filled in after the mapping tests land; see the PR.

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
| A2 | (a) RunBpmn | child **BUSINESS error** (BPMN error / modelled `BusinessError`) | unknown on current non-ETL hosting; see OPEN notes | **OPEN** | No current Owned-Workbench / xUnit dual-path test. Historical ETL qualification at `specs/1017-direct-bpmn-hosting/evidence/pipeline/business-error-native-recovery/` executing source `d55964a76139349eeff722501df473401cce204d` is **not** counted: ETL `#3804`/`#3789` is out of this slice, and `RunEtl` is the only first-party activity that Completes with outcome `BusinessError`. Direct native-work profile admits coded error boundaries only; it refuses error-end events. `#3993` uses a **Faulted** worker (`ErrorCode = null`) and does not prove this row. |
| A3 | (a) RunBpmn | child **TECHNICAL failure** (native worker `Faulted` → model `Faulted` / `HoldForRetry` / `FinalFault`) | held: parent stays `Running`, `RunBpmn` `Suspended`, no parent incident; `FinalFault` or exhausted hold: parent `Faulted` once with blocking `ActivityReturnedFault` / `bpmn.model-faulted`; model `TerminalSettled` / `Faulted` | **PROVEN** | `RunBpmnChildFaultHeldRetryTests` in `Essentials/tests/Activities/Bpmn/Hosting/Tests/RunBpmnChildFaultHeldRetryTests.cs`, landed `#3993` `428d9840cc92dd0c0eb00e2d88d380f540e848dc` (held retry → parent `Done`; `FinalFault`; exhausted `HoldForRetry`). Binding `service.business-error` is present but unused for a `Faulted` worker (`BpmnDirectExecutionDriver` sets `ErrorCode` only when the worker **Completed** with `BusinessError`). |
| A4 | (a) RunBpmn | child **cancel** (operator `POST /bpmn/executions/{id}/cancel`) | model `Cancelled` → `RunBpmn` `Cancelled`; Root parent `Cancelled` once; not `Completed`, not `Faulted`; `cancel-embedded` stays parent-owned | **PROVEN** | `RunBpmnChildCancelTests.A_child_model_cancel_maps_to_native_parent_Cancelled_once_and_survives_a_pending_parent_resume_crash` in `Essentials/tests/Activities/Bpmn/Hosting/Tests/RunBpmnChildCancelTests.cs`, landed `#4028` `0eff60275f7e0b43b0d8621b4315cbeb565f2a8a`. |
| B1 | (b) callActivity → native child | child **success** (`WorkflowExecutionStatus.Completed`) | dispatch activity `Completed` with outcome `Completed`; BPMN routes normal outbound; parent `Completed`; no parent incident | **PROVEN** | `CallActivityNativeChildCrashTests` in `Essentials/tests/Activities/Bpmn/Hosting/Tests/CallActivityNativeChildCrashTests.cs`, landed `#3995` `9e52948dd5239e4715e137c14e2546a56c2ab92a` (wait-phase and pending-parent-resume, native Event/WriteLine child). Also e2e `e2e-tests/bpmn/Test-BpmnCallActivity.ps1` Scenario A, original `#1032` `57bd65a58e283a5f5baa7b3be0f2c3ad5a53475e` (still present on this main). Unit `BpmnCallActivityTests.NonFailureOutcome_RoutesNormalOutbound` is a **probe** (not dual-model). |
| B2 | (b) callActivity → native child | child **BUSINESS error** (BPMN error / modelled business outcome) | unknown for a **native-root** child | **OPEN** | Native children do not emit `RuntimeChildControl.AuthoredFailureCode`. `DispatchWorkflow` maps only `WorkflowExecutionStatus` (`Completed`/`Faulted`/`Cancelled`/`DispatchFailed`), not a `BusinessError` activity outcome. `BpmnCalledWorkflowControlEfTests.Actual_called_coded_error_recovers_before_caller_delivery_without_rewriting_child_fault` is a **BPMN-root** child with an error-end; do not merge that with native-root. No first-party native activity besides `RunEtl` Completes with `BusinessError`; ETL is out of this slice. |
| B3 | (b) callActivity → native child | child **TECHNICAL failure** (native `Fault` → child `Faulted`) | dispatch **completes** with outcome `Faulted` (not a parent incident on the dispatch node); catch-all error boundary fires; parent `Completed` incident-free; child keeps its original blocking incident | **PROVEN** | `e2e-tests/bpmn/Test-BpmnCallActivity.ps1` Scenario B, original `#1032` `57bd65a58e283a5f5baa7b3be0f2c3ad5a53475e`. Probe `BpmnCallActivityTests.FailureOutcome_WithErrorBoundary_FiresBoundary_NoIncident` (`Faulted`) is not dual-model. Uncaught `bpmn.call-activity.faulted` is probe-only (`FailureOutcome_NoCatcher_FaultsCompositeWithPinnedCode`); not claimed as hosted native-root proof. |
| B4 | (b) callActivity → native child | child **cancel** (operator `CancelWorkflow` on the native child) | dispatch **completes** with outcome `Cancelled`. With a catch-all error boundary: parent `Completed` via the recover path, no parent incident, normal outbound does not run. Without a catcher: parent `Faulted`, `BpmnProcess` `Faulted`, blocking `ActivityReturnedFault` incident on the process node; dispatch node still `Completed`/`Cancelled` (not itself cancelled). Probe in-memory `BpmnProcess.Fault.Code` is `bpmn.call-activity.cancelled`; hosted instance REST redacts incident metadata without inspect-values (same surface as `#3993`) | **PROVEN** (this slice) | `CallActivityNativeChildOutcomeTests` in `Essentials/tests/Activities/Bpmn/Hosting/Tests/CallActivityNativeChildOutcomeTests.cs`. Probe `BpmnCallActivityTests` Cancelled rows remain probes and are not this proof. |

## What this slice added

Two Owned-Workbench Facts for **B4** only (the hosted native-child cancel row had no dual-model proof). Existing tests were not deleted or weakened. No third `ELSA_BPMN_HOLD_NATIVE_PARENT_RESUME` call site. No G3 / benchmark change.

| Test | Mapping shown |
|---|---|
| `A_cancelled_native_child_under_callActivity_routes_the_error_boundary_and_does_not_look_like_success` | Child `Cancelled` ≠ success: recover path runs, `node-call` outcome `Cancelled`, parent `Completed` incident-free |
| `A_cancelled_native_child_under_callActivity_without_a_catcher_faults_the_composite_with_the_cancelled_code` | Uncaught child `Cancelled` → parent `Faulted` / `BpmnProcess` `Faulted` / blocking `ActivityReturnedFault`; dispatch completes rather than cancelling. When REST metadata exposes a code it must be `bpmn.call-activity.cancelled`. |

## OPEN rows (remain visible)

| Row | Why it stays OPEN | Why this slice did not add a test |
|---|---|---|
| A2 RunBpmn business error | Direct model business error is native worker **Completed** + `OutcomeKey == "BusinessError"` → interpreter `OnWorkFaulted` with `ErrorCode = binding.BusinessErrorCode`. Matching coded boundary → model `Completed` / parent `Done`; unmatched → model `Faulted` / parent `bpmn.model-faulted` (historical ETL receipt). | Only first-party Completes-with-`BusinessError` activity is `RunEtl`. Goal forbids touching ETL `#3804`/`#3789`. Direct profile refuses error-end events. Inventing a production `BusinessError` activity would be production code. |
| B2 callActivity native business error | No native-root authored BPMN error code across `DispatchWorkflow`. | Same ETL hold. Do not treat native `Faulted` as a business error (that is B3). Do not reuse BPMN-root `AuthoredFailureCode` evidence. |
| B3 uncaught technical (no boundary) | Hosted native Fault child without a catcher is not in `Test-BpmnCallActivity.ps1` (Scenario B has a boundary). Probe-only. | Not required to close B3 (boundary path is PROVEN). Left visible as a narrower sub-gap, not as a missing B3 row. |

## Deliberate non-merges

1. RunBpmn cancel (`ActivityTransition.Cancel` → Root workflow `Cancelled`) vs callActivity cancel (dispatch **Complete**(`Cancelled`) → BPMN error ladder).
2. Technical `Faulted` worker (`HoldForRetry`/`FinalFault`, `ErrorCode` null) vs Completed-`BusinessError` (coded boundary). `#3993` is the former.
3. Native-root dispatch vs BPMN-root `AuthoredFailureCode` (`BpmnCalledWorkflowControlEfTests`).
4. Probe `BpmnCallActivityTests` vs real `DispatchWorkflow` host.

## G2.5 / G3 / G1

G2.5 re-run on the milestone's final main commit is **not** this slice. Named tests exist on `1769c5b5a` plus this slice's new class; they were not all re-executed here. G3 overhead (`docs/reports/exec-core-g3-overhead-counts.md`) and G1 inventory (`docs/reports/exec-core-g1-mechanism-inventory.md`) are unchanged. No extraction.

## Findings

No production defect was observed while reconciling or running the new B4 Facts. The recover-path Fact maps child cancel away from success (parent `Completed` via recover, not the normal outbound). The uncaught Fact maps child cancel to parent `Faulted` with a blocking `ActivityReturnedFault` on `BpmnProcess`, not to parent success. Hosted instance REST redacts incident metadata without inspect-values, so the probe-pinned `bpmn.call-activity.cancelled` code is not claimed on that surface; `#3993` already used the same fallback. The two OPEN business-error rows are **unknown** for a non-ETL native/BPMN proof, not a discovered mis-mapping.

## Out of scope (honoured)

No production code. No ETL `#3804`/`#3789`, `#3650`, `#3204`, `#4038`, R3 `#3951`. No issue closed. No generic-kernel extraction. No third hold-file call site. No new benchmark or `ef-command-budgets.json` row.
