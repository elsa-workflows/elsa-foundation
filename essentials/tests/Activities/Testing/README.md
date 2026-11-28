# Activity test archives

The classic Sequence and scheduling execution witnesses use the earlier Runtime.
Run them from the pinned checkout with its original fixtures and package locks.

```sh
archive="$(mktemp -d)"
git -c core.fsmonitor=false worktree add --detach "$archive/source" 0866f655606838d26fe8b628f1e5709b9fee3109
(
  cd "$archive/source"
  dotnet test essentials/tests/Activities/Sequence/Tests/Elsa.Activities.Sequence.Tests.csproj \
    -c Release -p:RestoreLockedMode=true \
    --filter 'FullyQualifiedName~SequenceActivityTests|FullyQualifiedName~SequenceResultVisibilityTests|FullyQualifiedName~SequenceRuntimeTests'
  dotnet test essentials/tests/Activities/Scheduling/Tests/Elsa.Activities.Scheduling.Tests.csproj \
    -c Release -p:RestoreLockedMode=true \
    --filter 'FullyQualifiedName~DelayExecutionTests|FullyQualifiedName~DelayHistoryExecutionTests|FullyQualifiedName~DurableTimerRestartCrashTests|FullyQualifiedName~RecurringTriggerSampleWorkflowTests'
)
```

Current suites keep Sequence authoring, start-entry and child-result contracts,
the typed Delay contract, authored Timer and Cron metadata and Test Run drivers,
and prepared standalone schedule registration. Actor Foundation's Native and
Kernel Host suites exercise supported workflow execution and restart behavior.
