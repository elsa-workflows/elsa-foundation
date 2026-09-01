using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// US1's executable boundary: portable bytes are enough for a runtime-only engine to import, activate, and run a
/// workflow without composing the design catalog, publishing bridge, or compiler.
/// </summary>
public sealed class RuntimeOnlyArtifactExecutionTests : IDisposable
{
    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-runtime-only-artifacts",
        Guid.NewGuid().ToString("N"));

    public RuntimeOnlyArtifactExecutionTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task A_design_free_runtime_imports_and_executes_a_portable_artifact_to_completion()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-runtime-only"),
            "definition-runtime-only");
        ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "runtime-only.json",
            ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);

        var reference = await ArtifactImportHarness.FindReferenceAsync(harness, entry.ActivationId!);
        Assert.NotNull(reference);
        await harness.StartPublishedAsync(reference!, "runtime-only-execution");

        var run = await harness.ReadRunAsync("runtime-only-execution");
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-runtime-only").Status);

        var loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetName().Name)
            .Where(name => name is not null)
            .ToArray();
        Assert.DoesNotContain(loadedAssemblies, name => name!.StartsWith("Elsa.Workflows.Design", StringComparison.Ordinal));
        Assert.DoesNotContain(loadedAssemblies, name => name!.StartsWith("Elsa.Workflows.Publishing", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_stimulus_starts_the_imported_trigger_artifact_and_runs_it_to_completion()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode("node-trigger")),
            "definition-trigger");
        ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "trigger.json",
            ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);

        await using var scope = harness.Services.CreateAsyncScope();
        var routed = await scope.ServiceProvider.GetRequiredService<IStimulusRouter>().RouteAsync(
            new StimulusDispatchRequest(
                ArtifactClosureFixture.TriggerStimulusType,
                ArtifactClosureFixture.TriggerStimulusHash("node-trigger"),
                mode: StimulusRoutingMode.StartOnly,
                idempotencyKey: "runtime-only-trigger"));

        var start = Assert.Single(routed.Starts);
        Assert.Equal(StimulusStartStatus.Started, start.Status);
        var run = await harness.ReadRunAsync(start.WorkflowExecutionId!);
        Assert.Equal(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-trigger").Status);
    }
}
