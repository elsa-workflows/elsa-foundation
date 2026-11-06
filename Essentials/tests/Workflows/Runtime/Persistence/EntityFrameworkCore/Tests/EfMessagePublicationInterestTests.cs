using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Messaging;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Options;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>SQLite component witnesses only; no accepted cohort, active/model coverage, or provider fleet claim.</summary>
public sealed class EfMessagePublicationInterestTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase database = new("message-publication");
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 4, 0, 0, TimeSpan.Zero);
    private const string Scope = "tenant-a";
    private const string Slot = "default";

    [Fact]
    public async Task Receive_only_preparation_is_nonserving_then_switch_exposes_names_and_one_epoch()
    {
        await using var node = await OpenAsync();
        var executable = Material("one");
        await node.PrepareAsync(executable, "a");
        Assert.Equal(WorkflowActivationProjectionState.Prepared, await node.Interests.FindStateAsync("a", default));
        Assert.Empty((await node.Interests.ObservePublicationAsync(node.MessageScope)).Names);
        Assert.Equal(0, await node.EpochAsync());
        Assert.True((await node.ActivateAsync(executable, "a", 0)).Succeeded);
        Assert.Equal(new[] { "orders" }, (await node.Interests.ObservePublicationAsync(node.MessageScope)).Names);
        Assert.Equal(1, await node.EpochAsync());
        Assert.Empty((await node.Bindings.ListByActivationAsync(new("a"))).Items);
        Assert.Throws<NotSupportedException>(node.Interests.RequireCompleteAcceptanceCoverage);
    }

    [Fact]
    public async Task Actual_coordinator_prepares_receive_only_projection_and_switches_with_real_retention_lease()
    {
        await using var node = await OpenAsync();
        var executable = Material("coordinator");
        await node.Material.SaveAsync(executable);
        var leases = new WorkflowExecutableRootWriteLeaseManager(node.Material,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions()), TimeProvider.System);
        var coordinator = node.Interests.CreateCoordinator(node.Switch, leases, new EmptyIndexer(node.Bindings), node.Bindings);
        var result = await coordinator.ActivateAsync(new(executable, Reference(executable, "ignored"), Slot,
            "coordinated", WorkflowActivationSource.Publishing, 0));
        Assert.True(result.Succeeded, result.Diagnostic);
        Assert.Equal(WorkflowActivationProjectionState.Active, await node.Interests.FindStateAsync("coordinated", default));
        Assert.Equal(1, await node.EpochAsync());
    }

    [Fact]
    public async Task Replacement_and_revert_restore_original_root_without_reextracting_or_changing_its_descriptor()
    {
        await using var node = await OpenAsync();
        var first = Material("replace", 1); var second = Material("replace", 2);
        await node.PrepareAsync(first, "a"); await node.PrepareAsync(second, "b");
        Assert.True((await node.ActivateAsync(first, "a", 0)).Succeeded);
        var immutable = (await node.Context.MessagePublicationInterests.AsNoTracking().SingleAsync(row => row.ActivationId == Encode("a"))).DescriptorFingerprint;
        var transition = await node.ActivateAsync(second, "b", 1); Assert.True(transition.Succeeded);
        Assert.Equal(WorkflowActivationProjectionState.Replaced, await node.Interests.FindStateAsync("a", default));
        Assert.True(await node.Switch.TryRevertAsync(new(transition, Reference(second, "b"), WorkflowActivationSource.Publishing, Now)));
        Assert.Equal(WorkflowActivationProjectionState.Active, await node.Interests.FindStateAsync("a", default));
        Assert.Equal(WorkflowActivationProjectionState.Missing, await node.Interests.FindStateAsync("b", default));
        Assert.Equal(3, await node.EpochAsync());
        Assert.Equal(immutable, (await node.Context.MessagePublicationInterests.AsNoTracking().SingleAsync(row => row.ActivationId == Encode("a"))).DescriptorFingerprint);
    }

    [Fact]
    public async Task Deactivation_closes_publication_but_retains_immutable_root_and_closed_epoch()
    {
        await using var node = await OpenAsync(); var executable = Material("close");
        await node.PrepareAsync(executable, "a"); await node.ActivateAsync(executable, "a", 0);
        Assert.True((await node.Switch.TryDeactivateAsync(new(executable.Identity.DefinitionId, Slot,
            WorkflowActivationSource.Publishing, 1, Now), [])).Succeeded);
        Assert.Empty((await node.Interests.ObservePublicationAsync(node.MessageScope)).Names);
        var row = await node.Context.MessagePublicationInterests.AsNoTracking().SingleAsync();
        var closed = MessagePublicationInterestDocument.Read(row, Scope, "a");
        Assert.Equal(MessagePublicationInterestDocument.Closed, closed.State);
        Assert.Equal(2, closed.ClosedAtEpoch); Assert.Equal(2, await node.EpochAsync());
        Assert.Equal(executable.Identity.DefinitionId, closed.Descriptor.Source.DefinitionId);
    }

    [Fact]
    public async Task Discard_of_prepared_projection_closes_without_a_serving_epoch_and_retires_owned_reference()
    {
        await using var node = await OpenAsync(); var executable = Material("discard");
        await node.PrepareAsync(executable, "a");
        Assert.True(await node.Switch.TryDiscardAsync(Reference(executable, "a")));
        Assert.Equal(0, await node.EpochAsync());
        Assert.Equal(WorkflowActivationProjectionState.Missing, await node.Interests.FindStateAsync("a", default));
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason,
            (await node.References.FindAsync(WorkflowActivationReferenceIdentity.Create("a")))!.DeletedReason);
    }

    [Fact]
    public async Task Legacy_partial_slot_can_be_repaired_only_from_retained_prepared_root()
    {
        await using var node = await OpenAsync(); var executable = Material("repair");
        await node.PrepareAsync(executable, "a");
        var slot = await node.Authority.TryActivateAsync(new(executable.Identity.DefinitionId, Slot, "a", WorkflowActivationSource.Publishing, 0, Now));
        Assert.True(await node.Switch.TryRepairAsync(slot.Slot, []));
        Assert.Equal(WorkflowActivationProjectionState.Active, await node.Interests.FindStateAsync("a", default));
        Assert.Equal(1, await node.EpochAsync());
    }

    [Fact]
    public async Task Repair_previews_the_exact_owner_resume_rule_when_an_active_root_has_a_failed_reference()
    {
        await using var node = await OpenAsync(); var executable = Material("repair-resume");
        await node.PrepareAsync(executable, "a"); var transition = await node.ActivateAsync(executable, "a", 0);
        await node.Bindings.DeleteByActivationAsync("a"); await node.Bindings.PrepareActivationAsync("a", []);
        Assert.True(await node.References.RetireAsync(WorkflowActivationReferenceIdentity.Create("a"), Now,
            WorkflowActivationCoordinator.FailedRetireReason));
        Assert.True(await node.Switch.TryRepairAsync(transition.Slot, []));
        Assert.Equal(WorkflowActivationProjectionState.Active, await node.Bindings.FindActivationStateAsync("a"));
        Assert.Equal(WorkflowActivationProjectionState.Active, await node.Interests.FindStateAsync("a", default));
        Assert.Null((await node.References.FindAsync(WorkflowActivationReferenceIdentity.Create("a")))!.DeletedAt);
        Assert.Equal(1, await node.EpochAsync()); // The root already served; reference repair is not a new eligibility transition.
    }

    [Fact]
    public async Task Checked_root_refuses_negative_retained_close_history_even_when_prepared()
    {
        await using var node = await OpenAsync(); var executable = Material("bad-history"); await node.PrepareAsync(executable, "a");
        var row = await node.Context.MessagePublicationInterests.AsNoTracking().SingleAsync();
        var document = MessagePublicationInterestDocument.Read(row, Scope, "a");
        row.ContentJson = RuntimeArtifactJson.Serialize(document with { ClosedAtEpoch = -1 });
        Assert.Throws<InvalidDataException>(() => MessagePublicationInterestDocument.Read(row, Scope, "a"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checked_retained_root_refuses_structurally_invalid_source_facts_even_with_matching_fingerprint(bool closed)
    {
        await using var node = await OpenAsync(); var executable = Material("bad-source"); await node.PrepareAsync(executable, "a");
        var row = await node.Context.MessagePublicationInterests.AsNoTracking().SingleAsync();
        var document = MessagePublicationInterestDocument.Read(row, Scope, "a");
        var descriptor = document.Descriptor with { Source = document.Descriptor.Source with { SourceKind = "" } };
        row.DescriptorFingerprint = MessagePublicationInterestDocument.Fingerprint(descriptor);
        row.State = closed ? MessagePublicationInterestDocument.Closed : (int)WorkflowActivationProjectionState.Prepared;
        row.ContentJson = RuntimeArtifactJson.Serialize(document with { Descriptor = descriptor, State = row.State, ClosedAtEpoch = closed ? 1 : null });
        Assert.Throws<InvalidDataException>(() => MessagePublicationInterestDocument.Read(row, Scope, "a"));
    }

    [Fact]
    public async Task Repair_refuses_reinserted_material_before_committing_any_projection_or_epoch()
    {
        await using var node = await OpenAsync(); var executable = Material("stale");
        await node.PrepareAsync(executable, "a");
        var slot = await node.Authority.TryActivateAsync(new(executable.Identity.DefinitionId, Slot, "a", WorkflowActivationSource.Publishing, 0, Now));
        Assert.True(await node.Material.DeleteAsync(executable.Identity.ArtifactId)); await node.Material.SaveAsync(executable);
        var before = await node.SnapshotAsync();
        await Assert.ThrowsAnyAsync<InvalidOperationException>(() => node.Switch.TryRepairAsync(slot.Slot, []).AsTask());
        Assert.Equal(before, await node.SnapshotAsync());
    }

    [Fact]
    public async Task Save_failure_rolls_back_slot_trigger_scope_reference_and_material_revision_fences()
    {
        var failure = new Failure(); await using var node = await OpenAsync(failure);
        var executable = Material("rollback"); await node.PrepareAsync(executable, "a");
        var before = await node.SnapshotAsync(); failure.Enabled = true;
        await Assert.ThrowsAsync<InjectedFailure>(() => node.ActivateAsync(executable, "a", 0).AsTask());
        failure.Enabled = false; Assert.Equal(before, await node.SnapshotAsync());
        Assert.Null(await node.Authority.FindAsync(executable.Identity.DefinitionId, Slot));
    }

    [Fact]
    public async Task Independent_context_stale_slot_contender_cannot_commit_a_second_epoch_or_any_fence()
    {
        await using var winner = await OpenAsync(); var a = Material("race", 1); var b = Material("race", 2);
        await winner.PrepareAsync(a, "a"); await winner.PrepareAsync(b, "b");
        await using var stale = new Node(OpenContext());
        Assert.Null(await stale.Authority.FindAsync(a.Identity.DefinitionId, Slot));
        Assert.True((await winner.ActivateAsync(a, "a", 0)).Succeeded);
        var before = await winner.SnapshotAsync();
        var lost = await stale.ActivateAsync(b, "b", 0);
        Assert.False(lost.Succeeded); Assert.Equal(WorkflowActivationConflict.RevisionMismatch, lost.Conflict);
        Assert.Equal(before, await winner.SnapshotAsync()); Assert.Equal(1, await winner.EpochAsync());
    }

    [Fact]
    public async Task Scope_epoch_overflow_refuses_before_slot_or_projection_mutations()
    {
        await using var node = await OpenAsync(); var executable = Material("overflow"); await node.PrepareAsync(executable, "a");
        var control = await node.Context.MessageInterestScopeControls.SingleAsync(); control.Epoch = long.MaxValue;
        control.ContentJson = RuntimeArtifactJson.Serialize(new MessageInterestControlDocument(1, node.MessageScope, long.MaxValue));
        await node.Context.SaveChangesAsync(); node.Context.ChangeTracker.Clear(); var before = await node.SnapshotAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => node.ActivateAsync(executable, "a", 0).AsTask());
        Assert.Equal(before, await node.SnapshotAsync());
    }

    [Fact]
    public async Task Unknown_canonical_schema_refuses_before_any_other_owner_is_written()
    {
        await using var node = await OpenAsync(); var executable = Material("future"); await node.PrepareAsync(executable, "a");
        await node.Context.MessagePublicationInterests.ExecuteUpdateAsync(set => set.SetProperty(row => row.SchemaVersion, "99.0.0"));
        var before = await node.SnapshotAsync();
        await Assert.ThrowsAsync<EfSchemaVersionSkewException>(() => node.ActivateAsync(executable, "a", 0).AsTask());
        Assert.Equal(before, await node.SnapshotAsync());
        Assert.Null(await node.Authority.FindAsync(executable.Identity.DefinitionId, Slot));
    }

    [Fact]
    public async Task Actual_owner_tuple_refuses_a_different_context_or_ordinary_scope()
    {
        await using var node = await OpenAsync(); await using var foreign = new Node(OpenContext(), "tenant-b");
        Assert.Throws<NotSupportedException>(() => new EfWorkflowActivationSwitch(node.Interests,
            foreign.Authority, foreign.Bindings, foreign.References, foreign.Access, TimeProvider.System));
        Assert.Throws<NotSupportedException>(() => new EfMessagePublicationInterestStore(node.Context, node.Authority,
            node.References, node.Material, foreign.Access, [new Provider()], TimeProvider.System));
    }

    [Fact]
    public async Task Shared_content_with_different_publication_provenance_keeps_actual_reference_owner()
    {
        await using var node = await OpenAsync(); var original = Material("shared"); await node.Material.SaveAsync(original);
        var republished = new WorkflowExecutable(new(original.Identity.ArtifactId, "new-definition", "new-version", "2.0.0", original.Identity.ArtifactHash),
            original.RootActivity, original.ResumeTargets, original.CreatedAt, original.CompatibilityMetadata, original.IncidentStrategy);
        await node.PrepareAsync(republished, "shared-b"); Assert.True((await node.ActivateAsync(republished, "shared-b", 0)).Succeeded);
        var row = await node.Context.MessagePublicationInterests.AsNoTracking().SingleAsync();
        Assert.Equal("new-definition", MessagePublicationInterestDocument.Read(row, Scope, "shared-b").Descriptor.Source.DefinitionId);
        Assert.Equal(original.Identity.DefinitionId, (await node.Material.FindAsync(original.Identity.ArtifactId))!.Identity.DefinitionId);
    }

    [Theory]
    [InlineData("definition")]
    [InlineData("version")]
    public async Task Preparation_refuses_requested_source_facts_that_disagree_with_actual_reference(string field)
    {
        await using var node = await OpenAsync(); var executable = Material("mismatch");
        await node.Material.SaveAsync(executable); await node.References.SaveAsync(Reference(executable, "a"));
        var identity = executable.Identity;
        var requested = new WorkflowExecutable(new(identity.ArtifactId, field == "definition" ? "other" : identity.DefinitionId,
                field == "version" ? "other" : identity.DefinitionVersionId, identity.ArtifactVersion, identity.ArtifactHash),
            executable.RootActivity, executable.ResumeTargets, executable.CreatedAt, executable.CompatibilityMetadata, executable.IncidentStrategy);
        await Assert.ThrowsAsync<NotSupportedException>(() => node.Interests.PrepareAsync(requested, "a",
            WorkflowActivationSlotIdentity.Create(requested.Identity.DefinitionId, Slot), Slot, default).AsTask());
        Assert.Empty(await node.Context.MessagePublicationInterests.AsNoTracking().ToArrayAsync());
        Assert.Empty(await node.Context.MessageInterestScopeControls.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(null, "ordinary")]
    [InlineData("a", "A")]
    [InlineData("ab:c", "ab")]
    public void Exact_scope_control_identity_does_not_alias_nullable_case_framing_or_opaque_utf16(string? left, string? right)
    {
        var a = new MessageScope(new(Scope), left); var b = new MessageScope(new(Scope), right);
        Assert.NotEqual(MessagePublicationInterestDocument.ScopeIdentity(a), MessagePublicationInterestDocument.ScopeIdentity(b));
        Assert.NotEqual(MessagePublicationInterestDocument.ScopeId(a), MessagePublicationInterestDocument.ScopeId(b));
    }

    [Fact]
    public void Opaque_surrogate_identity_is_not_replaced_by_custom_attribute_utf8_encoding()
    {
        var opaque = new MessageScope(new(Scope), new string((char)0xD800, 1));
        var replacement = new MessageScope(new(Scope), "\ufffd");
        Assert.NotEqual(MessagePublicationInterestDocument.ScopeIdentity(opaque), MessagePublicationInterestDocument.ScopeIdentity(replacement));
        Assert.NotEqual(MessagePublicationInterestDocument.ScopeId(opaque), MessagePublicationInterestDocument.ScopeId(replacement));
    }

    private async Task<Node> OpenAsync(Failure? failure = null)
    {
        var node = new Node(OpenContext(failure)); await node.Context.Database.EnsureCreatedAsync(); return node;
    }
    private RuntimeSqliteDbContext OpenContext(Failure? failure = null)
    {
        var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.ConnectionString);
        if (failure is not null) builder.AddInterceptors(failure);
        return new(builder.Options);
    }
    private static string Encode(string value) => Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode(value);
    private static WorkflowExecutable Material(string prefix, int version = 1) => RuntimeEventExecutableTestFixture.Create(prefix, version);
    private static WorkflowExecutableSourceReference Reference(WorkflowExecutable executable, string activation) => new(
        WorkflowActivationReferenceIdentity.Create(activation), executable.Identity.ArtifactId, "workflow-definition-version",
        executable.Identity.DefinitionVersionId, executable.Identity.ArtifactVersion, executable.Identity.DefinitionId,
        executable.Identity.DefinitionVersionId, executable.Identity.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.Published,
        ActivationId: activation, SlotId: WorkflowActivationSlotIdentity.Create(executable.Identity.DefinitionId, Slot), TenantId: Scope);
    public ValueTask DisposeAsync() => database.DisposeAsync();

    private sealed class Node : IAsyncDisposable
    {
        internal Node(RuntimeSqliteDbContext context, string scope = Scope)
        {
            Context = context; Access = new(scope); MessageScope = new(new(scope), scope);
            var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
            { SigningKey = "message-publication-fixture-signing-key-32", AllowEphemeralDevelopmentKey = false }));
            Authority = new(context, Access); Bindings = new(context, Access); Material = new(context, Access);
            References = new(context, Access, codec); Interests = new(context, Authority, References, Material, Access, [new Provider()], TimeProvider.System);
            Switch = new(Interests, Authority, Bindings, References, Access, TimeProvider.System);
        }
        internal RuntimeSqliteDbContext Context { get; }
        internal Accessor Access { get; }
        internal MessageScope MessageScope { get; }
        internal EfWorkflowActivationAuthority Authority { get; }
        internal EfWorkflowTriggerBindingStore Bindings { get; }
        internal EfWorkflowExecutableSourceReferenceStore References { get; }
        internal EfWorkflowExecutableStore Material { get; }
        internal EfMessagePublicationInterestStore Interests { get; }
        internal EfWorkflowActivationSwitch Switch { get; }
        internal async Task PrepareAsync(WorkflowExecutable executable, string activation)
        {
            await Material.SaveAsync(executable); await References.SaveAsync(Reference(executable, activation));
            await Bindings.PrepareActivationAsync(activation, []);
            await Interests.PrepareAsync(executable, activation, WorkflowActivationSlotIdentity.Create(executable.Identity.DefinitionId, Slot), Slot, default);
        }
        internal ValueTask<WorkflowActivationTransition> ActivateAsync(WorkflowExecutable executable, string activation, long revision) =>
            Switch.TryActivateAsync(new(executable.Identity.DefinitionId, Slot, activation, WorkflowActivationSource.Publishing, revision, Now));
        internal async Task<long> EpochAsync() => (await Interests.ObservePublicationAsync(MessageScope)).Epoch;
        internal async Task<string> SnapshotAsync()
        {
            Context.ChangeTracker.Clear();
            return RuntimeArtifactJson.Serialize(new
            {
                Slots = await Context.WorkflowActivationSlots.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
                Bindings = await Context.WorkflowTriggerBindings.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
                Projections = await Context.WorkflowTriggerBindingProjectionStates.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
                References = await Context.WorkflowExecutableSourceReferences.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
                Materials = await Context.WorkflowExecutableCoordinations.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
                Roots = await Context.MessagePublicationInterests.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync(),
                Controls = await Context.MessageInterestScopeControls.AsNoTracking().OrderBy(row => row.Id).ToArrayAsync()
            });
        }
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }
    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(scope)); }
    private sealed class Provider : IMessageMaterialInterestProvider
    {
        public string ProviderId => "test.native.receive-only";
        public RuntimeExecutionProtocol? ModelProtocol => null;
        public MessageMaterialInterestResult Describe(RuntimeExecutionMaterial material) => MessageMaterialInterestResult.Recognized(
            [new(material.NativeWorkflow!.RootActivity.ExecutableNodeId, ["orders"])]);
    }
    private sealed class EmptyIndexer(IWorkflowTriggerBindingStore bindings) : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(WorkflowExecutable executable,
            string activationId, string slotId, CancellationToken cancellationToken = default)
        { await bindings.PrepareActivationAsync(activationId, [], cancellationToken); return []; }
    }
    private sealed class InjectedFailure : Exception { }
    private sealed class Failure : SaveChangesInterceptor
    {
        internal bool Enabled;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default) => Enabled ? throw new InjectedFailure() : ValueTask.FromResult(result);
    }
}
