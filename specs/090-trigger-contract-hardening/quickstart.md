# Quickstart: Validate Trigger Publication Hardening

## Prerequisites

- .NET 10 SDK available.
- Repository dependencies restored.
- Feature implemented according to [plan.md](plan.md).

## 1. Shared provider and index contract

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --filter "FullyQualifiedName~WorkflowTriggerBindingExtractorTests|FullyQualifiedName~WorkflowTriggerIndexerTests"
```

Expected:

- zero-provider and multiple-provider claims fail with contextual preflight errors;
- one provider id is recorded for registered and intentionally non-starting outcomes;
- one invalid node preserves all prior bindings;
- `Recognized([])` succeeds with no binding.

## 2. Event, Timer, Cron, and HttpEndpoint provider matrix

```bash
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --filter "FullyQualifiedName~EventTriggerStimulusProviderTests"
dotnet test tests/Elsa/Activities/Scheduling/Tests/Elsa.Activities.Scheduling.Tests.csproj --filter "FullyQualifiedName~TimerCronProviderTests"
dotnet test tests/Elsa/Activities/Http/Tests/Elsa.Activities.Http.Tests.csproj --filter "FullyQualifiedName~HttpEndpointTriggerStimulusProviderTests"
```

Expected: each provider satisfies its row in [trigger-contract-matrix.md](contracts/trigger-contract-matrix.md), including HTTP's explicit non-start case.

## 3. Recurring preflight ordering

```bash
dotnet test tests/Elsa/Workflows/Runtime/Scheduling/Tests/Elsa.Workflows.Runtime.Scheduling.Tests.csproj --filter "FullyQualifiedName~RecurringTriggerScheduleIndexerTests"
```

Expected:

- complete schedules are materialized before the inner indexer runs;
- exhausted Cron and invalid Timer/Cron inputs fail before prior bindings or schedules change;
- successful republish still replaces old schedules;
- an inner indexing failure leaves schedules unchanged.

## 4. Publication and compatibility

```bash
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --filter "FullyQualifiedName~PublishWorkflowTriggerIndexingTests|FullyQualifiedName~WorkflowExecutableCompilerTests"
dotnet test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj --filter "FullyQualifiedName~ClrAssemblyScannerTests"
```

Expected:

- invalid publications preserve seeded trigger/schedule registrations;
- legacy catalog rows compile with correct trigger projection;
- same-version reconciliation hashes remain stable;
- existing executable shapes remain readable.

## 5. Boundary and full-suite gate

```bash
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
dotnet build Elsa.Server.slnx
dotnet test Elsa.Server.slnx
```

Expected: no Runtime → Design dependency, no warnings or errors, and all existing tests remain green. If implementation changes a Groundwork-persisted record despite the plan, stop and add a schema version, upcaster, and historical/current golden fixtures before accepting the result.
