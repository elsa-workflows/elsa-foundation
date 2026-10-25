# Released-library adapter checkpoint

Implementation increment on the second ordered PR, [#3338](https://github.com/elsa-workflows/elsa-foundation/pull/3338). This is not final adoption qualification or ADR acceptance. The parity PR is merged; the switch PR remains draft.

## Delivered in this increment

- Published Model/Interchange/Semantics 0.5.0 references; no source-project substitution.
- A Foundation-owned runtime boundary, version-aware state codec, complete graph binding and parent-scoped durable child ledger. Existing v1 documents retain their alias/version and `activeChildren` / `producingActivityExecutionId` representation. Fresh executions write v2. Callback reads cannot initialize missing state.
- Exact child/parent/node/element/token/iteration checks before library pruning, including terminal history. Missing identity is an explicit failure; neither first-node matching nor callback metadata replaces historical provenance.
- Ordered notification/fault folding, exact retirement of reporting handles, suppression of accumulated unissued work on final Fault, preservation of the first semantic fault, and absorption of only the original incident on a non-fault handled verdict.
- Shared graph translation for runtime and trigger/schedule preflight, including rejection of orphan authored children.
- Explicit import binding capabilities. Date/cycle waits retain their descriptors/data and report Unsupported; signal publication maps to PublishEvent. Unknown never qualifies execution readiness.
- Default-edge markers derive from the source element's DefaultFlowId. Typed conditions remain typed edit/export data in the existing element property bag, never outcome labels. This host refuses their execution profile. Deleting/replacing a flow permits the remaining graph; reconnecting an expression flow preserves its typed condition by flow identity and remains explicitly unsupported for execution.
- Exact degradation findings for unrepresented scripts, variable type/default fields and retained foreign content, including recursive lane metadata.

The old semantic implementation is still present pending its deletion gate. The preview capture generator is retired from current execution: its permanent source is `d81a4c88a2924708ea8afcc2db20b8a77b84f703`, and the actual frozen databases now drive upgraded-host tests. No normal test regenerates them.

## Evidence and meaningful controls

| Witness | Result / limit |
|---|---|
| Whole BPMN project | 267 passed, zero failed/skipped. Includes real host command/incident behavior, three combined-outcome cases, ended/missing-checkpoint controls, orphan rejection and scoped replacement registration validation. |
| Frozen legacy EF databases | Five fresh upgraded-host outcomes plus four corruption/refusal cases pass. Both stored digests are checked before opening; continuing documents remain v1; the original deferred fault code/message survives. See [capture provenance and limits](legacy/README.md). |
| First-node/iteration mutation | Exactly the listener-rearm case fails (1 failed / 8 passed); restoring exact-handle lookup passes 9/9. |
| Raw library exception mutation | Exactly the missing-graph-element case fails its recorded BpmnHostException assertion (1 failed / 8 passed); restored 9/9. |
| Whole interchange project | 117 passed, zero failed/skipped. Typed expression body/language roundtrip and real trigger-publish refusal are distinct checks. These are not Studio/browser evidence. |
| Whole runtime EF project | 1,014 passed, zero failed/skipped. Initial eight failures were test inspection through the retired v1-only helper; updated inspection preserves the actual native join, restart, cancellation and typed-incident assertions. This includes existing general combined-outcome controls, not yet the dedicated BPMN combined-outcome durability witness. |
| Locked solution restore | Passed after the package/reference changes. Generated solution-filter check passed; filters unchanged. |

Incremental independent correctness, standards and acceptance reviews found and corrected truncated-state initialization, missing stopped-work history, iteration mismatch, orphan-child omission, ended-notification bypass, semantic-fault replacement, replacement registration ambiguity and a raw library exception leak. Interchange review separately corrected expression flattening, missing foreign-data findings and stale retention markers. These development reviews are not the final complete-PR author-review loop.

The SDK 10.0.402 restore pruned framework-provided packages from eleven affected locks. Retained non-BPMN package version/hash changes are limited to PKCS 10.0.7 → 9.0.13 in Workbench and the two Modularity locks: pruning Cryptography.Xml removed the former 10.0.7 requirement; surviving NuGet/SqlClient requirements select 9.0.13. This was inspected, not hidden as a BPMN-only lock change. A global pin would affect unrelated projects and is not introduced.

## Test reconciliation so far

All original runtime scenarios stay in Foundation. State assertions now read released Model records. Intentionally changed expectations have these reasons:

| Original scenario | Current witness / reason |
|---|---|
| BpmnFaultAndTerminateTests.TerminateEndEvent_EndsProcess_WhileSiblingBranchStillPending | Same scenario now asserts no unissued sibling invocation escapes. The release completes immediately and Foundation clears completed private state. |
| BpmnEventBasedGatewayTests.Terminate_WhileRaceLive_CancelsMembersLogically_WithoutSeamATeardown | Renamed Terminate_WhileRaceLive_CancelsMemberSubtrees_AndCleansBookmarks. Release terminate tears down already-issued children; durable cancellation and bookmark cleanup are asserted. |
| BpmnEventStartRuntimeTests.TriggerDelivery_SeedsOnlyTargetedStart_AndRoutesToCompletion | Same routing assertions; diagnostic wording follows the release's “trigger” vocabulary. |
| BpmnEventStartRuntimeTests.DirectInvocation_WithOnlyEventDefinedStarts_FaultsDeterministically | Timer fixture now declares a valid interval, so it reaches the intended no-none-start verdict after the stronger released graph validation. |
| BpmnBoundaryEventTests.ChildFault_WithoutErrorBoundary_FaultsComposite_Unchanged | Same terminal host assertions; diagnostic wording follows “failed work cannot complete.” |
| NativeBackgroundActivityFlowchartEfTests state inspection | Reads v2 released state and ActiveWork; actual native parent, joins, cancellation and typed incident assertions remain. |
| Interchange timer/signal expectations | Newly retained date/cycle descriptors are explicitly Unsupported; signal publishing has a host binding. Typed conditions retain body/language, do not become literal outcomes, and are refused at preflight. |

The full permanent 242-method audit and deletion/replacement reconciliation are still required before merge. No library-only replacement is claimed for Foundation host assertions.

## Registered binder and catalog increment

The named scoped IBpmnWorkBinder now owns the existing Delay/Event/PublishEvent/DispatchWorkflow/nested activity policy. Its public request/result models are host-owned; third-party model types do not leak through that contract. Importer/exporter/binder are explicit single-implementation replacement contracts, with startup missing/duplicate checks and a preserved explicit host override.

Pure synchronous conversion cannot establish host inventory: its proposed bindings are Unknown and CanExecute is false. The registered HTTP import/analyze endpoints use the asynchronous catalog path. It resolves visible, unique existing 1.0.0 profile versions to actual catalog IDs, recursively including nested processes, and refuses readiness for missing/ambiguous inventory rather than selecting a newer native contract implicitly. Ordinary publishing validation remains necessary.

The whole interchange project passes **127/127, zero failures/skips**. Registration-removal mutation fails its positive composition witness; reverting the malformed-structure wrapper fails the exact BpmnInterchangeException control; restored whole-project tests pass. Independent correctness/standards/acceptance increment reviews are clean after the JSON-boundary fix. Normal and locked solution restore and generated-filter check pass. Five lock deltas add only the explicit Design.Core contract dependency edge.

These are conversion, registration and catalog-port controls. They do not establish actual persisted publication/execution, message/signal delivery, dispatch, or Studio compatibility. PowerShell 7.6.8 is prepared in an isolated temporary tool directory, with the official release asset SHA-256 verified, for the repository's owned-server source journeys. A current source-built Workbench build succeeds; actual journeys follow on the committed source.

## Open gates

Dedicated late-fault, duplicate callback and pruning witnesses; old/new reader and rollback matrix; durable combined-outcome retry/concurrent claims; complete native-parent qualification; actual CallActivity dispatch/error/escalation and ordinary-task compensation proof; published authoring fingerprint; source-built Studio journeys; duplicate deletion/guards; full checks and final review/merge. Keep #2898/#2907 and the broader program open. Orleans/task-runner follow-ups remain deferred.
