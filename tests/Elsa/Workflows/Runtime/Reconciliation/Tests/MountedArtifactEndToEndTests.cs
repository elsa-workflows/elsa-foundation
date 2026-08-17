using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Models;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// US1 acceptance scenarios 1 and 2, end to end on a real engine: a valid artifact mounted as a JSON closure
/// reaches the executable store and runs to completion, and a trigger-started artifact routes its stimulus and
/// executes.
/// </summary>
/// <remarks>
/// <para>
/// The trigger scenario is the one that proves <b>both</b> projections activated rather than only the executable
/// having been persisted: the stimulus can only find the workflow through a trigger binding the coordinator
/// prepared and flipped active, and the start can only resolve a payload through the source reference it minted
/// under the same activation id.
/// </para>
/// <para>
/// Nothing here is mocked below the seam under test. The envelope is serialized by the engine's own payload
/// serializer, written to disk, read back by the production JSON source, and every gate — including the
/// content-hash recompute — runs against the round-tripped bytes.
/// </para>
/// </remarks>
public sealed class MountedArtifactEndToEndTests : IDisposable
{
    private const string SourceId = "mounted-artifacts";

    private readonly string _mount = Path.Combine(
        Path.GetTempPath(),
        "elsa-artifact-e2e",
        Guid.NewGuid().ToString("N"));

    public MountedArtifactEndToEndTests() => Directory.CreateDirectory(_mount);

    public void Dispose()
    {
        if (Directory.Exists(_mount))
            Directory.Delete(_mount, true);
    }

    [Fact]
    public async Task Scenario1_mounted_artifact_reaches_the_executable_store_and_runs_to_completion()
    {
        await using var harness = BuildHarness();
        var executable = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-root"), "definition-invoice");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "invoice.json", ArtifactClosureFixture.Closure(executable));

        var result = await ReconcileAsync(harness);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);
        Assert.Equal(executable.Identity.ArtifactId, entry.ArtifactId);

        // In the executable store, byte-identical to what the envelope carried.
        var stored = await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(executable.Identity.ArtifactId);
        Assert.NotNull(stored);
        Assert.Equal(executable.Identity.ArtifactHash, stored!.Identity.ArtifactHash);

        // Live in the definition's default activation slot, owned by the importing source.
        var slot = await harness.Services.GetRequiredService<IWorkflowActivationAuthority>()
            .FindAsync("definition-invoice", WorkflowArtifactReconciler.DefaultSlotName);
        Assert.NotNull(slot);
        Assert.Equal(entry.ActivationId, slot!.ActiveActivationId);
        Assert.Equal(WorkflowActivationSource.ArtifactReconciliationKind, slot.Source!.Kind);
        Assert.Equal(SourceId, slot.Source.SourceId);

        // And it runs.
        var reference = await FindMintedReferenceAsync(harness, entry.ActivationId!);
        await harness.StartPublishedAsync(reference, WorkflowExecutionHarness.WorkflowExecutionId);
        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);

        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Scenario2_trigger_started_artifact_routes_its_stimulus_and_executes()
    {
        await using var harness = BuildHarness();
        var trigger = ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode("node-trigger"));
        var executable = ArtifactClosureFixture.Executable(trigger, "definition-onboarding");
        ArtifactClosureFixture.Mount(harness.Services, _mount, "onboarding.json", ArtifactClosureFixture.Closure(executable));

        var entry = Assert.Single((await ReconcileAsync(harness)).Entries);
        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);

        // Projection 1: the trigger binding the importer recomputed is live under the same activation.
        var stimulusHash = ArtifactClosureFixture.TriggerStimulusHash("node-trigger");
        var bindings = await harness.Services.GetRequiredService<IWorkflowTriggerBindingStore>()
            .ListByStimulusAsync(new WorkflowTriggerBindingPageQuery(ArtifactClosureFixture.TriggerStimulusType, stimulusHash));
        var binding = Assert.Single(bindings.Items);
        Assert.True(binding.IsActive);
        Assert.Equal(entry.ActivationId, binding.ActivationId);
        Assert.Equal(executable.Identity.ArtifactId, binding.ArtifactId);

        // Projection 2: the source reference the binding's (ActivationId, SlotId) resolves to at dispatch time.
        var reference = await FindMintedReferenceAsync(harness, entry.ActivationId!);
        Assert.Equal(binding.SlotId, reference.SlotId);

        // The stimulus arrives and the runtime routes it.
        await using var scope = harness.Services.CreateAsyncScope();
        var routing = await scope.ServiceProvider.GetRequiredService<IStimulusRouter>().RouteAsync(
            new StimulusDispatchRequest(
                ArtifactClosureFixture.TriggerStimulusType,
                stimulusHash,
                mode: StimulusRoutingMode.StartOnly,
                idempotencyKey: "stimulus-1"));

        Assert.Equal(1, routing.StartedCount);
        var started = Assert.Single(routing.Starts);
        Assert.Equal(executable.Identity.ArtifactId, started.ArtifactId);
        Assert.Equal(binding.TriggerBindingId, started.TriggerBindingId);

        var run = await harness.ReadRunAsync(started.WorkflowExecutionId!);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task A_parent_child_closure_activates_the_child_before_the_parent()
    {
        // Dependencies-first, not the graph resolver's id order: a parent that went live while its child's source
        // reference was still absent would dispatch into nothing.
        await using var harness = BuildHarness();
        var child = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-child"), "definition-child");
        var parent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-parent"),
            "definition-parent",
            "1.0.0",
            ArtifactClosureFixture.DependencyOn(child, "node-parent"));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "closure.json", ArtifactClosureFixture.Closure(parent, child));

        var result = await ReconcileAsync(harness);

        Assert.Equal(2, result.ImportedCount);
        Assert.Equal([child.Identity.ArtifactId, parent.Identity.ArtifactId], result.Entries.Select(x => x.ArtifactId).ToArray());

        var store = harness.Services.GetRequiredService<IWorkflowExecutableStore>();
        Assert.NotNull(await store.FindAsync(child.Identity.ArtifactId));
        Assert.NotNull(await store.FindAsync(parent.Identity.ArtifactId));
    }

    [Fact]
    public async Task A_second_pass_over_an_unchanged_mount_changes_nothing()
    {
        // FR-B-007's steady state. The activation id is derived from source + definition + content-addressed
        // artifact id, so an unchanged mount re-derives the same activation and the coordinator no-ops.
        await using var harness = BuildHarness();
        var child = ArtifactClosureFixture.Executable(ArtifactClosureFixture.ProbeNode("node-child"), "definition-child");
        var parent = ArtifactClosureFixture.Executable(
            ArtifactClosureFixture.ProbeNode("node-parent"),
            "definition-parent",
            "1.0.0",
            ArtifactClosureFixture.DependencyOn(child, "node-parent"));
        ArtifactClosureFixture.Mount(harness.Services, _mount, "closure.json", ArtifactClosureFixture.Closure(parent, child));

        var first = await ReconcileAsync(harness);
        Assert.Equal(2, first.ImportedCount);

        var authority = harness.Services.GetRequiredService<IWorkflowActivationAuthority>();
        var revisionAfterFirst = (await authority.FindAsync("definition-parent", WorkflowArtifactReconciler.DefaultSlotName))!.Revision;

        var second = await ReconcileAsync(harness);

        Assert.Equal(0, second.ImportedCount);
        Assert.Equal(2, second.AlreadyCurrentCount);
        Assert.Equal(0, second.RejectedCount);
        Assert.Equal(
            revisionAfterFirst,
            (await authority.FindAsync("definition-parent", WorkflowArtifactReconciler.DefaultSlotName))!.Revision);
    }

    [Fact]
    public async Task An_envelope_whose_carried_trigger_surface_matches_is_imported()
    {
        // The carried bindings are an expectation the importer checks against what it recomputes from the same
        // payload — never rows it persists.
        await using var harness = BuildHarness();
        var trigger = ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode("node-trigger"));
        var executable = ArtifactClosureFixture.Executable(trigger, "definition-onboarding");
        ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "onboarding.json",
            ArtifactClosureFixture.ClosureWithCarriedBindings(
                executable,
                "node-trigger",
                ArtifactClosureFixture.TriggerStimulusHash("node-trigger")));

        var entry = Assert.Single((await ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Imported, entry.Outcome);

        // The exporting engine's activation id is meaningless here — the importer minted its own.
        var binding = Assert.Single((await harness.Services.GetRequiredService<IWorkflowTriggerBindingStore>()
            .ListByStimulusAsync(new WorkflowTriggerBindingPageQuery(
                ArtifactClosureFixture.TriggerStimulusType,
                ArtifactClosureFixture.TriggerStimulusHash("node-trigger")))).Items);
        Assert.NotEqual("exporter-activation", binding.ActivationId);
        Assert.Equal(entry.ActivationId, binding.ActivationId);
    }

    [Fact]
    public async Task An_envelope_whose_carried_trigger_surface_disagrees_is_rejected()
    {
        await using var harness = BuildHarness();
        var trigger = ArtifactClosureFixture.AsStartTrigger(ArtifactClosureFixture.ProbeNode("node-trigger"));
        var executable = ArtifactClosureFixture.Executable(trigger, "definition-onboarding");
        ArtifactClosureFixture.Mount(
            harness.Services,
            _mount,
            "onboarding.json",
            ArtifactClosureFixture.ClosureWithCarriedBindings(executable, "node-trigger", "sha256:a-surface-this-runtime-does-not-produce"));

        var entry = Assert.Single((await ReconcileAsync(harness)).Entries);

        Assert.Equal(WorkflowArtifactImportOutcome.Rejected, entry.Outcome);
        Assert.Equal(WorkflowArtifactRejectionKind.TriggerSurfaceMismatch, entry.RejectionKind);
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(executable.Identity.ArtifactId));
    }

    private static async Task<WorkflowArtifactReconciliationResult> ReconcileAsync(WorkflowExecutionHarness harness)
    {
        // The activity-type registry must be populated before the import gate asks whether each node's CLR type is
        // present — the same ordering [TaskDependency(typeof(RegisterActivityTypesStartupTask))] enforces at boot.
        harness.InitializeActivityTypes();

        await using var scope = harness.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>().ReconcileAsync();
    }

    private static async Task<WorkflowExecutableSourceReference> FindMintedReferenceAsync(
        WorkflowExecutionHarness harness,
        string activationId)
    {
        var reference = await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .FindAsync(WorkflowActivationReferenceIdentity.Create(activationId));
        Assert.NotNull(reference);
        return reference!;
    }

    private WorkflowExecutionHarness BuildHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeTriggersFeature().ConfigureServices(services))
            .WithFeature(services => new JsonWorkflowArtifactReconciliationFeature
            {
                Options =
                {
                    SourceId = SourceId,
                    FolderPath = _mount,
                },
            }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                // The host always composes logging; the bare harness does not, and the reconciliation services take
                // a non-optional ILogger<T>.
                services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
                services.AddSingleton<IActivityTriggerStimulusProvider, ProbeTriggerStimulusProvider>();
            })
            .Build(Enumerable.Range(1, 32).Select(index => $"activity-execution-{index}").ToArray());
}
