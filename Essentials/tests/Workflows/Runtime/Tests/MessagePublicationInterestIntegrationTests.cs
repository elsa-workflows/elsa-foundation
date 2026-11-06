using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Messaging;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Real volatile activation owner/projection writes; no durable backend or accepted cohort is enabled.</summary>
public sealed class MessagePublicationInterestIntegrationTests
{
    [Fact]
    public async Task Receive_only_material_serves_before_any_bookmark_or_trigger_binding_exists()
    {
        var fixture = new Fixture(); var executable = fixture.Executable("artifact-a", "Payment");
        var result = await fixture.Activate(executable, "a");
        Assert.True(result.Succeeded);
        var observed = await fixture.Interests.ObservePublicationAsync(fixture.Scope);
        Assert.Equal(1, observed.Epoch); Assert.Equal(["Payment"], observed.Names);
        Assert.Empty(await fixture.Bindings.ListAllByActivationAsync("a"));
        Assert.Throws<NotSupportedException>(() => fixture.Interests.RequireCompleteAcceptanceCoverage());
    }

    [Fact]
    public async Task Prepared_names_are_invisible_and_the_real_switch_publishes_slot_names_and_epoch_together()
    {
        var fixture = new Fixture(); var executable = fixture.Executable("artifact-a", "Payment");
        await fixture.Prepare(executable, "a");
        var before = await fixture.Interests.ObservePublicationAsync(fixture.Scope);
        Assert.Equal(0, before.Epoch); Assert.Empty(before.Names);
        Assert.Equal(WorkflowActivationProjectionState.Prepared, fixture.Interests.State("a"));
        var switched = await fixture.Switch.TryActivateAsync(new("definition", "default", "a", WorkflowActivationSource.Publishing, 0, Fixture.Now));
        Assert.True(switched.Succeeded); Assert.Equal("a", switched.Slot.ActiveActivationId);
        Assert.Equal(["Payment"], (await fixture.Interests.ObservePublicationAsync(fixture.Scope)).Names);
        Assert.Equal(WorkflowActivationProjectionState.Active, await fixture.Bindings.FindActivationStateAsync("a"));
    }

    [Fact]
    public async Task Replacement_and_deactivation_change_publication_membership_without_enabling_complete_cohorts()
    {
        var fixture = new Fixture(); var first = await fixture.Activate(fixture.Executable("first", "Payment"), "a");
        var secondExecutable = fixture.Executable("second", "Shipping");
        var second = await fixture.Activate(secondExecutable, "b", first.Slot.Revision);
        Assert.True(second.Succeeded); Assert.Equal(WorkflowActivationProjectionState.Replaced, fixture.Interests.State("a"));
        var serving = await fixture.Interests.ObservePublicationAsync(fixture.Scope);
        Assert.Equal(2, serving.Epoch); Assert.Equal(["Shipping"], serving.Names);
        Assert.True((await fixture.Coordinator.DeactivateAsync(new(secondExecutable, "default", WorkflowActivationSource.Publishing, second.Slot.Revision))).Succeeded);
        var closed = await fixture.Interests.ObservePublicationAsync(fixture.Scope);
        Assert.Equal(3, closed.Epoch); Assert.Empty(closed.Names);
        Assert.Throws<NotSupportedException>(() => fixture.Interests.RequireCompleteAcceptanceCoverage());
    }

    [Fact]
    public async Task Observer_failure_reverts_only_its_own_transition_and_restores_prior_names()
    {
        var fixture = new Fixture(); var first = await fixture.Activate(fixture.Executable("first", "Payment"), "a");
        fixture.Observer.Fail = true;
        var failed = await fixture.Activate(fixture.Executable("second", "Shipping"), "b", first.Slot.Revision);
        Assert.False(failed.Succeeded); Assert.Equal(WorkflowActivationStep.TriggerObserverNotification, failed.FailedStep);
        var observed = await fixture.Interests.ObservePublicationAsync(fixture.Scope);
        Assert.Equal(["Payment"], observed.Names); Assert.Equal(3, observed.Epoch);
        Assert.Equal(WorkflowActivationProjectionState.Missing, fixture.Interests.State("b"));
        Assert.NotNull((await fixture.References.FindAsync(WorkflowActivationReferenceIdentity.Create("b")))!.DeletedAt);
    }

    [Fact]
    public async Task A_shared_activation_loser_cannot_discard_the_serving_interest_or_advance_its_epoch()
    {
        var fixture = new Fixture(); var result = await fixture.Activate(fixture.Executable("first", "Payment"), "a");
        Assert.False(await fixture.Switch.TryDiscardAsync(result.Reference!));
        var observed = await fixture.Interests.ObservePublicationAsync(fixture.Scope);
        Assert.Equal(1, observed.Epoch); Assert.Equal(["Payment"], observed.Names);
    }

    [Fact]
    public async Task A_changed_actual_material_incarnation_refuses_before_any_projection_or_slot_switches()
    {
        var fixture = new Fixture(); var executable = fixture.Executable("first", "Payment");
        await fixture.Prepare(executable, "a");
        var original = fixture.Materials.ReadNativeMaterialForMessageInterest("first")!;
        await fixture.Materials.SaveAsync(executable);
        Assert.Equal(original.IncarnationId, fixture.Materials.ReadNativeMaterialForMessageInterest("first")!.IncarnationId);
        await fixture.Materials.DeleteAsync("first"); await fixture.Materials.SaveAsync(executable);
        Assert.NotEqual(original.IncarnationId, fixture.Materials.ReadNativeMaterialForMessageInterest("first")!.IncarnationId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Switch.TryActivateAsync(
            new("definition", "default", "a", WorkflowActivationSource.Publishing, 0, Fixture.Now)).AsTask());
        Assert.Null(await fixture.Authority.FindAsync("definition", "default"));
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await fixture.Bindings.FindActivationStateAsync("a"));
        Assert.Equal(0, (await fixture.Interests.ObservePublicationAsync(fixture.Scope)).Epoch);
    }

    [Fact]
    public async Task A_missing_Message_projection_cannot_be_certified_by_the_same_artifact_fast_path()
    {
        var fixture = new Fixture(); var executable = fixture.Executable("first", "Payment");
        await fixture.Materials.SaveAsync(executable); await fixture.Indexer.PrepareActivationAsync(executable, "a", fixture.Slot);
        await fixture.References.SaveAsync(fixture.Reference(executable, "a"));
        var ordinarySwitch = new InMemoryWorkflowActivationSwitch(fixture.Authority, fixture.References, fixture.Clock, fixture.Bindings);
        await ordinarySwitch.TryActivateAsync(new("definition", "default", "a", WorkflowActivationSource.Publishing, 0, Fixture.Now));
        var result = await fixture.Coordinator.EnsureServingAsync("definition", "default");
        Assert.False(result.Succeeded); Assert.Equal(WorkflowActivationProjectionState.Missing, fixture.Interests.State("a"));
    }

    [Fact]
    public async Task Recognized_empty_retains_checked_projection_while_missing_provider_refuses_activation()
    {
        var empty = new Fixture(); var result = await empty.Activate(empty.Executable("empty", null), "a");
        Assert.True(result.Succeeded); Assert.Equal(WorkflowActivationProjectionState.Active, empty.Interests.State("a"));
        Assert.Empty((await empty.Interests.ObservePublicationAsync(empty.Scope)).Names);
        var missing = new Fixture(noProvider: true);
        Assert.False((await missing.Activate(missing.Executable("missing", "Payment"), "b")).Succeeded);
        Assert.Null(await missing.Authority.FindAsync("definition", "default"));
        Assert.Equal(WorkflowActivationProjectionState.Missing, missing.Interests.State("b"));
    }

    [Fact]
    public async Task Scope_drift_and_mixed_physical_tuple_refuse_before_root_preparation()
    {
        var fixture = new Fixture(); fixture.Access.Current = PersistenceAccessContext.Scoped(new("foreign"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Prepare(fixture.Executable("first", "Payment"), "a"));
        Assert.Throws<NotSupportedException>(() => new InMemoryMessagePublicationInterestStore(
            new InMemoryRuntimeCheckpointCommitStore(), new(), new InMemoryWorkflowExecutableSourceReferenceStore(), new(),
            new Access(), new([]), fixture.Clock));
    }

    [Fact]
    public async Task A_cancelled_switch_leaves_checked_preparation_nonserving_and_epoch_unchanged()
    {
        var fixture = new Fixture(); var executable = fixture.Executable("first", "Payment"); await fixture.Prepare(executable, "a");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Switch.TryActivateAsync(
            new("definition", "default", "a", WorkflowActivationSource.Publishing, 0, Fixture.Now), cancellation.Token).AsTask());
        Assert.Null(await fixture.Authority.FindAsync("definition", "default"));
        var observed = await fixture.Interests.ObservePublicationAsync(fixture.Scope); Assert.Equal(0, observed.Epoch); Assert.Empty(observed.Names);
    }

    [Fact]
    public async Task A_new_scoped_adapter_retains_the_actual_checkpoint_states_projection_and_epoch()
    {
        var fixture = new Fixture(); await fixture.Activate(fixture.Executable("first", "Payment"), "a");
        var another = new InMemoryMessagePublicationInterestStore(fixture.Checkpoints, fixture.Authority, fixture.References,
            fixture.Materials, fixture.Access, new([new Provider()]), fixture.Clock);
        var observed = await another.ObservePublicationAsync(fixture.Scope);
        Assert.Equal(1, observed.Epoch); Assert.Equal(["Payment"], observed.Names);
        Assert.Throws<NotSupportedException>(() => new InMemoryMessagePublicationInterestStore(fixture.Checkpoints,
            fixture.Authority, fixture.References, fixture.Materials, new ForeignAccess(), new([new Provider()]), fixture.Clock));
    }

    [Fact]
    public async Task A_legacy_multi_root_repair_preflights_the_complete_epoch_budget_before_any_mutation()
    {
        var fixture = new Fixture(); var first = await fixture.Activate(fixture.Executable("first", "Payment"), "a");
        await fixture.Prepare(fixture.Executable("second", "Shipping"), "b");
        await fixture.Prepare(fixture.Executable("third", "Invoice"), "c");
        // Actual pre-atomic-switch shape: other projections serve beside the slot's own activation.
        await fixture.Interests.UnderWriteGateAsync(async () =>
        {
            foreach (var id in new[] { "b", "c" })
            {
                await fixture.Bindings.ActivateAsync(id, null);
                var projection = (IInMemoryActivationProjection)fixture.Interests;
                lock (projection.SyncRoot) { projection.CheckSwitch(id, null); projection.Switch(id, null); }
            }
            return true;
        }, CancellationToken.None);
        await fixture.Bindings.DeleteByActivationAsync("a");
        await fixture.Indexer.PrepareActivationAsync(fixture.Executable("first", "Payment"), "a", fixture.Slot);
        var state = fixture.Checkpoints.State.MessagePublicationInterests!;
        lock (state.Gate) state.Epochs[fixture.Scope] = long.MaxValue - 1;
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Switch.TryRepairAsync(first.Slot, ["b", "c"]).AsTask());
        Assert.Equal(first.Slot, await fixture.Authority.FindAsync("definition", "default"));
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await fixture.Bindings.FindActivationStateAsync("a"));
        Assert.Equal(WorkflowActivationProjectionState.Active, fixture.Interests.State("b"));
        Assert.Equal(WorkflowActivationProjectionState.Active, fixture.Interests.State("c"));
        Assert.Equal(long.MaxValue - 1, state.Epochs[fixture.Scope]);
        Assert.Null((await fixture.References.FindAsync(WorkflowActivationReferenceIdentity.Create("b")))!.DeletedAt);
        Assert.Null((await fixture.References.FindAsync(WorkflowActivationReferenceIdentity.Create("c")))!.DeletedAt);
    }

    [Fact]
    public async Task Observation_refuses_a_serving_material_that_was_physically_removed_and_reinserted()
    {
        var fixture = new Fixture(); var executable = fixture.Executable("first", "Payment");
        await fixture.Activate(executable, "a");
        await fixture.Materials.DeleteAsync("first"); await fixture.Materials.SaveAsync(executable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Interests.ObservePublicationAsync(fixture.Scope).AsTask());
        Assert.False((await fixture.Coordinator.EnsureServingAsync("definition", "default")).Succeeded);
    }

    [Fact]
    public async Task Legacy_repair_revalidates_prepared_material_before_any_projection_switches()
    {
        var fixture = new Fixture(); var executable = fixture.Executable("first", "Payment"); await fixture.Prepare(executable, "a");
        var legacy = await fixture.Authority.TryActivateAsync(new("definition", "default", "a", WorkflowActivationSource.Publishing, 0, Fixture.Now));
        var reference = await fixture.References.FindAsync(WorkflowActivationReferenceIdentity.Create("a"));
        await fixture.Materials.DeleteAsync("first"); await fixture.Materials.SaveAsync(executable);
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Switch.TryRepairAsync(legacy.Slot, []).AsTask());
        Assert.Equal(legacy.Slot, await fixture.Authority.FindAsync("definition", "default"));
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await fixture.Bindings.FindActivationStateAsync("a"));
        Assert.Equal(WorkflowActivationProjectionState.Prepared, fixture.Interests.State("a"));
        Assert.Equal(0, (await fixture.Interests.ObservePublicationAsync(fixture.Scope)).Epoch);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(reference!, (await fixture.References.FindAsync(reference!.SourceReferenceId))!));
    }

    [Fact]
    public async Task Shared_content_can_publish_under_another_canonical_definition_and_version()
    {
        var fixture = new Fixture(); var first = fixture.Executable("shared", "Payment"); await fixture.Activate(first, "a");
        var another = new WorkflowExecutable(first.Identity with { DefinitionId = "other-definition", DefinitionVersionId = "other-version", ArtifactVersion = "2" },
            first.RootActivity, first.ResumeTargets, first.CreatedAt, first.CompatibilityMetadata, first.IncidentStrategy);
        await fixture.Materials.SaveAsync(another); // Actual content store retains its first source-fact carrier.
        Assert.Equal("definition", fixture.Materials.ReadNativeMaterialForMessageInterest("shared")!.Identity.DefinitionId);
        var result = await fixture.Activate(another, "b");
        Assert.True(result.Succeeded);
        Assert.Equal("other-definition", fixture.Checkpoints.State.MessagePublicationInterests!.Roots["b"].Definition.DefinitionId);
        Assert.Equal("definition", fixture.Checkpoints.State.MessagePublicationInterests.Roots["a"].Definition.DefinitionId);
    }

    [Theory]
    [InlineData("definition-version")]
    [InlineData("artifact-version")]
    public async Task Incoherent_canonical_reference_and_requested_publication_facts_cannot_enroll_interest(string changed)
    {
        var fixture = new Fixture(); var executable = fixture.Executable("first", "Payment"); await fixture.Materials.SaveAsync(executable);
        var reference = fixture.Reference(executable, "a");
        reference = changed == "definition-version" ? reference with { DefinitionVersionId = "wrong" } : reference with { ArtifactVersion = "wrong" };
        var result = await fixture.Coordinator.ActivateAsync(new(executable, reference, "default", "a", WorkflowActivationSource.Publishing, 0));
        Assert.False(result.Succeeded); Assert.Null(await fixture.Authority.FindAsync("definition", "default"));
        Assert.Equal(WorkflowActivationProjectionState.Missing, fixture.Interests.State("a"));
    }

    private sealed class Fixture
    {
        internal static readonly DateTimeOffset Now = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);
        internal readonly TimeProvider Clock = new FixedClock();
        internal readonly Access Access = new();
        internal readonly InMemoryWorkflowExecutableStore Materials = new();
        internal readonly InMemoryWorkflowExecutableSourceReferenceStore References;
        internal readonly InMemoryWorkflowActivationAuthority Authority = new();
        internal readonly InMemoryWorkflowTriggerBindingStore Bindings = new();
        internal readonly InMemoryMessagePublicationInterestStore Interests;
        internal readonly InMemoryRuntimeCheckpointCommitStore Checkpoints;
        internal readonly InMemoryWorkflowActivationSwitch Switch;
        internal readonly WorkflowActivationCoordinator Coordinator;
        internal readonly Indexer Indexer;
        internal readonly Observer Observer = new();
        internal MessageScope Scope => new(new("tenant"), "tenant");
        internal string Slot => WorkflowActivationSlotIdentity.Create("definition", "default");
        internal Fixture(bool noProvider = false)
        {
            References = new(Materials, Clock); Indexer = new(Bindings);
            Checkpoints = new InMemoryRuntimeCheckpointCommitStore(Materials, References, state: new());
            Interests = new(Checkpoints, Authority, References, Materials, Access,
                new(noProvider ? [] : [new Provider()]), Clock);
            Switch = new(Interests, Authority, References, Clock, Bindings);
            Coordinator = new(Interests, Authority, Switch, References,
                new WorkflowExecutableRootWriteLeaseManager(Materials, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock),
                Clock, Indexer, Bindings, triggerObservers: [Observer]);
        }
        internal WorkflowExecutable Executable(string id, string? name) => new(new(id, "definition", "version-" + id, "1", "sha256:" + id),
            new ExecutableNode("receive", "receive", "Fixture.Receive", "1", "fixture", JsonSerializer.SerializeToElement(new { name }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        internal WorkflowExecutableSourceReference Reference(WorkflowExecutable executable, string activation) => new(
            WorkflowActivationReferenceIdentity.Create(activation), executable.Identity.ArtifactId, "workflow-definition-version", executable.Identity.DefinitionVersionId, executable.Identity.ArtifactVersion,
            executable.Identity.DefinitionId, executable.Identity.DefinitionVersionId, executable.Identity.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.Published,
            ActivationId: activation, SlotId: WorkflowActivationSlotIdentity.Create(executable.Identity.DefinitionId, "default"), TenantId: "tenant");
        internal async Task Prepare(WorkflowExecutable executable, string activation)
        {
            await Materials.SaveAsync(executable); await References.SaveAsync(Reference(executable, activation));
            await Indexer.PrepareActivationAsync(executable, activation, Slot);
            await Interests.PrepareAsync(executable, activation, Slot, "default", CancellationToken.None);
        }
        internal async Task<WorkflowActivationResult> Activate(WorkflowExecutable executable, string activation, long revision = 0)
        {
            await Materials.SaveAsync(executable);
            return await Coordinator.ActivateAsync(new(executable, Reference(executable, activation), "default", activation,
                WorkflowActivationSource.Publishing, revision));
        }
    }
    private sealed class Access : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new("tenant")); }
    private sealed class ForeignAccess : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("foreign")); }
    private sealed class FixedClock : TimeProvider { public override DateTimeOffset GetUtcNow() => Fixture.Now; }
    private sealed class Provider : IMessageMaterialInterestProvider
    {
        public string ProviderId => "fixture.native";
        public RuntimeExecutionProtocol? ModelProtocol => null;
        public MessageMaterialInterestResult Describe(RuntimeExecutionMaterial material)
        {
            var name = material.NativeWorkflow!.RootActivity.DescriptorPayload.GetProperty("name");
            return MessageMaterialInterestResult.Recognized(name.ValueKind == JsonValueKind.Null ? [] : [new("receive", [name.GetString()!])]);
        }
    }
    private sealed class Indexer(InMemoryWorkflowTriggerBindingStore bindings) : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(WorkflowExecutable executable,
            string activationId, string slotId, CancellationToken cancellationToken = default)
        { await bindings.PrepareActivationAsync(activationId, [], cancellationToken); return []; }
    }
    private sealed class Observer : IWorkflowTriggerIndexObserver
    {
        internal bool Fail;
        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default) =>
            Fail ? ValueTask.FromException(new InvalidOperationException("Fixture observer refusal.")) : ValueTask.CompletedTask;
    }
}
