# Quickstart: Workflow Instance Inspection

## Prerequisites

- `elsa-foundation` backend dependencies restored.
- `elsa-foundation-studio` frontend dependencies installed.
- Elsa Server running on `https://localhost:7243`.
- Elsa Studio running on `https://localhost:7030` or `http://localhost:5089`.

## Backend checks

```bash
dotnet test tests/Elsa/Workflows/Design/Tests/Elsa.Workflows.Design.Tests.csproj --filter "FullyQualifiedName~WorkflowDefinitionVersionDetails"
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --filter "FullyQualifiedName~WorkflowInstance"
```

Expected:
- Workflow definition version details include state and layout.
- Runtime workflow instance details still return runtime evidence without design data.

## Studio checks

```bash
cd ../elsa-foundation-studio
pnpm --filter @elsa-workflows/studio-workflows test -- --run src/__tests__/module.test.tsx src/__tests__/workflowAdapter.test.ts
pnpm --filter @elsa-workflows/studio-workflows build
```

Expected:
- The instances list opens `/workflows/instances/{workflowExecutionId}`.
- Direct instance routes load instance details, version state/layout, activity catalog, and render the read-only canvas.
- Faulted activity/incident evidence is visible on the graph and in the incident panel.

## Manual validation

1. Start Elsa Server and Studio.
2. Create or open a workflow definition with a Flowchart root and at least one child activity.
3. Save/promote/publish or run the workflow so an instance exists.
4. Open `/workflows/instances`.
5. Select the instance.
6. Confirm the route changes to `/workflows/instances/{workflowExecutionId}`.
7. Confirm the workflow graph appears with the same node positions as the definition designer.
8. Select an activity in the timeline and confirm the matching node is highlighted.
9. For a faulted instance, confirm the incident appears and the affected node is marked.
