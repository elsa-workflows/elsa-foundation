# Called BPMN escalation and duplicate delivery through EF

This bounded **T045 increment** adds eight actual-runtime cases: interrupting/non-interrupting escalation, running/terminal child emission, and Durable/Balanced persistence. They use real published executable/source records, DispatchWorkflow, a separately dispatched BPMN root, normal control production/delivery and released BPMN 0.5.0 over SQLite. The [machine receipt](called-escalation-ef-recovery.json) binds final source, binaries, positive runs and restored behavioral controls. T045/T029 remain open.

The warning handler is a real Event wait. It stays Suspended while the caller stays Running, so checking a child's cancellation cannot accidentally rely on terminal caller cleanup. After disposing the first provider, a fresh one receives the **identical actually dispatched intent**, using its retained original outbox authority. Complete committed caller/child activity and incident documents, named workflow status/pin/mode fields and exact physical bookmark observations remain equal. The original Delivered source document also remains equal. A passive wrapper forwards all six BPMN runtime methods and records actual ControlAsync calls: initial ordinal zero evaluates once for the exact calling invocation; duplicate delivery adds no adapter evaluation.

| Child emission | Boundary | State while warning handler waits |
|---|---|---|
| Running escalation followed by child Event wait | Non-interrupting | Caller Running, dispatch Suspended, child Running with its original physical Event wait. Completing the warning handler still leaves the caller waiting for the child. |
| Running escalation followed by child Event wait | Interrupting | Caller Running, dispatch Cancelled, child Cancelled and its physical wait gone. An actually dispatched cancellation payload names the exact dispatch/call/child IDs. |
| Escalation end event | Non-interrupting | Caller Running at warning handler; child and dispatch Completed, normal branch runs once. |
| Escalation end event | Interrupting | Caller Running at warning handler; child and dispatch Completed, normal branch never runs. No cancellation intent is dispatched for the already terminal child. |

All four combinations run in both modes. The non-interrupting paths also dispatch no cancellation intent. Warning-handler state and its physical wait are positively present before restart/duplicate in every case. Cancellation assertions concern this linked child; concurrent same-binding isolation remains a separate T045 obligation.

The caller's exact inbox has cursor one, no pending batch and a terminal flag matching the original emission. Interrupted running calls retain the consumed nonterminal prefix on their Cancelled leaf; these tests do not invent a subsequently consumed terminal inbox there. Initial actual control has one unchanged WARN escalation notification, ordinal zero, matching call/child identity and no authored error. Source intent and complete Delivered row are independently read from EF.

After duplicate verification, the real warning Event resumes. Only the non-interrupting running call remains Running. Its original child Event then resumes through the normal bookmark dispatcher. The actual child produces a **fresh empty terminal ordinal one** for the same root/dispatch; normal caller control evaluates ordinal one exactly once, consumes it with cursor two/no pending batch, runs the normal branch once and completes. The empty barrier is delivered in order here; this is not the separately required terminal-before-earlier-escalation gap witness. Other combinations finish their caller after the warning wait is released. Final caller/child status, pins and persisted modes are checked again; no incidents, poison or physical waits remain.

Snapshot scope is explicit: complete activity/incident documents, physical bookmark observations (including real positive generation/scope witnesses), and named workflow status/pin/mode. It does not claim whole-workflow byte identity or complete workflow-variable comparison. Duplicate execution is normal dispatcher delivery of an original real message with retained source, not a fabricated source claim, a newly claimed outbox retry or guaranteed scheduler-handler re-entry. Actual adapter evaluation is counted; internal early-return locations are not inferred from a state comparison. These are provider restarts, not process kills.

Final focused **12/0/0** includes the eight new cases and all four previously delivered coded-error/ACK-loss cases under the passive observer. Whole Runtime EF is **1,690/0/0**. Two caller-adapter mutations run against the eight escalation rows, compile and fail behaviorally, then restore production source byte-identically:

| Mutation | Failures / companion passes | Actual phase |
|---|---:|---|
| Omit explicit CancelWorkSubtree application | 2 / 6 | Both running-interrupting cases find no actual targeted child-cancel intent. This precedes their child-status assertions; no mutant child-status check is claimed. |
| Pass a foreign notification code to the library | 8 / 0 | Actual source production/delivery and ControlAsync entry occur. The two running-interrupting rows fail first on absent cancellation intents; the other six fail on the missing warning handler. All fail before fresh-provider duplicate replay. |

The first two positive focused runs used a completing warning probe and passed twelve cases. They established basic routing but did not isolate explicit cancellation from terminal workflow cleanup. The final fixture deliberately parks the warning handler and reruns the matrix; the earlier runs are not used as proof of that stronger cut. No production implementation or contract changed to fit an expectation. Historical coded-error, native, combined and legacy receipts remain unchanged and source-specific; the new twelve-case run requalifies the shared test observer path.

Reproduce:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~BpmnCalledWorkflowControlEfTests
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

Builders supply compilation output, not public compiler/HTTP host proof. Three independent bounded component audits support this increment; full-PR rounds remain zero. Remaining T045 includes signal/failure precedence, concurrent same-binding calls and cancellation isolation, split/concurrent claims, terminal-success/ordinary-fault empty barriers delivered before an earlier escalation with gap-buffered restart, deliberate split-terminal mutation, no-capability legacy recovery and actual Workbench. T029 ordinary compensation, final real current Studio, old writer/duplicate semantics deletion and guards, final qualification/full-PR/required exact-head checks/protected delivery/resulting-main and separate ADR0063 remain open. Independent #2898/#2907/program production evidence stays open; Orleans/task-runner deferred.
