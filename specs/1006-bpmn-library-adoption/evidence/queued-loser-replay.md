# Actual queued-loser callback replay after pruning

The permanent EF test `Actual_legacy_queued_loser_callback_is_not_re_evaluated_after_pruning_and_provider_restart` consumes the frozen `legacy/queued-loser.sqlite.gz` database from the actual preview host. Both compressed and raw database digests are verified; normal tests never run or replace its writer. The exact original losing CompleteActivity work item is read through the durable scheduler queue contract before recovery.

The first upgraded host evaluates that original callback exactly once, retains the processed work-item receipt, prunes its canceled token, and parks the trailing catch. After disposing the provider, a fresh upgraded provider over the same database receives the identical captured work item. The test asserts the exact queued item exists before the sweep and is absent immediately afterward, with no poison record, extra adapter evaluation, state change, child or bookmark change; the parent remains Running. Resuming the final catch then completes the process with no bookmarks.

An omitted-replay-sweep mutation fails at the immediate queue-empty assertion. A separate mutation disabling the generic processed-completion guard faults the captured recovery before the replay phase; it is evidence of receipt sensitivity, not a claim that the second replay phase was reached under that mutation. Both mutations were removed. Whole runtime EF: **1016 passed, zero failed/skipped**.

Reproduce from this commit with:

```sh
dotnet test Essentials/tests/Workflows/Runtime/Persistence/EntityFrameworkCore/Tests/Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests.csproj --filter FullyQualifiedName~Actual_legacy_queued_loser_callback
```

This qualifies duplicate scheduler-callback consumption across provider restart after semantic pruning. It does not qualify outbox acknowledgement loss, combined fault-and-signal durability, a real late child fault or running rollback. T017/T018 remain open for their remaining cases.
