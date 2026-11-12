# BPMN combined outcome durability through EF

This closes T024's actual-runtime/fresh-provider durability scope. The permanent `Bpmn_combined_outcome_survives_failed_enqueue_parent_commit_before_ack_and_competing_fresh_providers` case uses the actual Foundation BPMN adapter and released Model/Semantics 0.5.0. It extends the existing combined-outcome EF test class and reuses its failure/claim controls; the original custom-parent cases remain active. No production code, public authoring contract or persistence authority changes. The [machine receipt](combined-outcome-ef-recovery.json) binds source, binaries, package closure, results and restored mutations.

The real BPMN graph starts one structural child that emits two ordered escalation notifications and returns its original typed fault. An interrupting boundary routes to a native Event wait. The first ordinary outbox dispatch fails before queue acceptance, retaining one FailedRetryable source, the combined payload and original blocking incident. The workflow stays Running; no parent outcome has been evaluated. Exact parent/child IDs, the nonblank reporting token, its preexisting active-work binding, both ordered payload ordinals and original incident identity are positively checked.

A second EF provider claims the due real source. Its dispatcher forwards the actual enqueue, then pauses before returning to acknowledgement recording. The physically queued work equals the original saved work. The source is Delivering with a positive fence; its attempt count is still one because the counter records completed results. A concurrent normal recovery sweep runs the actual BPMN combined callback. Before releasing the dispatcher, concrete EF reads must see the exact processed-work receipt, native trailing Suspended activity and its physical bookmark, including generation and scope. The source remains Delivering with the same fence, the dispatcher task remains incomplete and its release gate is unset. Thus parent and trailing-wait commits genuinely precede acknowledgement.

The first notification retires the reporter; the second remains part of the one ordered outcome and does not start another handler. The reporter token is pruned. The original child fault document is unchanged, and only its original incident is resolved by AbsorbFault with StructuralFaultAbsorption provenance, exact BPMN parent and reason `bpmn.work.late-fault-absorbed`. A passive adapter wrapper observes one actual OutcomeAsync call and forwards it unchanged.

Releasing the gate throws a deterministic timeout **after real queue acceptance and the observed parent commit**. The ordinary processor records FailedRetryable result two; no delivered ACK was recorded. After disposal and the normal retry delay, two separately constructed fresh EF providers enter competing delivery passes over the same database. Their existing barrier synchronizes processor entry, not an instrumented same-SQL CAS conflict. Exactly one pass attempts/delivers the source, both report zero failures, and the source becomes Delivered on result three. Its saved callback is unchanged. There is no second BPMN outcome evaluation, queued/poison residue, new activity, physical bookmark replacement or incident change. Completing the one original trailing Event finishes the workflow once.

All four EF providers use the real SQLite runtime stores and shell/type initialization. A separate disposable in-memory fixture is used only to construct the graph with the existing builder API; it does not execute this proof. The database is test-owned and deleted after all providers dispose. No synthetic checkpoint, callback, fault or incident is inserted.

Verification: **complete combined-outcome class 3/0/0; whole Runtime EF 1,654/0/0**. Two isolated production-source mutations each fail one behavioral assertion, with no compile failure, then restore byte-identically before the final normal runs:

| Mutation | Actual failure | Scope |
|---|---|---|
| Bypass the exact processed-parent-outcome guard | The competing fresh-provider retry records two actual BPMN outcome calls instead of one. | Reaches the retry phase and detects repeated adapter evaluation. |
| Omit the exact incident-absorption request | The original incident remains blocking at the committed-parent cut. | Fails before acknowledgement release; it is not claimed as a replay-phase failure. |

The initial compile-only attempt is unqualified. The first actual attempt corrected the in-flight counter expectation; the next exposed that activity/receipt observations alone can precede the physical bookmark. The final cut positively waits for that exact bookmark. Review also fixed a sampling race: if recovery finishes after the first read, the helper takes a final fresh physical read before deciding the required cut is missing. These corrections strengthen the witness; production behavior was not altered to fit an expectation.

Reproduce with normal checkout output:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~CombinedChildOutcomeEfTests
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

T017 and broadcast T049/T050/T051 retain their separately source-bound qualification; all old captures and the frozen fingerprint remain unchanged. Three independent bounded source/artifact audits support this T024 gate; full-PR review rounds remain zero. This is one actual BPMN combined-outcome durability witness, not completion of T021/T022's full semantic matrix, T025 native-parent qualification, full-process Workbench crash recovery, final real Studio, running binary rollback, required exact-head checks/protected delivery/resulting-main, separate ADR0063 or independent production/program acceptance. The deterministic after-effect timeout is not a process-kill claim. Orleans/task-runner remain deferred.
