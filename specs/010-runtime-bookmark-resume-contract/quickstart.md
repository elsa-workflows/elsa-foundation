# Quickstart: Runtime Bookmark Resume Contract

Run the focused validation:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

Manual inspection points:

- `BookmarkState` stores `ResumeTargetId`, not callback method names.
- `BookmarkResumeResolver` resolves through the pinned `WorkflowExecutable`.
- `ResumeTargetAttribute` lives in Activities.Runtime.Core, not Workflow Design.
