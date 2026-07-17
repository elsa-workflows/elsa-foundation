# Quickstart: Verify Durable and Inspectable Detached Dispatch

Run with the absolute SDK path required by this worktree:

```bash
/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/DispatchWorkflow/Elsa.Activities.DispatchWorkflow.Tests.csproj
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Elsa.Workflows.Runtime.Tests.csproj
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.Tests.csproj
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Resumption/Tests/Elsa.Workflows.Runtime.Resumption.Tests.csproj
/usr/local/share/dotnet/dotnet test tests/Elsa/Persistence/Groundwork/Elsa.Persistence.Groundwork.Tests.csproj
/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

Provider-specific verification uses the existing Groundwork SQLite, PostgreSQL, SQL Server, and MongoDB registration/manifest suites where available in the checkout.

Completion evidence must demonstrate:

1. one Groundwork transaction contains Pending dispatch + child-start outbox + parent checkpoint marker;
2. process recreation and lease-expiry redelivery converge on one child execution;
3. lifecycle reaches Started and child terminal state after parent completion;
4. runtime-read list/get filters work and response JSON contains no forbidden values or exception material;
5. a dispatch survives either linked execution alone and becomes collectable only after both are absent;
6. readiness distinguishes ProcessLocal, DurableReady, and Unsafe partial composition;
7. Groundwork coverage ledger, manifests, fixtures, maps, and architecture audits are clean.
