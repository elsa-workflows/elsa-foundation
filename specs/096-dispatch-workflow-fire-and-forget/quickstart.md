# Quickstart: Verify DispatchWorkflow Fire-and-Forget

Use the absolute SDK path in this environment:

```bash
/usr/local/share/dotnet/dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --no-restore
/usr/local/share/dotnet/dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore
```

Completion evidence must show:

1. full stable activity schema/defaults/options metadata;
2. tenant-scoped, unambiguous live Published options;
3. exact artifact/source pin in compiled node metadata;
4. one atomic parent completion/dispatch/outbox checkpoint;
5. parent continuation before child materialization;
6. global outbox delivery through the real start dispatcher and actor provider;
7. duplicate convergence and distinct activity-execution identities;
8. correlation, lineage, tenant/partition, authority, root initiator, and run-kind inheritance;
9. no raw inputs in dispatch inspection state;
10. no broker, Studio, Design-on-runtime, Composition runtime, or construct-only workflow-definition activity dependency.

Document the architecture suite’s unrelated baseline failures separately; do not describe a partially failing suite as green.
