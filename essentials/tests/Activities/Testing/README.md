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

## Classic Flowchart execution

The same pinned checkout retains the classic Flowchart scheduler, checkpoint
and join-policy fixtures, including both historical join policy inputs.

```sh
dotnet test essentials/tests/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj \
  -c Release -p:RestoreLockedMode=true
```

Current Flowchart tests keep the authoring, routing, start-entry, policy and
serialized state contracts. The Behavioral suite keeps the reusable Graph
boundary outcome case. The Actor Foundation Workbench checks exercise retained
authored Flowcharts on SQLite and PostgreSQL.

## Classic full-library activity drives

The pinned Behavioral suite drives the previous engine and records its declared
outcomes, outputs and missing-input failures. It includes the complete classic
DispatchWorkflow outcome matrix, including cancellation and terminal delivery failure.
The classic BPMN gateway-loop occurrence case uses the same archive.

```sh
dotnet test essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj \
  -c Release -p:RestoreLockedMode=true \
  --filter 'FullyQualifiedName~ActivityOutcomeReachabilityTests|FullyQualifiedName~ActivityOutputPopulationTests|FullyQualifiedName~RequiredInputEnforcementTests|FullyQualifiedName~ActivityDriveHealthTests'
dotnet test essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj \
  -c Release -p:RestoreLockedMode=true \
  --filter 'FullyQualifiedName~A_reader_beside_its_producer_in_a_sequence_on_a_bpmn_gateway_loop'
```

Current Behavioral tests keep the separate Graph execution, member-read and
activity contract checks. Supported Actor workflow execution is exercised by the
Native and Kernel Host suites.
