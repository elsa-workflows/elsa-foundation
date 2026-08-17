using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Elsa.Workflows.Runtime.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// An artifact containing an engine intrinsic (<c>Set</c>, <c>Merge</c>, <c>Return</c>, <c>Control</c>,
/// <c>SetCorrelationId</c>, …) must import and run.
/// </summary>
/// <remarks>
/// <para>
/// This is a regression pin on a defect that was latent rather than hypothetical.
/// <c>ExecutableNodeCompiler</c> stamps intrinsic nodes with the reserved <c>"intrinsic"</c> descriptor type, and
/// <c>WorkflowExecutable</c>'s constructor <em>derives</em> <c>RuntimeRequirements</c> from every node's consumer
/// key — so a compiled workflow containing any intrinsic declares <c>RuntimeRequirement("intrinsic", "1")</c>.
/// Only <c>ClrActivity</c> and <c>GraphActivity</c> advertised an <c>IRuntimeActivityConsumerCapability</c>, so
/// that requirement read as <c>Missing</c> and the import gate rejected the artifact. The gate's other two axes
/// skip intrinsics deliberately; the requirement axis structurally cannot, because it sees only the derived list.
/// </para>
/// <para>
/// It stayed invisible because no fixture in the repo was intrinsic-bearing. That absence is the reason these
/// tests exist as much as the fix is.
/// </para>
/// </remarks>
public sealed class IntrinsicBearingArtifactImportTests : IDisposable
{
    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-intrinsic",
        Guid.NewGuid().ToString("N"));

    public IntrinsicBearingArtifactImportTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public void The_engine_advertises_the_intrinsic_consumer_the_compiler_stamps()
    {
        // The two identifiers that must agree, asserted against each other rather than against a literal: the key
        // the compiler writes into artifacts, and the key the runtime advertises. The schema version is the one
        // ExecutableNode defaults to when the compiler omits it.
        var capability = new WorkflowIntrinsicActivityConsumerCapability();

        Assert.Equal(WellKnownRuntimeActivityConsumers.Intrinsic, capability.ConsumerKey);
        Assert.Equal("intrinsic", capability.ConsumerKey);
        Assert.Equal([RuntimeActivityDescriptor.InitialSchemaVersion], capability.SupportedSchemaVersions);

        var node = ArtifactClosureFixture.IntrinsicNode("node-root");
        Assert.Equal(capability.ConsumerKey, node.Descriptor.ConsumerKey);
        Assert.Contains(node.Descriptor.SchemaVersion, capability.SupportedSchemaVersions);
    }

    [Fact]
    public void An_intrinsic_bearing_executable_derives_an_intrinsic_requirement()
    {
        // The premise of the whole file. If the derivation ever stops emitting this, the tests below would still
        // pass while proving nothing.
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.IntrinsicNode("node-root"),
            "definition-premise");

        Assert.Contains(
            executable.RuntimeRequirements,
            requirement => requirement.ConsumerKey == WellKnownRuntimeActivityConsumers.Intrinsic
                           && requirement.SchemaVersion == RuntimeActivityDescriptor.InitialSchemaVersion);
    }

    [Fact]
    public async Task An_intrinsic_bearing_artifact_passes_the_import_gate_and_is_imported()
    {
        await using var harness = ArtifactImportHarness.Build(_mount);
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.IntrinsicNode("node-root"),
            "definition-intrinsic");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "intrinsic.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.None, entry.RejectionKind);
        Assert.True(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.NotNull(await ArtifactImportHarness.FindSlotAsync(harness, "definition-intrinsic"));
    }

    [Fact]
    public async Task A_mixed_clr_and_intrinsic_artifact_is_imported_and_runs_to_completion()
    {
        // The realistic compiled shape: a CLR leaf beside an engine intrinsic, so the artifact declares both
        // consumer keys and the gate has to satisfy them from two different providers — an activity package for
        // one, the runtime spine itself for the other.
        await using var harness = ArtifactImportHarness.Build(_mount);
        var root = ArtifactClosureFixture.ProbeNode("node-root");
        var executable = ArtifactClosureFixture.Executable(
            WithChild(root, ArtifactClosureFixture.IntrinsicNode("node-correlate")),
            "definition-mixed");
        Assert.Equal(
            [WellKnownRuntimeActivityConsumers.ClrActivity, WellKnownRuntimeActivityConsumers.Intrinsic],
            executable.RuntimeRequirements.Select(requirement => requirement.ConsumerKey).ToArray());
        ArtifactClosureFixture.Mount(harness.Services, _mount, "mixed.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);

        // And it executes: importable-but-unexecutable is exactly the state US2 exists to prevent, so the gate's
        // verdict is only trustworthy if the artifact it admitted actually runs.
        var reference = await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .FindAsync(WorkflowActivationReferenceIdentity.Create(entry.ActivationId!));
        Assert.NotNull(reference);
        await harness.StartPublishedAsync(reference!, WorkflowExecutionHarness.WorkflowExecutionId);

        (await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId)).AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Without_the_intrinsic_capability_the_same_artifact_is_rejected_naming_the_intrinsic_consumer()
    {
        // The counter-assertion that keeps the tests above from passing vacuously. Delete the capability and the
        // gate must reject again with the diagnostic that motivated the fix — so a future removal fails loudly
        // here rather than silently re-arming the defect.
        await using var harness = ArtifactImportHarness.Build(_mount, RemoveIntrinsicCapability);
        var executable = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.IntrinsicNode("node-root"),
            "definition-intrinsic");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "intrinsic.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ArtifactImportHarness.ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.UnmetRequirement, entry.RejectionKind);
        Assert.Contains("activity consumer 'intrinsic'", entry.Diagnostic);
        Assert.Contains("is not installed", entry.Diagnostic);
        Assert.False(await ArtifactImportHarness.IsInStoreAsync(harness, executable.Identity.ArtifactId));
        Assert.Null(await ArtifactImportHarness.FindSlotAsync(harness, "definition-intrinsic"));
    }

    private static void RemoveIntrinsicCapability(IServiceCollection services)
    {
        var descriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IRuntimeActivityConsumerCapability)
                                 && descriptor.ImplementationType == typeof(WorkflowIntrinsicActivityConsumerCapability))
            .ToArray();

        // Guards the guard: if the registration is ever moved or renamed, this removes nothing and the test would
        // report a rejection that never depended on it.
        Assert.Single(descriptors);
        foreach (var descriptor in descriptors)
            services.Remove(descriptor);
    }

    /// <summary>Places <paramref name="child"/> in a child slot of <paramref name="parent"/>.</summary>
    private static ExecutableNode WithChild(ExecutableNode parent, ExecutableNode child) =>
        new(
            executableNodeId: parent.ExecutableNodeId,
            authoredActivityId: parent.AuthoredActivityId,
            activityType: parent.ActivityType,
            activityTypeVersion: parent.ActivityTypeVersion,
            descriptorType: parent.DescriptorType,
            descriptorPayload: parent.DescriptorPayload,
            inputBindings: parent.InputBindings,
            metadata: parent.Metadata,
            childSlots: [new ExecutableChildSlot("Body", [child])],
            structure: parent.Structure,
            activityContract: parent.ActivityContract,
            intrinsicKind: parent.IntrinsicKind,
            intrinsicVariable: parent.IntrinsicVariable,
            outputCaptures: parent.OutputCaptures,
            descriptorSchemaVersion: parent.DescriptorSchemaVersion);
}
