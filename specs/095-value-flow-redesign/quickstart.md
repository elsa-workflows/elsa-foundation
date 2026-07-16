# Quickstart: Role-Owned Workflow Value Flow

This work unit is a deliberate major-version contract replacement. Use the commands and examples in
this document to exercise one coherent slice at a time; do not introduce compatibility adapters for
the removed memory-block API to make an intermediate build green.

## Prerequisites

- .NET SDK 10.0.300 or newer in the .NET 10 feature band.
- Repository root as the working directory.
- A clean or intentionally reviewed worktree.
- The design documents in this directory and ADR 0045 read before changing public contracts.

On this workstation the SDK is invoked explicitly:

```bash
DOTNET=/usr/local/share/dotnet/dotnet
$DOTNET --info
```

## Baseline

Record the baseline before deleting any legacy test or public type:

```bash
$DOTNET build Elsa.Server.slnx --no-restore
$DOTNET test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj --no-build
$DOTNET test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj --no-build
$DOTNET test tests/Elsa/Expressions/Tests/Elsa.Expressions.Tests.csproj --no-build
$DOTNET test tests/Elsa3/Mapping/Tests/Elsa3.Mapping.Tests.csproj --no-build
```

If packages have not been restored, omit `--no-restore` on the build. Record pre-existing failures in
the implementation notes; do not weaken the target verification to conceal them.

## Activity authoring target

An activity is a transient service-bearing behavior object. Its inputs are ordinary hydrated
properties and successful completion returns one result value.

```csharp
public sealed record DownloadResult(int StatusCode, string Content);

public sealed class DownloadDocument(HttpClient client) : Activity<DownloadResult>
{
    [ActivityInput(Key = "url")]
    public required Uri Url { get; set; }

    protected override async ValueTask<ActivityTransition<DownloadResult>> ExecuteAsync(
        ActivityExecutionContext context)
    {
        using var response = await client.GetAsync(Url, context.CancellationToken);
        var content = await response.Content.ReadAsStringAsync(context.CancellationToken);
        return ActivityTransition.Complete(
            new DownloadResult((int)response.StatusCode, content),
            outcome: "Done");
    }
}
```

The runtime must pin `Url` before activating `DownloadDocument`. It commits `DownloadResult` and the
outcome atomically; `StatusCode` and `Content` are projections, not separately writable output slots.

## Code-first authoring target

Code-first authoring produces the same authored state as dynamic authoring. Generated methods make
activity calls discoverable, while `.From(...)` and `.Value(...)` make the dynamic/literal boundary
explicit.

```csharp
public sealed record DownloadRequest(Uri Url);
public sealed record DownloadWorkflowResult(string Content);

public sealed class DownloadWorkflow : WorkflowDefinition<DownloadRequest, DownloadWorkflowResult>
{
    protected override void Build(IWorkflowBuilder<DownloadRequest, DownloadWorkflowResult> workflow)
    {
        var download = workflow.DownloadDocument(url: workflow.From(x => x.Url));
        workflow.Return(workflow.From(download.Outputs.Content));
    }
}
```

The exact generated surface is governed by `contracts/authoring-contract.md`. Nothing generated is
serialized into `WorkflowDefinitionState` or referenced by Runtime.

## Durable invocation scenario

For the first vertical slice, prove this sequence:

1. Publish a workflow with a literal, variable, result, and explicit expression input binding.
2. Schedule an activity execution.
3. Materialize and checkpoint the complete `ActivityInputSnapshot` and first `ActivityAttempt`.
4. Mutate each original source after the checkpoint.
5. Activate a fresh CLR activity and verify its properties contain the pinned values.
6. Suspend with typed private state, dispose the activation, and resume on a fresh activation.
7. Complete with one typed result and outcome in the checkpoint.
8. Interrupt continuation scheduling and recover without invoking the completed activity again.

## Focused verification

Run focused projects after every migration slice:

```bash
$DOTNET test tests/Elsa/Activities/Design/Tests/Elsa.Activities.Design.Tests.csproj
$DOTNET test tests/Elsa/Activities/Runtime/Tests/Elsa.Activities.Runtime.Tests.csproj
$DOTNET test tests/Elsa/Workflows/Publishing/Api/Tests/Elsa.Workflows.Publishing.Api.Tests.csproj
$DOTNET test tests/Elsa/Workflows/Runtime/Tests/Elsa.Workflows.Runtime.Tests.csproj
$DOTNET test tests/Elsa/Persistence/Groundwork/Tests/Elsa.Persistence.Groundwork.Tests.csproj
$DOTNET test tests/Elsa/Expressions/JavaScript/Jint/Tests/Elsa.Expressions.JavaScript.Jint.Tests.csproj
$DOTNET test tests/Elsa3/Mapping/Tests/Elsa3.Mapping.Tests.csproj
$DOTNET test tests/Elsa/Architecture/Elsa.Architecture.Tests.csproj
```

Then run the complete solution:

```bash
$DOTNET test Elsa.Server.slnx
```

Before completion, search the canonical source and public API surface for forbidden remnants:

```bash
rg -n "IMemoryBlock|IMemoryRegister|InputArgument|OutputArgument|DelegateExpression" src/Elsa tests/Elsa
```

Only importer-local Elsa 3 DTO terminology documented by the import contract may remain. Search tests
separately when validating fixture convergence; production and test allowlists are intentionally distinct.

## Activation-scope evidence

After the real activation seam and intrinsic path exist, run the benchmark harness for burst,
per-attempt, and eligible conditional strategies. Retain raw results and environment metadata, apply
all semantic gates in `contracts/activation-scope-benchmark.md`, and record the selected lifetime in
ADR 0045 or a focused successor. Do not publish a lifetime guarantee before this evidence exists.

## Completion checklist

- Every row in `test-migration-ledger.md` is implemented and passing or has an explicit architectural
  removal rationale.
- Groundwork document version, upcaster, round-trip fixture, and mixed-version recovery tests pass.
- Code-first and equivalent dynamic authored states compare semantically equal.
- Runtime projects have no Design or code-generation references.
- Canonical packages have no memory-block contracts, argument wrappers, ambient expression value
  access, or synthetic workflow-value channels.
- All functional requirements and success criteria in `spec.md` are reconciled with evidence.
