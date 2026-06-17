# Quickstart: Flowchart Scoped Execution Validation

## Prerequisites

- Restore/build the Elsa foundation solution using the repository's existing .NET workflow.
- Run Flowchart tests from `tests/Elsa/Activities/Flowchart/Tests`.

## Validation Scenarios

### 1. Direct continuation

Run a Flowchart with `A -> B`.

Expected outcome:

- A starts from the Flowchart start node.
- B is scheduled after A completes with the matching outcome.
- Each child schedule carries `executionPathId` and `executionScopeId` metadata.

### 2. Implicit activation-aware join

Run a diamond Flowchart:

```text
Start
├── Left
└── Right
    ↓
JoinTarget
```

Expected outcome:

- `JoinTarget` waits until both active branches arrive.
- `JoinTarget` runs exactly once.
- Diagnostics explain the waiting and joined decisions.

### 3. Decision with dead-path reconvergence

Run a Flowchart where a decision selects only one branch and both possible branches reconverge.

Expected outcome:

- The selected branch runs.
- The unselected branch is treated as unable to arrive in the current scope.
- The reconverged target runs without deadlock.
- Diagnostics explain why the unselected branch is no longer expected.

### 4. Loop iteration isolation

Run a Flowchart with a backward edge to a stable loop entry and a join inside the loop.

Expected outcome:

- Each backward traversal creates a new loop iteration scope.
- Arrivals from one iteration do not satisfy joins in another.
- Ambiguous loopbacks into active join/race scopes are rejected.

### 5. First Wins race

Run a race Flowchart where two sibling branches compete.

Expected outcome:

- The first completing branch wins.
- Losing sibling execution paths within the race scope are canceled through normal runtime cancellation.
- Unrelated ancestor/cousin work continues.
- Diagnostics identify the winning and canceled branches.

### 6. Custom policy extension

Register a custom policy and configure a Flowchart node to use it.

Expected outcome:

- The policy receives a read-only context.
- The policy returns commands rather than mutating state.
- The Flowchart engine applies valid commands.
- Invalid/conflicting commands are rejected with policy failure diagnostics.

## Suggested Commands

Run existing Flowchart tests:

```bash
dotnet test tests/Elsa/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj
```

Run broader runtime-adjacent tests when the implementation changes shared runtime behavior:

```bash
dotnet test tests/Elsa/Activities/Flowchart/Tests/Elsa.Activities.Flowchart.Tests.csproj
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
```
