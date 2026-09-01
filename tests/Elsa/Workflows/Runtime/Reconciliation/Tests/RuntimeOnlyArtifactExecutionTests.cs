using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
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
}
