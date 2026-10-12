using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// US2 scenarios 1 and 2, and the third gate axis that neither of FR-B-005a's two axes covers: an artifact this
/// runtime cannot execute is refused <b>at import</b>, with a diagnostic that names what is missing.
/// </summary>
/// <remarks>
/// <para>
/// Every case asserts three things, because a gate that reports a problem and imports anyway is worse than no
/// gate: the entry is <see cref="WorkflowArtifactImportOutcome.Rejected"/> with the
/// <see cref="WorkflowArtifactRejectionKind.UnmetRequirement"/> kind, the diagnostic <em>names</em> the missing
/// capability by its own identifier (an operator has to know which package to install), and nothing reached the
/// executable store or the activation slot.
/// </para>
/// <para>
/// The artifacts are built and mounted exactly as a real export would be, through
/// <see cref="ArtifactClosureFixture"/>, so each one passes parse, closure validation and the content-hash
/// recompute before reaching the gate under test. Otherwise these would prove that some earlier gate fires.
/// </para>
/// </remarks>
public sealed class ImportGateRequirementTests : IDisposable
{
    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-gate",
        Guid.NewGuid().ToString("N"));

    public ImportGateRequirementTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task Scenario1_an_artifact_whose_activity_type_is_not_registered_is_rejected_at_import()
    {
        // The descriptor is well-formed and the consumer key resolves — only the CLR activity behind it is absent,
        // which is exactly the shape of "the operator forgot to deploy the activity package".
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.UnregisteredClrNode("node-root", "Acme.Warehouse.PickAndPack"),
            "definition-fulfilment");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "fulfilment.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, entry.RejectionKind);
        Assert.Contains("Acme.Warehouse.PickAndPack", entry.Diagnostic);
        Assert.Contains("node-root", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-fulfilment"));
    }

    [Fact]
    public async Task Scenario2_an_artifact_with_an_unmet_storage_driver_requirement_is_rejected_at_import()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.ExecutableRequiring(
            ArtifactClosureFixture.ProbeNode("node-root"),
            "definition-archive",
            storageDriverRequirements: [new RuntimeStorageDriverRequirement("acme.cold-storage")]);
        ArtifactClosureFixture.Mount(harness.Services, _mount, "archive.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, entry.RejectionKind);
        Assert.Contains("acme.cold-storage", entry.Diagnostic);
        Assert.Contains("storage driver", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-archive"));
    }

    [Fact]
    public async Task An_artifact_requiring_an_uninstalled_activity_consumer_is_rejected_at_import()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.ExecutableRequiring(
            ArtifactClosureFixture.ProbeNode("node-root"),
            "definition-scripted",
            runtimeRequirements: [new RuntimeRequirement("acme.wasm-activity", "1")]);
        ArtifactClosureFixture.Mount(harness.Services, _mount, "scripted.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, entry.RejectionKind);
        Assert.Contains("acme.wasm-activity", entry.Diagnostic);
        Assert.Contains("is not installed", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
    }

    [Fact]
    public async Task An_artifact_requiring_an_unsupported_consumer_schema_is_rejected_and_told_what_is_supported()
    {
        // The consumer is installed; the artifact was produced against a descriptor schema this build predates.
        // The diagnostic must distinguish that from "not installed" — the fixes are entirely different.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.ExecutableRequiring(
            ArtifactClosureFixture.ProbeNode("node-root"),
            "definition-future",
            runtimeRequirements: [new RuntimeRequirement(WellKnownRuntimeActivityConsumers.ClrActivity, "99")]);
        ArtifactClosureFixture.Mount(harness.Services, _mount, "future.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, entry.RejectionKind);
        Assert.Contains(WellKnownRuntimeActivityConsumers.ClrActivity, entry.Diagnostic);
        Assert.Contains("does not support descriptor schema '99'", entry.Diagnostic);
        Assert.Contains("supported: [1]", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
    }

    [Fact]
    public async Task An_artifact_whose_descriptor_carries_a_clr_type_name_instead_of_a_consumer_key_is_rejected_at_import()
    {
        // The third axis (the gap found while writing T071). WorkflowExecutionHarness.NewProbeNode emits the
        // descriptor's *CLR type name* and relies on the harness rewriting it to the consumer key when it pins
        // contracts on save. The importer deliberately never rewrites a content-addressed artifact — the bytes are
        // the identity — so an envelope carrying the unpinned form parses, validates, hashes to its own id and
        // (before this gate) activated cleanly, only to fault at *first execution* with
        // UnknownActivityConsumerException. That is precisely the production surprise US2 exists to prevent, on an
        // axis FR-B-005a's two do not check. Building the node unpinned here is deliberate, not an oversight.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var unpinned = WorkflowExecutionHarness.NewProbeNode("node-root");
        Assert.Equal(typeof(ClrActivityDescriptor).FullName, unpinned.DescriptorType);

        var executable = ArtifactClosureFixture.Executable(unpinned, "definition-unpinned");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "unpinned.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, entry.RejectionKind);
        Assert.Contains("node 'node-root' declares descriptor consumer", entry.Diagnostic);
        Assert.Contains(typeof(ClrActivityDescriptor).FullName!, entry.Diagnostic);
        Assert.Contains("no activity activation strategy installed on this runtime handles", entry.Diagnostic);

        // Rejected at import, not faulted at first execution: nothing was stored and nothing was activated, so
        // there is no live definition for a stimulus to reach.
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-unpinned"));
    }

    [Fact]
    public async Task One_rejection_names_every_unmet_requirement_rather_than_only_the_first()
    {
        // An operator who fixes the missing consumer and redeploys only to discover the missing storage driver has
        // paid for two deploy cycles to learn one artifact's requirements.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.ExecutableRequiring(
            ArtifactClosureFixture.UnregisteredClrNode("node-root", "Acme.Warehouse.PickAndPack"),
            "definition-multi",
            runtimeRequirements: [new RuntimeRequirement("acme.wasm-activity", "1")],
            storageDriverRequirements: [new RuntimeStorageDriverRequirement("acme.cold-storage")]);
        ArtifactClosureFixture.Mount(harness.Services, _mount, "multi.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, entry.RejectionKind);
        Assert.Contains("acme.wasm-activity", entry.Diagnostic);
        Assert.Contains("acme.cold-storage", entry.Diagnostic);
        Assert.Contains("Acme.Warehouse.PickAndPack", entry.Diagnostic);
        Assert.Contains(executable.Identity.ArtifactId, entry.Diagnostic);
    }

    [Fact]
    public async Task A_satisfiable_artifact_still_passes_the_gate()
    {
        // The gate's counter-assertion. Without it, every test above would also pass if the gate rejected
        // unconditionally.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-ok");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "ok.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        var slot = await ArtifactImportHarness.FindSlotAsync(harness, "definition-ok");

        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);
        Assert.True(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.NotNull(slot);
        Assert.Equal(entry.ActivationId, slot!.ActiveActivationId);
    }

    [Fact]
    public async Task A_valid_artifact_mints_a_published_reference_in_the_source_tenant()
    {
        await using var harness = ArtifactImportHarness.Build(_mount, tenantId: "tenant-a");
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-tenant");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "tenant.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        var reference = await ArtifactImportHarness.FindReferenceAsync(harness, entry.ActivationId!);

        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);
        Assert.Equal(WorkflowExecutableReferenceScope.Published, reference!.Scope);
        Assert.Equal("tenant-a", reference.TenantId);
        Assert.Equal(executable.Identity.ArtifactId, reference.ArtifactId);
    }
}
