using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>US4's deployment loop: latest wins, older versions never reactivate, and retries add no records.</summary>
public sealed class ArtifactImportSupersessionTests : IDisposable
{
    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-supersession",
        Guid.NewGuid().ToString("N"));

    public ArtifactImportSupersessionTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task A_newer_version_supersedes_once_and_repeated_reconciliation_is_idempotent()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var v1 = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-v1"),
            "definition-promoted",
            "1.0.0");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "01-v1.json", ArtifactClosureFixture.Closure(v1));

        var first = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, first.Outcome);

        var v2 = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-v2"),
            "definition-promoted",
            "2.0.0");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "02-v2.json", ArtifactClosureFixture.Closure(v2));

        var secondPass = await ArtifactImportHarness.ReconcileAsync(harness);
        var promoted = Assert.Single(secondPass.Entries, entry => entry.ArtifactId == v2.Identity.ArtifactId);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, promoted.Outcome);
        Assert.Equal(promoted.ActivationId, (await ArtifactImportHarness.FindSlotAsync(harness, "definition-promoted"))!.ActiveActivationId);
        Assert.NotNull((await ArtifactImportHarness.FindReferenceAsync(harness, first.ActivationId!))!.DeletedAt);
        Assert.Null((await ArtifactImportHarness.FindReferenceAsync(harness, promoted.ActivationId!))!.DeletedAt);

        var thirdPass = await ArtifactImportHarness.ReconcileAsync(harness);
        var current = Assert.Single(thirdPass.Entries, entry => entry.ArtifactId == v2.Identity.ArtifactId);
        Assert.Equal(WorkflowArtifactImportOutcome.AlreadyCurrent, current.Outcome);
        Assert.Equal(2, (await ArtifactImportHarness.ListAllReferencesAsync(harness)).Count);
        Assert.Equal(promoted.ActivationId, (await ArtifactImportHarness.FindSlotAsync(harness, "definition-promoted"))!.ActiveActivationId);
    }
}
