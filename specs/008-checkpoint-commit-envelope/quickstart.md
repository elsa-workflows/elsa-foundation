# Quickstart: Checkpoint Commit Envelope

Validate the slice with:

```bash
dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
dotnet test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

Manual inspection points:

- `RuntimeCheckpointCommit` carries checkpoint semantics, state changes, intents, and metadata.
- `IRuntimeCheckpointWriter` accepts the full commit envelope, not only the checkpoint header.
- `RuntimeCheckpointCommitter` dispatches intents only after successful writer completion.
- Runtime projects still have no `Elsa.Workflows.Design.*` references.
