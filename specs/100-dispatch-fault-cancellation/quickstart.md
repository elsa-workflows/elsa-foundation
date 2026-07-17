# Quickstart: Verify DispatchWorkflow Fault and Cancellation

Use the absolute SDK path required by this worktree:

```bash
/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
```

## Required scenarios

1. Complete linked waited children as Faulted and Cancelled. Redeliver terminal work three times and verify one parent result/outcome, zero partial outputs, and only allow-listed diagnostics.
2. Leave the Faulted/Cancelled outcome unconnected. Verify DispatchWorkflow completes normally and creates no implicit parent incident.
3. Use barriers to force parent-cancellation-wins and child-admission-wins orders, then repeat concurrent admission/cancellation at least 100 times. Verify cancellation-wins never calls start; admission-wins records one durable Cancel responsibility.
4. Deliver child Cancel before child visibility, after visibility, and after each terminal status. Verify retry then idempotent acknowledgement without terminal clobbering.
5. Repeat child-start, child-cancel, terminal, parent-resume, and parent-cancel delivery at least three times.
6. Verify explicit propagation false and every fire-and-forget case create zero child-cancel work and let the child continue.
7. Recreate Groundwork services at every admission, cancellation directive, outbox claim, command, terminal notification, and acknowledgement boundary.
8. Re-run successful wait, fire-and-forget, unsupported-kind, runtime cancellation, and architecture suites.

## Completion evidence

- Faulted/Cancelled result JSON contains no output values, exception detail, stack trace, diagnostic payload, or arbitrary incident metadata.
- One checkpoint atomically contains parent cancellation and child cancellation responsibility.
- Exactly one provider race winner is visible after restart.
- Cancel commands use one deterministic command/envelope/idempotency identity.
- Architecture checks show no #681, #682, #683, broker, Studio, or WorkflowDefinitionActivity expansion.
