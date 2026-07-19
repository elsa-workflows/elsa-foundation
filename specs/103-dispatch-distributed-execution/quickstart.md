# Quickstart: Verify Dispatch Distributed Execution

Use `/usr/local/share/dotnet/dotnet` for every command.

1. Run the shared two-node acceptance tests proving a parent dispatch checkpoint/outbox committed on one node can produce child execution on an eligible distributed node through the existing workflow start dispatcher and distributed actor provider.
2. Run duplicate and stale-placement tests proving one dispatch record, one child workflow execution identity, and no state regression.
3. Run restart tests where either node restarts after durable child-start intent creation and before or after child materialization.
4. Run readiness tests proving in-memory development, durable single-node Groundwork, and distributed Groundwork are distinguished.
5. Run DispatchWorkflow regression tests proving local in-process behavior and public activity inputs/outcomes remain unchanged.
6. Run Architecture tests proving no broker, service-bus, routing-channel, priority, affinity, or transport-selection dependency enters the activity contract.

Expected project commands:

```bash
/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Distributed/Tests/Elsa.Workflows.Runtime.Distributed.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Distributed/Persistence/Groundwork/Tests/Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
```

Completion requires green tests, no activity contract transport controls, no broker/Studio/WorkflowDefinitionActivity expansion, and a clean local commit.
