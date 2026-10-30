# Exact called-workflow sibling isolation through EF

This bounded **T045 increment** adds four cases: interrupting and non-interrupting escalation in Durable and Balanced modes, through real published/pinned DispatchWorkflow and separately executing BPMN roots over SQLite. Two parallel tokens occupy one valid Call Activity element and one executable binding, with null iteration, identical child pin and distinct call invocations, semantic tokens and child workflow executions. The [machine receipt](called-sibling-isolation-ef-recovery.json) binds source, binaries, verification and restored mutations. T045 remains open.

The test chooses the **second actual semantic active-work token**, then uniquely resolves its durable calling activity and dispatch record. It explicitly differs from the first semantic token; activity-store enumeration order does not choose the target. Both real children initially park at an Event. The original other call/dispatch, complete child activity/incident documents and actual physical bookmark observations are frozen, with child workflow status, pin and persisted mode checked.

After disposing the first provider, the second positively reads both unchanged projections. It resumes only the selected child's actual Event through the normal bookmark dispatcher. Its real WARN notification reaches one ControlAsync addressed to the exact selected call at ordinal zero. With interruption, the actual dispatched CancelChild payload names only that dispatch/call/child workflow; the selected call and child become Cancelled. Without interruption, both remain live until the selected child's normal finish Event produces empty terminal ordinal one. The other original call, dispatch, activities, incidents and physical bookmark generation/scope observations remain equal throughout selected handling. A real warning-handler Event keeps the caller live during these assertions and is resumed normally.

After selected handling, the caller is still Running with exactly the original other token active and the selected token absent. The finished selected-call/dispatch/child projection is frozen. A third fresh provider verifies both the complete parent/other activity/incident/physical-wait projection and the finished selected projection unchanged. Only then does it resume the other child's actual emit Event. That child's own exact call receives ordinal zero, its interruption policy acts independently, and the selected projection remains equal. Normal warning-handler and, for non-interruption, child finish Events complete the remaining paths. Both dispatch leaves have truthful Cancelled or Completed status, the non-interrupting normal branch runs twice while the interrupting normal branch never runs, and the caller completes with its original pin/mode. Normal terminal structural-state cleanup leaves PrivateState null. All three workflows have no physical waits, incidents or poison.

## Verification and limits

Final called-control class: **24/0/0**, including these four cases and twenty prior error/escalation/ordering cases. Whole Runtime EF: **1,702/0/0**. Two behavioral production variants compile and restore byte-identically before final normal verification:

| Mutation | Failures / companion passes | Directly observed phase |
|---|---:|---|
| Resolve the first semantic work at matching node/iteration instead of the exact calling handle | 4 / 0 | Both interrupting cases lack the expected actual cancellation payload; both non-interrupting callers become Faulted instead of remaining Running during selected handling. No wrong-target cancellation or later replay under this mutant is claimed. |
| Apply one calling activity's cancellation to all waited sibling dispatches | 2 / 2 | Both interrupting cases observe two adapter control callbacks, one for each distinct calling invocation, instead of the selected call's single callback. The non-interrupting companions pass. The mutant fails before later cancellation-target and restart comparisons. |

These are six behavioral failures and two companion passes, not compile failures. An earlier fixture run completed the scenarios but incorrectly tried to enumerate active work after normal terminal PrivateState cleanup: four failures are retained as unqualified fixture mechanics. Its corrected four-case pass and the earlier 24-case regression run precede the final explicit second-semantic-token selection and are not the final adversarial-selection qualification. The final runs above use that explicit selection. Three independent bounded component audits are clean; full-PR rounds remain zero, bound six.

Snapshots cover the complete named call and dispatch records, complete child activity/incident documents and actual physical bookmark observations, plus named child workflow status/pin/persistence-mode fields. Parent/other restart comparison additionally covers complete parent activity/incident/physical-wait facts. No whole-workflow byte identity or complete variable comparison is claimed. Normal bookmark dispatch adds each physical bookmark identity to its idempotency key, so reusing the authored warning node in the second invocation does not suppress its independent resume.

These are provider restarts through real EF stores, outbox, bookmark/scheduler and released BPMN 0.5.0 paths. Builders supply compiled executable contracts. This proves overlapping same-binding execution and isolation, not simultaneous processor claims, a process kill, the public authoring compiler, HTTP Workbench or final Studio compatibility. No production source, package/authoring contract, old capture or historical receipt changes.

Reproduce:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~BpmnCalledWorkflowControlEfTests
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

Remaining T045: split/concurrent claims, actual no-capability legacy recovery and complete actual Workbench bridge. T029 ordinary compensation, final real current Studio, T032 deletion after all old writer captures and upgraded-host proof, T033–T035 guards/audits, T036–T038 final qualification/full-PR/required green exact-head checks/protected delivery/resulting-main, then separate ADR0063 stay open. Independent #2898/#2907/program production acceptance stays open; Orleans/task-runner remain deferred.
