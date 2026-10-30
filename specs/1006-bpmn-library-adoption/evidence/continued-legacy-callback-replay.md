# Continued legacy MI and listener callback replay

This bounded T017 increment continues the unchanged actual preview-host `parallel-mi` and `listener-rearm` database checkpoints through the released-library Foundation runtime and fresh EF providers. The [machine receipt](continued-legacy-callback-replay.json) binds the source, binaries, package versions, original compressed/raw database digests, checks and mutations. T017 remains open for its remaining fault/cancel scenarios and complete reconciliation; this is not final Workbench process-crash or Studio acceptance.

The permanent `Continued_legacy_work_callback_replay_after_pruning_preserves_exact_current_and_sibling_facts` cases copy the frozen databases and verify both digests. They never invoke the old writer or regenerate captures. Before normal continuation they prove the exact current suspended child, durable parent, nonblank semantic token, token presence and unique live-work node/token/iteration correlation. The MI case starts with three distinct iterations. The listener case has a completed prior invocation and a different current invocation at the same node and null iteration.

A normal exact Event resume completes the selected current child. A passive wrapper observes the actual normal parent-completion scheduler handler's work-item input, forwarding its name, eligibility and handling unchanged. The child receives that exact processed-callback receipt; the original semantic token is removed by released pruning. Every other original activity retains its complete durable facts, and every original surviving bookmark retains state, physical generation and scope. MI keeps its other two waits. The listener creates a different invocation at the same node/null iteration.

After disposing the first provider, a fresh provider over the same database receives the identical captured actual callback. The test positively checks that exact item is queued, sweeps it, and immediately requires an empty queue and poison store. The adapter is not evaluated again, the workflow stays Running, and **all durable activity facts and complete physical bookmark observations** remain equal. Completing the remaining two MI waits or the original listener host wait finishes the workflow without bookmarks.

The initial dispatcher-level observation saw no selected callback and failed both cases. It was replaced with observation at the actual normal scheduler handler input; no callback payload or old state was invented. Review then corrected two evidence gaps: after-only token absence could be vacuous, and bookmark content alone omitted replacement generation. Both positive preconditions and physical observations are now required.

Final verification: **focused cases 2/0/0; whole Runtime EF 1,651/0/0**. Model and Semantics 0.5.0 are verified in the built dependency closure. Two production-source mutations were tested in isolated in-checkout outputs, then restored byte-identically:

| Mutation | Actual result | Scope |
|---|---|---|
| Ignore the processed parent-completion receipt | Both cases fail at complete activity-fact equality after fresh-provider replay. | Proves the replay phase was reached and detects repeated effects. |
| Resolve child by first node/iteration match | Listener fails during initial recovery/capture; MI passes. | Proves same-node/null-iteration history sensitivity. It does not claim that this mutant reached replay. |

Frozen authoring and both original captures are unchanged. Three independent bounded source/actual/portable audits are clean; full-PR rounds remain **zero**, bound six. Reproduce from the committed source using normal checkout outputs:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --configuration Release -p:RestoreLockedMode=true --filter FullyQualifiedName~Continued_legacy_work_callback_replay
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --configuration Release -p:RestoreLockedMode=true
```

This qualifies actual captured-checkpoint continuation and scheduler-callback replay across fresh provider restart. It does not qualify outbox acknowledgement loss, full-process Workbench restart, old-binary running rollback, final current Studio or production acceptance. Existing queued-loser/late-fault receipts remain separate. Remaining T017, native-composite/combined/called-control gates, semantic writer deletion/guards, complete-PR review, required exact-head checks/protected merge/resulting-main, separate ADR0063 and independent #2898/#2907/program production evidence remain open. Orleans/task-runner remain deferred.
