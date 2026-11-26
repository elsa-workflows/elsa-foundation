using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

public sealed class WorkflowArtifactHashCompatibilityTests : IDisposable
{
    private readonly string _mount = Path.Combine(Path.GetTempPath(), "elsa-hash-compatibility", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, recursive: true);
    }

    [Fact]
    public async Task A_retained_v1_artifact_roundtrips_through_the_export_codec_and_imports_with_its_original_identity()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var legacy = LegacyWorkflowHashFixture.Load();
        ArtifactClosureFixture.Mount(harness.Services, _mount, "legacy.json", ArtifactClosureFixture.Closure(legacy));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);
        var stored = await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(legacy.Identity.ArtifactId);
        Assert.NotNull(stored);
        Assert.Equal(legacy.Identity.ArtifactHash, stored.Identity.ArtifactHash);
        Assert.Equal("Elsa.Any", stored.RootActivity.InputBindings["value"].TargetType.Alias);
        Assert.Equal(legacy.CreatedAt, stored.CreatedAt);
    }

    [Fact]
    public async Task A_current_artifact_with_an_altered_destination_is_rejected_before_persistence()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var legacy = LegacyWorkflowHashFixture.Load();
        var current = LegacyWorkflowHashFixture.WithRoot(legacy, legacy.RootActivity, new WorkflowExecutableHasher());
        var type = new ValueTypeDescriptor("Int32");
        var tampered = LegacyWorkflowHashFixture.WithBinding(current, new RuntimeInputBinding("value", type,
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Expression,
            expression: current.RootActivity.InputBindings["value"].Expression,
            metadata: current.RootActivity.InputBindings["value"].Metadata));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "tampered.json", ArtifactClosureFixture.Closure(tampered));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.ContentHashMismatch, entry.RejectionKind);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, current.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, current.Identity.DefinitionId));
    }

    [Fact]
    public async Task A_retained_v1_hash_with_an_altered_expression_remains_rejected()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var legacy = LegacyWorkflowHashFixture.Load();
        var binding = legacy.RootActivity.InputBindings["value"];
        var tampered = LegacyWorkflowHashFixture.WithBinding(legacy, new RuntimeInputBinding("value", binding.TargetType,
            binding.EffectivePolicy, RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "8", binding.Expression!.ResultType),
            metadata: binding.Metadata));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "tampered-legacy.json", ArtifactClosureFixture.Closure(tampered));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactRejectionKind.ContentHashMismatch, entry.RejectionKind);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, legacy.Identity.ArtifactId));
    }
}
