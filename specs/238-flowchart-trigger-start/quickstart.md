# Quickstart: Verify Spec 238

Build and test only the touched projects (shared machine). Run from the repository root of the w238 worktree.
Allow for `[dotnet-build-slots] waiting for a build slot`.

## 1. Runtime: Flowchart starts at the trigger

```bash
dotnet test Essentials/tests/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj
```

Expect the new trigger-start tests to pass alongside the existing suite, with no existing test changed.

```bash
dotnet test Essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj --filter "FullyQualifiedName~FlowchartTriggerStart"
```

This covers HTTP + HTTP and HTTP + Cron. The fired trigger's branch runs, the other trigger never runs, and no
bookmark is left.

```bash
dotnet test Essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --filter "FullyQualifiedName~FlowchartTriggerStart"
```

This covers a Flowchart in a `For` loop, which restarts at the trigger on every pass.

## 2. End to end: publish, fire the second endpoint

```bash
dotnet test Essentials/tests/Activities/Http/IntegrationTests/Elsa.Activities.Http.IntegrationTests.csproj --filter "FullyQualifiedName~MultiTriggerFlowchart"
```

The test publishes a Flowchart with two start-capable HTTP endpoints and requests the second path. It asserts that
the run completed through the second branch and that `ListBookmarksAsync` is empty.

## 3. Design: value flow per entry point

```bash
dotnet test Essentials/tests/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --filter "FullyQualifiedName~ValueFlowValidatorTests"
```

Expect User Story 2's five scenarios plus the single-entry regression cases to pass, and every existing case to keep
its result.

## 4. Architecture guards

```bash
dotnet test Essentials/tests/Architecture/Tests/Elsa.Architecture.Tests.csproj --filter "FullyQualifiedName~ArchitectureGuardTests"
```

The guards confirm that no Runtime project gained a Design reference.
