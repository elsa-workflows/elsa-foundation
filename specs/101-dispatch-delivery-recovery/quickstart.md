# Quickstart: Verify Dispatch Delivery Recovery

```bash
/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Api/Tests/Elsa.Workflows.Runtime.Api.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
```

## Required scenarios

1. Fail transiently three times then succeed; verify positive retry schedules and invariant identities.
2. Explicitly reject; verify immediate safe final failure without raw reason.
3. Exhaust wait mode; verify atomic dead letter + `DispatchFailed` + resume item, then triple-deliver for one normal outcome with zero outputs/faults.
4. Exhaust fire-and-forget; verify no parent mutation/resume and allowlisted inspection only.
5. Reject every wait/noneligible redrive before mutation.
6. Redrive an eligible detached failure; verify original item/intent/payload/policy/identities and advanced generation/fence.
7. Race at least 100 duplicate/different redrives and stale completions; verify one current generation.
8. Recreate Groundwork around retry, finalization, resume, redrive, admission, and acknowledgement boundaries.
9. Run sensitive exception/reason/payload/context corpus; verify no leakage in persistence/API/telemetry.
10. Re-run all successful start/wait/detached, child terminal, parent cancellation, unsupported intent, retention, and architecture suites.

## Completion evidence

- Retry is host-configured and absent from activity schema.
- Exhaustion yields one dead letter/incident per generation.
- Wait resumes once and is never redrivable.
- Detached redrive preserves canonical identity and never reopens parent.
- Read/manage authorization is distinct and tenant scope fails closed.
