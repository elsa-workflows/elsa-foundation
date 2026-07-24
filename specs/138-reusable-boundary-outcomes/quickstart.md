# Quickstart: Reusable Activity Boundary Outcomes

1. Create or edit an activity definition using provider `elsa.activity-graph`, schema `2`.
2. Add the desired public outcomes to the activity contract and mark them emitted.
3. Add the direct entry activity and select one emitted entry outcome for each public boundary outcome.
4. Publish with the semantic version required by the contract diff.
5. Place the published reusable activity in a parent flowchart and connect each visible outcome port.
6. Execute and verify only the connection matching the actual reusable-graph result runs.

Schema-1 definitions need no changes and continue to expose only `done`.

## Focused verification

```bash
dotnet test tests/Elsa/Activities/Graph/Tests/Elsa.Activities.Graph.Tests.csproj
dotnet test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj --filter "FullyQualifiedName~ActivityDefinitionPublicationTests"
```

In the Studio worktree:

```bash
pnpm --filter @elsa-workflows/studio-workflows test -- --run activityGraphOutcomeMappings activityGraphImplementationEditor
pnpm --filter @elsa-workflows/studio-workflows typecheck
pnpm --filter @elsa-workflows/studio-workflows build
```
