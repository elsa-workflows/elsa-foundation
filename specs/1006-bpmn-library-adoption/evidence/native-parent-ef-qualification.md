# Native BPMN parent qualification through EF

This closes T025's actual Foundation runtime/provider scope for native Sequence and outer Flowchart, using released BPMN 0.5.0 in both **Durable and Balanced** persistence modes. The [machine receipt](native-parent-ef-qualification.json) binds the source, binaries, full runs, restored behavioral controls and unchanged historical evidence. No production code or package/authoring contract changes are included. These are published native workflows driven through the real runtime, background worker APIs and SQLite stores; they are not library in-memory-host substitutes or final Workbench/Studio process acceptance.

| Matrix | Current cases | Evidence |
|---|---:|---|
| BPMN completion across provider restart | 12 | Both child-result orders under each native parent; Flowchart also covers its outer sibling finishing before or after the internal join. All combinations run in both persistence modes, with the persisted mode positively asserted after publication/start. A worker result is physically ResultReady before the first provider is disposed. The fresh provider retains the pinned artifact and accepts it once, keeping internal/outer joins closed until their own remaining work completes. |
| Cancel before worker claim | 4 | Both parents/modes preserve cancelled children and an unclaimable background ledger; no body or final step runs. Existing assertions remain active. |
| Cancel between worker results, then restart/replay | 8 | Either child can complete first under either parent/mode. The other body is already held, cancellation retires it, and a real stale result-record attempt is refused. The original provider disposes before a new provider re-enqueues the saved late pointer. It is positively queued, consumed without poison and cannot reopen the joins or change durable facts. |
| Existing single-provider typed-fault controls | 4 | Existing assertions retained and expanded to both modes. Original business fault/incident and parent-specific fault statuses remain explicit. |
| Typed fault ready before acceptance, then two provider restarts | 8 | Either child faults under each parent/mode. The first provider records the actual typed ResultReady payload while activity/workflow remain Running. The next provider accepts that exact result and preserves the original fault/incident; the third receives the identical accepted pointer and purges it without new effects. |
| Existing non-BPMN native background controls | 7 | Original generic Sequence completion/mode and cancellation cases remain intact. |
| Captured legacy recovery regression set | 17 | The whole existing old-database recovery/refusal/completion/fault/replay class reruns under the corrected observer contract described below. |

The native class contains **43 cases**, including 36 BPMN/native cases; with the 17 old-database cases the native/legacy set is **60/0/0**; the final focused run also includes the three combined-outcome regression cases for **63/0/0**. The complete Runtime EF project is **1,678/0/0**. The native class previously had 19 cases, so discovery increases by 24; no baseline scenario was silently removed. Both publication and partition-copy setup remain the original fixture path: the copied executable/source reference comes from the actual publish operation, not fabricated execution state.

## Live replay and durable observations

Each of the twelve completion cases retains its saved coordinator pointer and additionally captures the **actual normal parent-completion scheduler input** through a forwarding handler wrapper. Exact parent node and completed-child identity are checked. After normal acceptance, the original coordinator pointer and the identical actual parent callback are independently re-enqueued and consumed. The latter has positive queue presence and actual handler re-entry; its processed receipt prevents repeated effects. No callback is reconstructed from known IDs.

Concrete committed EF reads freeze complete activity documents, durable-value documents, background work rows including revision/fence/result facts, incidents and the bookmark projection. They also retain every current workflow continuation/source/context field, including **RootVariableFrame**, authority, partition, run kind, provenance and system metadata. Only workflow `UpdatedAt` is excluded as operational touch time; this is not a claim of whole-workflow byte identity. Inline variables are not replaced by an assertion about external durable-value rows. These background workloads do not provide a positive bookmark-generation witness; the projection checks absence/equality, while T017/T024 retain their separate positive bookmark observations.

Across live callback replay, the complete frozen projection stays equal, the second child/joins remain waiting, worker bodies run once, and the final step stays zero until both internal and any outer work permit it. Normal completion then runs the final step once. Existing terminal-completion replay status/counter checks remain active.

Cancellation and fault snapshots have the same explicit field scope and are compared before and after the fresh-provider terminal pointer replay, with positive queue presence, consumption and empty poison checks. Those terminal replays use the runtime's terminal-workflow purge; they are not described as live parent callback-guard execution. The typed fault's result is serialized before acceptance and remains equal afterward. Its child code is `background.business-fault`, its original incident stays blocking/ActivityReturnedFault, and siblings are cancelled without executing. **Sequence remains Running while the workflow is Faulted; outer Flowchart is Faulted.** The adapter does not normalize the native parents' different policies.

## Observer correction and negative controls

The first native increment run passed19/failed6: the new observer exposed only IWorkflowSchedulerWorkHandler. That selected plain compatibility dispatch and dropped the pipeline partition on replay, producing missing-parent poison. The corrected native observer also implements **IRuntimePipelineWorkHandler** and forwards the actual context unchanged, preserving the original name, eligibility and lifetime. The older `BpmnUpgradeRecoveryEfTests` observer receives the same correction. Historical receipts remain frozen at their original source/instrumentation; they are not rewritten to imply they already forwarded pipeline context. The current 17-case rerun requalifies their outcomes under the faithful observer, and the final focused set includes it.

The initial final whole EF run passed 1,677 and failed the existing T024 full snapshot comparison once. An isolated diagnostic rerun passed three and a whole diagnostic rerun passed 1,678, but the concrete read-skew path was corrected: activity suspension and physical bookmark creation commit separately. The required cut now positively ties the trailing activity's BookmarkWaiting substatus, exact BookmarkIds/metadata and nonempty CreateBookmark scheduler-work ID to the same physical wait's identity/work ID, as written by the real bookmark-created checkpoint. All full fields and post-ACK/retry comparisons remain intact; no post-ACK baseline or field exclusion is added. An attempted wait for the whole sweep instead timed out against the deliberately held delivery in normal and mutant runs; both are retained unqualified, and that approach was removed. Historical T024 receipts remain frozen; the current combined mutations and final whole run requalify the precise observation.

Two deliberately broken production variants were run in isolated in-checkout outputs, then restored byte-identically before final normal verification:

| Mutation | Actual result | Scope |
|---|---|---|
| Ignore the processed parent-completion receipt | All 12 completion cases fail their complete durable-fact comparison after replaying the real captured parent callback. | Reaches the live callback replay phase; no claim that worker bodies reran or joins advanced is inferred from the comparison alone. |
| Make Sequence handle its child's fault by returning Fault | All four Sequence recovery cases fail expected Running versus actual Faulted; all four Flowchart cases pass. | Fails during recovered parent-status verification, before terminal replay; proves the asymmetric policy is intentionally preserved. |

Both final native mutants compile and fail behaviorally: **16 failures/four companion passes**. A first fault-policy mutation attempt used a nonexistent context member and failed compilation; that attempt is retained as unqualified and is not counted. The corrected T024 observation also reruns its two distinct behavioral mutants: bypassing the processed-outcome guard must still fail at the duplicate adapter evaluation, and omitted incident absorption must still fail at the original before-ACK incident assertion. These are recorded separately from the native sixteen-failure total. No production semantic, cancellation, provider or fault policy was changed to fit an expectation.

Reproduce from normal checkout output:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true \
  --filter 'FullyQualifiedName~NativeBackgroundActivityFlowchartEfTests|FullyQualifiedName~BpmnUpgradeRecoveryEfTests|FullyQualifiedName~CombinedChildOutcomeEfTests'
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

Three independent bounded source/artifact audits support this T025 reconciliation; full-PR rounds remain zero. T017, T021/T022, T024 and broadcast receipts stay source-specific. This qualifies native current-library EF behavior across service-provider restarts, not full-process kill/restart, an old-binary native upgrade/rollback, final real Workbench/Studio or production acceptance. T029/T045 binding/called-control, semantic writer deletion/guards, final qualification/full-PR/required exact-head checks/protected delivery/resulting-main, separate ADR0063 and independent #2898/#2907/program evidence remain open. Orleans/task-runner remain deferred.
