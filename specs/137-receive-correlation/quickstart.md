# Quickstart: Validate Receive Event Correlation

## Prerequisites

- .NET 10 SDK installed.
- Repository dependencies restored by the standard local setup.
- Worktree on `codex/1001-receive-correlation` with the implementation applied.

## Focused Validation

1. Run the Event activity tests:

   ```bash
   dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --filter "FullyQualifiedName~EventTriggerStimulusProviderTests"
   ```

   Expected: a nonblank authored Event correlation is retained on the wait registration; null,
   empty, and whitespace-only values leave it unscoped.

2. Run the Event correlation routing acceptance test:

   ```bash
   dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --filter "FullyQualifiedName~EventCorrelationRoutingTests"
   ```

   Expected: two real same-named Event waits persist distinct correlation metadata; routing one
   correlation resumes only its matching workflow and leaves the other waiting.

3. Run the existing lookup and router coverage:

   ```bash
   dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --filter "FullyQualifiedName~GlobalBookmarkStimulusLookupTests|FullyQualifiedName~StimulusRouterTests"
   ```

   Expected: a correlated delivery selects only matching bookmark metadata; an unscoped delivery
   remains a broadcast; start-and-resume behavior remains unchanged.

4. Run both affected test projects before review:

   ```bash
   dotnet test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
   dotnet test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
   ```

## Acceptance Matrix

| Scenario | Expected outcome |
|---|---|
| Same name, matching nonblank correlation | The waiting Event resumes. |
| Same name, different nonblank correlation | The waiting Event stays parked. |
| Same name, no delivery correlation | All eligible waits remain broadcast candidates. |
| Null, empty, or whitespace-only authored correlation | The Event wait behaves as unscoped. |
| Correlated delivery and a start binding with a different authored correlation scope | Existing start fan-out is unchanged; only the resume set is narrowed. |

See [data-model.md](data-model.md) for retention and compatibility rules and [spec.md](spec.md)
for the complete acceptance scope.
