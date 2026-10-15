# Quickstart: verify graph boundary completion

## Unit and engine tests

Run only the projects this unit touches (shared machine; see AGENTS.md "Builds on a shared machine"):

```bash
dotnet test Essentials/tests/Activities/Graph/Tests/Elsa.Activities.Graph.Tests.csproj
dotnet test Essentials/tests/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
dotnet test Essentials/tests/Activities/Behavioral/Tests/Elsa.Activities.Behavioral.Tests.csproj
dotnet test Essentials/tests/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj
```

Expected:

- **Graph tests**: provider validation, compile and descriptor cases pass (User Stories 3 and 4).
- **Runtime tests**: ancestor-notification routing pass, including nearest-first, no-target faults and ordering
  before the notifier's completion.
- **Behavioral tests**: exits through Flowchart, Sequence, If, Switch, ForEach, While, Parallel, a nested graph and an
  entry-level `Complete` all pass. The durable-cancellation test passes on both in-memory and EF Sqlite (SC-001,
  SC-002).
- **Design tests**: the catalog contract baseline includes `Complete`.

## End to end, against a server running from source

```bash
pwsh e2e-tests/reusable-activities/Test-ReusableActivityComplete.ps1
pwsh e2e-tests/reusable-activities/Test-ReusableActivityOutcomeLimit.ps1
pwsh e2e-tests/reusable-activities/Test-ReusableActivity.ps1
```

The first script publishes a reusable activity with outcomes `succeed` and `fail` whose Flowchart body ends in
`Complete(fail)` on one branch. It then runs it from a parent Flowchart and asserts that only the `fail` branch runs.
It also asserts that publishing a body with `Complete(unknown)` and a plain workflow containing `Complete` are both
rejected. The other two scripts are the unchanged regression checks (SC-003).

## Manual check (REST)

1. Resolve the catalog version id of `Elsa.Activities.Graph.Runtime.Activities.Complete`.
2. Author a schema-2 activity graph with a `Complete` node, using the shape in
   [contracts/complete-activity.md](contracts/complete-activity.md).
3. Publish it, place it in a workflow and run the workflow. The reusable activity's outcome equals the `Complete`
   node's `outcome`, and the run's bookmark and timer lists are empty for the body.
