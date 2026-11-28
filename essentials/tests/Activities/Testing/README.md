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

## Control-flow execution and JavaScript input hydration

From the same pinned checkout, run the control-flow module suite for the earlier
activity scheduler and the focused RunJavaScript test for classic input hydration.

```sh
dotnet test essentials/tests/Activities/ControlFlow/Tests/Elsa.Activities.ControlFlow.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
dotnet test essentials/tests/Activities/Scripting/Tests/Elsa.Activities.Scripting.Tests.csproj \
  -c Release -p:RestoreLockedMode=true --filter FullyQualifiedName~RunJavaScriptPreparationTests
```

Current source keeps the control-flow authoring and executable structure contracts,
including the compiled and remapped loop bytes. It also keeps the typed RunJavaScript
activity and real script evaluator assertions using the shared Activity execution context.
