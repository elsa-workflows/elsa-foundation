# Actual legacy MI/listener fault continuation and terminal replay

Two supplemental actual preview-host databases complete T017's fault coverage. Their [capture source/control recipe](legacy/README.md#supplemental-actual-typed-milistener-fault-checkpoints) preserves the exact old runtime/writer/pins and adds a real test-only typed wait leaf. This leaf is byte-identical in the old capture and upgraded consumer. Both [machine receipts](legacy/parallel-mi-outcome.json) ([listener](legacy/listener-rearm-outcome.json)) and the [qualification receipt](legacy-outcome-fault-replay.json) bind source, patch, raw/compressed data, dependencies, binaries and results. The prior six captures and frozen authoring fingerprint are unchanged.

The permanent `Actual_legacy_outcome_wait_fault_preserves_siblings_and_terminal_replay_is_purged` cases first reopen exact database copies through fresh released BPMN 0.5.0 EF hosts. They positively establish the current Suspended child, exact parent, nonblank token, node/iteration/live-work relationship, and either three distinct MI iterations or a prior Completed/current Suspended same-node/null-iteration listener pair. A normal typed resume returns the real child fault. A passive forwarding wrapper records the actual fault callback at the ordinary scheduler handler input; its exact parent/child payload, ChildFaulted metadata and original incident identity are checked.

Only the selected semantic work binding is removed. Every other original activity retains its complete durable facts; every surviving original bookmark retains its full state, physical generation and scope. The selected bookmark is absent, the faulted child's bookmark list is empty, and the physical wait count decreases exactly once. The v1 writer shape remains readable by the original production reader. The original child code/message remain `LEGACY-OUTCOME-FAULT` / `Actual legacy outcome wait faulted.` and its incident stays blocking with the actual FaultWorkflow resolution. The released unhandled parent verdict is `bpmn.work.faulted`, versus the old control's `bpmn.child.faulted`.

After disposing the provider, a fresh provider receives the identical captured fault callback. The test proves queue presence, then immediate consumption, no poison, no handler entry, no adapter evaluation and no activity, physical wait, incident or workflow-status changes. The workflow is already Faulted. **This is terminal-workflow queue purge, not a processed-completion receipt guard.** The real fault path has no ordinary ParentCompletionProcessedWorkItemId; the test explicitly checks its absence. Separate [continued completion replay](continued-legacy-callback-replay.md) and [canceled-token late-fault replay](late-fault-upgrade.md) qualify the live-workflow guard after pruning.

Verification: **old export/control 2/0/0; upgraded focused 2/0/0; whole Runtime EF 1,653/0/0**. Source review strengthened count-only capture predicates to exact identity assertions; consumer review added positive selected-wait retirement. Initial unqualified attempts are retained in the receipt: a native Sequence intentionally stayed Running after its leaf fault, typed trigger JSON needed its explicit property name, self-cancellation emitted no parent completion callback, ordinary processed-completion receipts were absent on propagated faults, and the released parent verdict uses the work terminology. No production change was made to fit those assumptions. The final cases claim fault continuation and terminal purge only; the fixture's unused cancel branch is not cancellation evidence.

Reproduce the upgraded cases with normal checkout output:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj \
  -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~Actual_legacy_outcome_wait_fault
```

This is actual captured-database continuation across fresh EF providers. It does not qualify combined-outcome ACK loss, full-process Workbench crash recovery, final current Studio, running old-binary rollback or production acceptance. Full-PR rounds remain zero. T017 closes within the reconciliation below; all separate native-composite/control/deletion/final delivery/ADR0063/program gates remain open until their own evidence is accepted. Orleans/task-runner remain deferred.

## T017 reconciliation

| Required part | Actual evidence | Boundary |
|---|---|---|
| Canceled work and queue order | Original resolved-race/queued-loser captures, strict queue-before-cancellation control and upgraded recovery. | Semantic cancellation of real work; no invented self-cancel callback. |
| Late completion/fault replay after pruning | [Queued-loser](queued-loser-replay.md) and [late-fault](late-fault-upgrade.md) receipts. | Identical original callback, surviving receipt, fresh provider, live parent, unchanged original fault/incident truth. |
| Parallel MI completion/replay | [Continued replay](continued-legacy-callback-replay.md), original parallel-mi capture. | Three exact node/token/iteration bindings; selected continuation and pruning, other original waits unchanged; live-parent guard. |
| Same-node/null-iteration rearm | Original listener-rearm capture, current continued replay and retained first-node/iteration mutation. | Prior Completed and current distinct invocation; real rearm and exact replay. Mutant fails initial recovery/capture, not replay. |
| MI/listener typed fault and replay | New two actual captures and final cases described above. | Only selected work/bookmark retired; sibling facts unchanged; terminal replay is purge, not receipt guard. |

The current whole EF project passes 1,653/0/0, including all 17 `BpmnUpgradeRecoveryEfTests` cases; the focused new fault pair passes 2/0/0. The original six captures and prior receipts remain frozen at their source revisions; this reconciliation supersedes their historical statements that T017 remained open. Three independent bounded correctness, standards and acceptance audits found no remaining T017 scenario gap. These are component audits, not full-PR review rounds or final actual Workbench/Studio acceptance. T018 retains its already separate qualification.
