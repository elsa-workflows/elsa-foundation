# Ordered called control and indivisible terminal batches through EF

This bounded **T045 increment** adds eight actual-runtime cases in Durable and Balanced modes: terminal success and ordinary-fault empty barriers delivered before an earlier escalation, and one terminal batch carrying both WARN notification and authored OTHER failure under each interruption policy. All use real published/pinned DispatchWorkflow/BPMN roots, ordinary producers/outbox/bookmark/inbox paths and released BPMN 0.5.0 over SQLite. The [machine receipt](called-control-ordering-ef-recovery.json) binds final source, binaries, runs and restored mutations. T045/T029 remain open.

## A persisted gap cannot advance the caller

The child first produces actual nonterminal ordinal zero with WARN, then parks at a real Event. A forwarding dispatcher deliberately refuses that notification before its effect. Its real outbox records retryable results under UntilAcknowledged policy; no fabricated claim or stored control is installed. The actual caller is Running at its Suspended dispatch leaf, with no warning or normal branch execution.

The real child Event then resumes. Either the child completes, or a real ordinary faulting fixture leaf faults it. Its actual terminal result contains **fresh ordinal one, terminal, zero notifications and no authored failure code**. The ordinary fault remains ordinary; it is not promoted into authored BPMN classification. Terminal delivery succeeds while zero remains retryable. The dispatch leaf is Completed with its truthful Completed/Faulted outcome; its processed-parent receipt is nonempty. The exact terminal transport is durably Pending in its inbox at cursor zero/terminal-not-consumed. The caller stays Running, its complete BPMN private-state envelope is unchanged from before terminal delivery, neither branch has started, and the passive adapter observer sees **zero ControlAsync calls**.

After disposing that provider, a fresh one positively reads the same complete buffered activity/incident/physical-bookmark and named workflow status/pin/mode projection. With zero still deliberately unavailable, actual normal retry increases its attempt count, while the pending terminal, original completion receipt, caller semantics and complete Delivered terminal-source row stay equal. No adapter call or early work occurs. The held source intent stays equal and retryable; its source is retained even though the child is already terminal.

Release zero through the normal processor. Exactly one real adapter callback receives **[0, 1]** for the exact calling invocation. Both sources are independently authorized by the production path; inbox cursor becomes two/terminal-consumed with no pending batch, and the original processed-parent receipt is retained. Zero becomes Delivered; the previously Delivered terminal source stays byte-equivalent under the complete serialized row projection. Original child activity/incident documents remain equal across caller evaluation.

For success, the adapter returns Defer and stages exactly the warning and normal branches; the normal branch completes while the warning Event waits, and normal warning resume completes the caller. For ordinary fault, the adapter returns Fault with **zero staged schedules** before generic runtime application. The caller faults as `bpmn.call-activity.faulted` without invented authored classification, and neither warning nor normal work ever runs. The child's original faulting leaf, root fault and blocking incidents remain truthful and unabsorbed. Final status, pins and mode are checked, with no poison or physical waits.

## Notification and authored failure remain one terminal decision

A separately dispatched BPMN root forks to an escalation end and an OTHER error end in one actual interpreter evaluation. Its real terminal ordinal zero contains WARN notification **and** authored OTHER failure together, with Faulted dispatch status and separate safe diagnostics. A controlled outage before delivery leaves the actual caller waiting and child Faulted with its original blocking incident.

A fresh provider delivers the unchanged actual terminal source. One ControlAsync receives ordinal zero. The actual returned verdict and staged schedules/cancellations are observed before generic runtime application. With a non-interrupting boundary, the uncaught authored failure wins: Fault, no staged starts, no handler/normal execution and preserved OTHER classification. With an interrupting boundary, the first interruption retires that semantic call and retains only the warning path: Defer with one staged warning Event, then caller completion after normal warning resume. In both cases, the dispatch leaf completes normally with Faulted outcome/no local leaf fault, the exact inbox consumes terminal zero, and the already terminal child is never recancelled or rewritten. Its complete original fault/incident documents remain equal. No foreign incident is absorbed.

## Verification and limits

Final focused **20/0/0** includes eight new cases plus all twelve previously delivered called-error/escalation cases under the extended passive verdict observer. Whole Runtime EF is **1,698/0/0**. Three production variants compile, fail behaviorally, and restore byte-identically before final normal runs:

| Mutation | Failures / companion passes | Actual phase |
|---|---:|---|
| Initialize a new inbox cursor from incoming ordinal instead of zero | 4 / 0 | Terminal one incorrectly invokes ControlAsync before missing zero; the zero-callback assertion fails before the intended buffered restart. |
| Supply only the first ready item while retaining terminal completion outcomes | 4 / 0 | After real buffered restart/release, no ControlAsync callback is observed and the single-callback assertion fails. The source contract rejects nonterminal-only batches with terminal outcomes; no partial [0] callback or downstream verdict is claimed. |
| Drop terminal interpretation when a batch also has notifications | 2 / 2 | Both non-interrupting callers incorrectly remain Running instead of Faulted after real recovered delivery. The two first-interruption controls retain their intended behavior. |

These are ten behavioral failures/two companion passes, not compile failures. The third is the deliberate split-terminal-half control against a real notification-plus-failure batch. No production source, package/authoring contract or old checkpoint changed to fit a test. Historical receipts remain frozen and source-specific; current twenty-case verification requalifies shared passive test observation.

Snapshots explicitly cover complete activity/incident documents, actual physical bookmark observations and named workflow status/pin/mode. They are not whole-workflow byte identity or complete variable comparison. The pre-terminal comparison targets the complete BPMN private-state envelope, not incidental activity metadata. Claimed processing/retry and buffering use actual EF stores/processors; infrastructure unavailability is controlled by a forwarding dispatcher. These are service-provider restarts, not process kills. Builders supply compiler output rather than proving the public compiler or HTTP Workbench. Callback observations do not infer unobserved scheduler-handler re-entry.

Reproduce:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~BpmnCalledWorkflowControlEfTests
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

Three independent bounded component audits support this increment; full-PR rounds remain zero. Remaining T045 includes concurrent same-binding calls/cancellation isolation, split/concurrent claims, no-capability legacy recovery and actual Workbench proof. T029 ordinary compensation, final real current Studio, legacy writer/duplicate semantics deletion and guards, final qualification/full-PR/required exact-head checks/protected delivery/resulting-main and separate ADR0063 stay open. Independent #2898/#2907/program production acceptance remains open; Orleans/task-runner deferred.
