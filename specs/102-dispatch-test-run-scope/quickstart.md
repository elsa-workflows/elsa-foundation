# Quickstart: Verify Dispatch Test-Run Scope

Use `/usr/local/share/dotnet/dotnet` for every command.

1. Run DispatchWorkflow tests proving draft-parent/Published-child identity, run-kind/scope inheritance, detached parent-completion independence, waited outcome parity, and detached before/after-admission cleanup.
2. Run Runtime tests proving scope model/lifecycle guards, start/checkpoint propagation, atomic open-scope assertion, bounded cleanup, duplicate convergence, and legacy fail-closed behavior.
3. Run Publishing API tests proving scope creation, internal close coordination, idempotent dispositions, and tenant-scoped rejection; run Runtime API tests proving the inherited run kind is inspectable without exposing test-scope context.
4. Run Resumption tests proving expired/Closing scope sweeps continue after restart and do not require workflow mailboxes.
5. Run Groundwork tests proving persisted scope/query indexes, before/after-materialization teardown, response-loss replay, concurrent cleaners, terminal races, and provider recreation.
6. Run Architecture tests after updating storage coverage ledgers; generated maps remain unchanged until explicitly invoked by the user.

Expected full projects:

```bash
/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
```

Completion requires zero production/cross-scope/cross-tenant/cross-partition cancellations and no #683, broker, Studio, or WorkflowDefinitionActivity expansion.
