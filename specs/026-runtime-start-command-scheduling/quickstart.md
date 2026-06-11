# Quickstart: Runtime Start Command Scheduling

> Supersession note (2026-06-11): this quickstart's start-node scheduling expectation is
> superseded by
> [070-workflow-root-activity-contract](../070-workflow-root-activity-contract/spec.md). Start now
> schedules the executable root activity.

Run the focused validation:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build src/Elsa/Workflows/Runtime/Core/Elsa.Workflows.Runtime.Core.csproj
dotnet build src/Elsa/Workflows/Runtime/Api/Elsa.Workflows.Runtime.Api.csproj
git diff --check
```

Expected result: a runtime `Start` command accepted by the execution agent is recorded as scheduler work, then the start scheduler handler enqueues `ScheduleActivity` work for the pinned executable artifact's start nodes. No activity bodies execute in this slice.
