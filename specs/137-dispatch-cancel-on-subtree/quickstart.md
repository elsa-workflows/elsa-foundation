# Quickstart: Verify Dispatch Cancellation on Local Activity Teardown

Use the repository SDK selection unless the host requires the explicit SDK path.

```bash
dotnet test tests/Elsa/Activities/DispatchWorkflow/Tests/Elsa.Activities.DispatchWorkflow.Tests.csproj --no-restore --nologo -m:1
dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --no-restore --nologo -m:1
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-restore --nologo -m:1
dotnet test tests/Elsa/Activities/Bpmn/Tests/Elsa.Activities.Bpmn.Tests.csproj --no-restore --nologo -m:1
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj --no-restore --nologo -m:1
```

## Required scenarios

1. A Running parent checkpoint locally cancels the exact owner of an active waited dispatch and stages one canonical request and one child-cancel intent.
2. Re-enriching the same checkpoint produces an equivalent result.
3. Cancelling a sibling activity produces no cancellation work for the dispatch.
4. Whole-parent plus exact local cancellation produces one deduplicated responsibility.
5. Fire-and-forget, propagation-disabled, and terminal dispatches produce no new responsibility.
6. Committed-outbox recovery remains effective when terminal state wins after enrichment.
7. A matching owner on a later provider page is still found; a non-advancing cursor still fails.
8. Existing seam-A subtree cancellation tests continue to prove cancelled activity state and bookmark cleanup.

## Completion evidence

- Focused tests exercise every new branch and preserve every existing branch.
- Full paging and deterministic ordering are unchanged.
- No public or persistence shape changes appear in the diff.
- All listed projects pass with zero failures.
