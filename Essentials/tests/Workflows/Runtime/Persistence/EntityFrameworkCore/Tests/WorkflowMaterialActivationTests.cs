using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class WorkflowMaterialActivationTests
{
    [Fact]
    public async Task Slot_route_replacement_across_logical_tenants_releases_the_old_owner_and_deactivates()
    {
        var physical = new PersistenceScope("shared-physical-scope");
        await using var host = await Harness.OpenAsync(physical);
        var oldMaterial = await host.NativeAsync("old-route");
        var newMaterial = await host.NativeAsync("new-route");
        var coordinator = host.Coordinator(exclusiveRoutes: true);
        var firstCommand = host.Command(oldMaterial, "tenant-a-activation") with
        { Reference = host.Reference(oldMaterial) with { TenantId = "tenant-a" } };
        var first = await coordinator.ActivateMaterialAsync(firstCommand);
        Assert.True(first.Succeeded);
        var reader = new EfIngressReservationStore(host.Context, new ScopedAccess(physical));
        var tenantA = new TriggerScope(physical, "tenant-a"); var tenantB = new TriggerScope(physical, "tenant-b");
        Assert.NotNull(await reader.FindAsync(tenantA, "Http", "old-route"));

        var secondCommand = host.Command(newMaterial, "tenant-b-activation", first.Slot.Revision) with
        { Reference = host.Reference(newMaterial) with { TenantId = "tenant-b" } };
        var second = await coordinator.ActivateMaterialAsync(secondCommand);
        Assert.True(second.Succeeded);
        Assert.Null(await reader.FindAsync(tenantA, "Http", "old-route"));
        Assert.Equal(second.Slot.SlotId, (await reader.FindAsync(tenantB, "Http", "new-route"))!.OwnerId);
        var removed = await coordinator.DeactivateMaterialAsync(new("logical-definition", newMaterial, "default",
            WorkflowActivationSource.Publishing, second.Slot.Revision));
        Assert.True(removed.Succeeded);
        Assert.Empty((await reader.ListInScopeAsync(physical, new("Http"))).Items);
    }

    [Fact]
    public async Task Protected_logical_sources_keep_opaque_physical_scope_and_tenant_distinct_and_generic_saves_cannot_forge_it()
    {
        var physical = new PersistenceScope("physical-shell");
        await using var host = await Harness.OpenAsync(physical);
        var material = await host.ModelAsync("scoped-source");
        var command = host.Command(material, "scoped-publication") with { Reference = host.Reference(material) with { TenantId = "actual-tenant" } };
        var published = await host.Coordinator().ActivateMaterialAsync(command);
        Assert.True(published.Succeeded);
        var source = published.Reference!;
        Assert.Equal(physical, source.PublicationScope);
        Assert.Equal("actual-tenant", source.TenantId);
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion,
            (await host.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).SchemaVersion);

        var forgedId = WorkflowActivationReferenceIdentity.Create("forged-publication");
        await Assert.ThrowsAsync<NotSupportedException>(() => host.References.SaveAsync(source with { SourceReferenceId = forgedId, ActivationId = "forged-publication" }).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => host.References.SaveWithRootWriteLeasesAsync(source, material.Identity, []).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.References.SaveAsync(command.Reference with { SourceReferenceId = "legacy-unpaired" }).AsTask());
        Assert.Null(await host.References.FindAsync(forgedId));
        Assert.Null(await host.References.FindAsync("legacy-unpaired"));
        var retired = source.Retire(host.Clock.GetUtcNow(), "retired");
        Assert.True(await host.References.TryRetireAsync(source, retired));
        var readRetired = (await host.References.FindAsync(source.SourceReferenceId))!;
        Assert.Equal(physical, readRetired.PublicationScope);
        Assert.Equal("actual-tenant", readRetired.TenantId);
        Assert.True(await host.References.TryRestoreAsync(readRetired, source));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(source, (await host.References.FindAsync(source.SourceReferenceId))!));
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion,
            (await host.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).SchemaVersion);
    }

    [Fact]
    public async Task Model_activation_uses_the_logical_source_and_real_empty_projections_without_a_native_root()
    {
        await using var host = await Harness.OpenAsync();
        var material = await host.ModelAsync("business");

        var result = await host.Coordinator().ActivateMaterialAsync(host.Command(material, "publication-1"));

        Assert.True(result.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Activated, result.Outcome);
        Assert.Equal("logical-definition", result.Slot.WorkflowDefinitionId);
        Assert.Equal("logical-definition", result.Reference!.DefinitionId);
        Assert.Equal("authored-version", result.Reference.DefinitionVersionId);
        Assert.NotEqual(material.Identity.DefinitionId, result.Reference.DefinitionId);
        Assert.Null(await host.Materials.FindAsync(material.Identity.ArtifactId));
        Assert.Equal(material.Identity, (await host.Materials.FindMaterialAsync(material.Identity.ArtifactId))!.Identity);
        Assert.Empty(await host.Bindings.ListAllByActivationAsync("publication-1"));
        Assert.Empty(await host.Schedules.ListByActivationAsync("publication-1"));
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.Bindings.FindActivationStateAsync("publication-1"));
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.Schedules.FindActivationStateAsync("publication-1"));
    }

    [Fact]
    public async Task Native_and_model_material_replace_the_same_slot_in_both_directions()
    {
        await using var host = await Harness.OpenAsync();
        var native = await host.NativeAsync("native-1");
        var first = await host.Coordinator().ActivateMaterialAsync(host.Command(native, "native-publication"));
        Assert.Single(await host.Bindings.ListAllByActivationAsync("native-publication"));

        var model = await host.ModelAsync("model");
        var second = await host.Coordinator().ActivateMaterialAsync(host.Command(model, "model-publication", first.Slot.Revision));
        Assert.True(second.Succeeded);
        Assert.Equal(first.Slot.SlotId, second.Slot.SlotId);
        Assert.Equal("native-publication", second.ReplacedActivationId);
        Assert.Equal(WorkflowActivationProjectionState.Replaced, await host.Bindings.FindActivationStateAsync("native-publication"));
        Assert.NotNull((await host.References.FindAsync(first.Reference!.SourceReferenceId))!.DeletedAt);

        var third = await host.Coordinator().ActivateMaterialAsync(host.Command(native, "native-again", second.Slot.Revision));
        Assert.True(third.Succeeded);
        Assert.Equal(first.Slot.SlotId, third.Slot.SlotId);
        Assert.Equal("model-publication", third.ReplacedActivationId);
        Assert.Single(await host.Bindings.ListAllByActivationAsync("native-again"));
        Assert.Equal(WorkflowActivationProjectionState.Replaced, await host.Schedules.FindActivationStateAsync("model-publication"));
        Assert.NotNull((await host.References.FindAsync(second.Reference!.SourceReferenceId))!.DeletedAt);
    }

    [Fact]
    public async Task Shared_model_material_keeps_independent_logical_slots_and_returns_the_actual_no_op_source()
    {
        await using var host = await Harness.OpenAsync();
        var material = await host.ModelAsync("shared");
        var first = await host.Coordinator().ActivateMaterialAsync(host.Command(material, "first"));
        var another = host.Command(material, "another-definition") with
        {
            WorkflowDefinitionId = "second-definition",
            Reference = host.Reference(material) with { DefinitionId = "second-definition", DefinitionVersionId = "second-version" }
        };
        var second = await host.Coordinator().ActivateMaterialAsync(another);
        var noOp = await host.Coordinator().ActivateMaterialAsync(host.Command(material, "no-op", first.Slot.Revision) with
        {
            Reference = host.Reference(material) with { DefinitionVersionId = "new-authored-version", SourceId = "new-authored-version" }
        });

        Assert.True(second.Succeeded);
        Assert.NotEqual(first.Slot.SlotId, second.Slot.SlotId);
        Assert.Equal(WorkflowActivationOutcome.AlreadyActive, noOp.Outcome);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(first.Reference!, noOp.Reference!));
        Assert.Equal("authored-version", noOp.Reference!.DefinitionVersionId);
        Assert.Null(await host.References.FindAsync(WorkflowActivationReferenceIdentity.Create("no-op")));
        Assert.Equal(first.Slot, await host.Authority.FindAsync("logical-definition", "default"));
        Assert.Equal(second.Slot, await host.Authority.FindAsync("second-definition", "default"));
    }

    [Fact]
    public async Task A_stale_material_candidate_is_discarded_without_replacing_its_serving_predecessor()
    {
        await using var host = await Harness.OpenAsync();
        var first = await host.Coordinator().ActivateMaterialAsync(host.Command(await host.ModelAsync("first"), "first"));
        var result = await host.Coordinator().ActivateMaterialAsync(host.Command(await host.ModelAsync("candidate"), "candidate"));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationConflict.RevisionMismatch, result.Conflict);
        Assert.Equal(first.Slot, result.Slot);
        Assert.Equal(WorkflowActivationProjectionState.Missing, await host.Bindings.FindActivationStateAsync("candidate"));
        Assert.Equal(WorkflowActivationProjectionState.Missing, await host.Schedules.FindActivationStateAsync("candidate"));
        Assert.Equal(WorkflowActivationCoordinator.FailedRetireReason,
            (await host.References.FindAsync(WorkflowActivationReferenceIdentity.Create("candidate")))!.DeletedReason);
    }

    [Fact]
    public async Task Model_observer_failure_reverts_only_its_own_transition_and_restores_the_predecessor()
    {
        await using var host = await Harness.OpenAsync();
        var first = await host.Coordinator().ActivateMaterialAsync(host.Command(await host.ModelAsync("first"), "first"));
        var observer = new ThrowOnceObserver();

        var result = await host.Coordinator(observer).ActivateMaterialAsync(
            host.Command(await host.ModelAsync("second"), "second", first.Slot.Revision));

        Assert.False(result.Succeeded);
        Assert.Equal(WorkflowActivationStep.TriggerObserverNotification, result.FailedStep);
        Assert.Null(result.CompensationDiagnostic);
        Assert.Equal("first", result.Slot.ActiveActivationId);
        Assert.Null((await host.References.FindAsync(first.Reference!.SourceReferenceId))!.DeletedAt);
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.Bindings.FindActivationStateAsync("first"));
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.Schedules.FindActivationStateAsync("first"));
        Assert.Equal(WorkflowActivationProjectionState.Missing, await host.Bindings.FindActivationStateAsync("second"));
    }

    [Fact]
    public async Task Model_deactivation_and_failed_observer_compensation_use_the_empty_projection_lifecycle()
    {
        await using var host = await Harness.OpenAsync();
        var material = await host.ModelAsync("model");
        var active = await host.Coordinator().ActivateMaterialAsync(host.Command(material, "active"));
        var command = new WorkflowMaterialDeactivationCommand("logical-definition", material, "default", WorkflowActivationSource.Publishing, active.Slot.Revision);

        var failed = await host.Coordinator(new ThrowOnceObserver()).DeactivateMaterialAsync(command);
        Assert.False(failed.Succeeded);
        Assert.Null(failed.CompensationDiagnostic);
        Assert.Equal("active", failed.Slot.ActiveActivationId);
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.Schedules.FindActivationStateAsync("active"));

        var removed = await host.Coordinator().DeactivateMaterialAsync(command with { ExpectedRevision = failed.Slot.Revision });
        Assert.True(removed.Succeeded);
        Assert.Equal(WorkflowActivationOutcome.Deactivated, removed.Outcome);
        Assert.Null(removed.Slot.ActiveActivationId);
        // Publishing retires the selected reference after its coordinated unpublish, as on the native path.
        Assert.Null((await host.References.FindAsync(active.Reference!.SourceReferenceId))!.DeletedAt);
        Assert.Equal(WorkflowActivationProjectionState.Missing, await host.Bindings.FindActivationStateAsync("active"));
    }

    [Fact]
    public async Task Ensuring_a_model_slot_repairs_prepared_empty_projection_states()
    {
        await using var host = await Harness.OpenAsync();
        var active = await host.Coordinator().ActivateMaterialAsync(host.Command(await host.ModelAsync("model"), "active"));
        await host.Bindings.DeleteByActivationAsync("active");
        await host.Schedules.DeleteByActivationAsync("active");
        await host.Bindings.PrepareActivationAsync("active", []);
        await host.Schedules.PrepareActivationAsync("active", []);

        var repaired = await host.Coordinator().EnsureServingAsync("logical-definition", "default");

        Assert.True(repaired.Succeeded);
        Assert.Equal(active.Slot, repaired.Slot);
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.Bindings.FindActivationStateAsync("active"));
        Assert.Equal(WorkflowActivationProjectionState.Active, await host.Schedules.FindActivationStateAsync("active"));
    }

    [Fact]
    public async Task A_changed_physical_model_incarnation_refuses_before_source_or_slot_write()
    {
        await using var host = await Harness.OpenAsync();
        var material = await host.ModelAsync("model");
        var stale = new RuntimeExecutionMaterial(material.Model!, "different-incarnation");

        await Assert.ThrowsAsync<ArgumentException>(() => host.Coordinator().ActivateMaterialAsync(host.Command(stale, "stale")).AsTask());

        Assert.Null(await host.Authority.FindAsync("logical-definition", "default"));
        Assert.Null(await host.References.FindAsync(WorkflowActivationReferenceIdentity.Create("stale")));
    }

    [Fact]
    public async Task A_custom_native_only_coordinator_refuses_material_activation_without_native_fallback()
    {
        await using var host = await Harness.OpenAsync();
        var command = host.Command(await host.ModelAsync("model"), "unsupported");
        IWorkflowActivationCoordinator legacy = new NativeOnlyCoordinator();

        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.ActivateMaterialAsync(command).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => legacy.DeactivateMaterialAsync(
            new(command.WorkflowDefinitionId, command.Material, command.SlotName, command.Source, 0)).AsTask());

        Assert.Null(await host.Authority.FindAsync("logical-definition", "default"));
        Assert.Null(await host.References.FindAsync(WorkflowActivationReferenceIdentity.Create("unsupported")));
    }

    private sealed class NativeOnlyCoordinator : IWorkflowActivationCoordinator
    {
        public ValueTask<WorkflowActivationResult> ActivateAsync(WorkflowActivationCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Model activation must not fabricate a native root.");
        public ValueTask<WorkflowActivationResult> DeactivateAsync(WorkflowDeactivationCommand command, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Model deactivation must not fabricate a native root.");
        public ValueTask<WorkflowActivationResult> EnsureServingAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unsupported material activation must not check a slot.");
    }

    private sealed class ThrowOnceObserver : IWorkflowTriggerIndexObserver
    {
        private bool _throw = true;
        public ValueTask OnTriggersIndexedAsync(WorkflowTriggerIndexSnapshot snapshot, CancellationToken cancellationToken = default)
        {
            if (!_throw) return ValueTask.CompletedTask;
            _throw = false;
            throw new InvalidOperationException("Observer unavailable.");
        }
    }

    private sealed class Harness : IAsyncDisposable
    {
        private readonly TemporarySqliteDatabase _database;
        private readonly RuntimeSqliteDbContext _context;
        private readonly TimeProvider _clock = new FakeTimeProvider(new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero));
        public RuntimeSqliteDbContext Context => _context;
        public TimeProvider Clock => _clock;
        public IWorkflowExecutableStore Materials { get; }
        public IWorkflowExecutableSourceReferenceStore References { get; }
        public IWorkflowActivationAuthority Authority { get; }
        public IWorkflowTriggerBindingStore Bindings { get; }
        public IRecurringTriggerScheduleStore Schedules { get; }
        private readonly IWorkflowActivationSwitch _switch;

        private Harness(TemporarySqliteDatabase database, RuntimeSqliteDbContext context, PersistenceScope? physical)
        {
            _database = database; _context = context;
            IPersistenceAccessContextAccessor access = physical is null ? new AdmissionAccess() : new ScopedAccess(physical);
            var codec = new AdmissionCodec();
            Materials = new EfWorkflowExecutableStore(context, access);
            References = new EfWorkflowExecutableSourceReferenceStore(context, access, codec, _clock);
            Authority = new EfWorkflowActivationAuthority(context, access);
            Bindings = new EfWorkflowTriggerBindingStore(context, access);
            Schedules = new EfRecurringTriggerScheduleStore(context, access, codec);
            _switch = new EfWorkflowActivationSwitch(Authority, Bindings, References, access, _clock, Schedules);
        }

        public static async Task<Harness> OpenAsync(PersistenceScope? physical = null)
        {
            var database = new TemporarySqliteDatabase("material-activation");
            var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.ConnectionString).Options);
            await context.Database.EnsureCreatedAsync();
            return new(database, context, physical);
        }

        public WorkflowActivationCoordinator Coordinator(IWorkflowTriggerIndexObserver? observer = null, bool exclusiveRoutes = false) => new(
            Materials, Authority, _switch, References,
            new WorkflowExecutableRootWriteLeaseManager(Materials, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), _clock),
            _clock, new NativeIndexer(Bindings, Schedules, exclusiveRoutes), Bindings, Schedules,
            observer is null ? [] : [observer]);

        public async Task<RuntimeExecutionMaterial> ModelAsync(string behavior)
        {
            var definition = JsonSerializer.SerializeToElement(new { behavior });
            var seed = new WorkflowExecutableIdentity("pending", "content-producer", "producer-version", "1", "pending");
            var draft = new RuntimeModelMaterial(1, seed, new("test.model", 1), "test-model-v1", definition, new(1, []), [], [], []);
            var hash = draft.ComputeContentHash();
            var model = new RuntimeModelMaterial(1, seed with { ArtifactId = hash, ArtifactHash = hash }, draft.Protocol,
                draft.Profile, definition, draft.InputContract, [], [], []);
            await Materials.SaveModelAsync(model);
            return (await Materials.FindMaterialAsync(hash))!;
        }

        public async Task<RuntimeExecutionMaterial> NativeAsync(string artifactId)
        {
            var executable = new WorkflowExecutable(new(artifactId, "logical-definition", "native-version", "1", "sha256:" + artifactId),
                new ExecutableNode("node", "node", "test/activity", "1", "test", JsonSerializer.SerializeToElement(new { }),
                    new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
                new Dictionary<string, WorkflowExecutableResumeTarget>(), _clock.GetUtcNow(), new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
            await Materials.SaveAsync(executable);
            return (await Materials.FindMaterialAsync(artifactId))!;
        }

        public WorkflowExecutableSourceReference Reference(RuntimeExecutionMaterial material) => new("candidate", material.Identity.ArtifactId,
            "authored-model", "authored-version", "1", "logical-definition", "authored-version", "1", _clock.GetUtcNow(),
            _clock.GetUtcNow(), WorkflowExecutableReferenceScope.Published);

        public WorkflowMaterialActivationCommand Command(RuntimeExecutionMaterial material, string activation, long revision = 0) =>
            new("logical-definition", material, Reference(material), "default", activation, WorkflowActivationSource.Publishing, revision);

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    private sealed class ScopedAccess(PersistenceScope scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(scope); }

    private sealed class NativeIndexer(IWorkflowTriggerBindingStore bindings, IRecurringTriggerScheduleStore schedules, bool exclusiveRoutes = false) : IWorkflowTriggerIndexer
    {
        public ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> IndexAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async ValueTask<IReadOnlyCollection<WorkflowTriggerBinding>> PrepareActivationAsync(WorkflowExecutable executable,
            string activationId, string slotId, CancellationToken cancellationToken = default)
        {
            var hash = exclusiveRoutes ? executable.Identity.ArtifactId : "event";
            var binding = new WorkflowTriggerBinding(WorkflowTriggerBinding.BuildId(activationId, executable.Identity.ArtifactId, "node", hash),
                executable.Identity.ArtifactId, executable.Identity.DefinitionId, executable.Identity.ArtifactVersion, executable.Identity.ArtifactHash,
                "node", exclusiveRoutes ? "Http" : "Event", hash, null, new Dictionary<string, string>(), DateTimeOffset.UtcNow, activationId, slotId,
                Cardinality: exclusiveRoutes ? TriggerCardinality.Exclusive : TriggerCardinality.FanOut);
            await bindings.PrepareActivationAsync(activationId, [binding], cancellationToken);
            await schedules.PrepareActivationAsync(activationId, [], cancellationToken);
            return [binding];
        }
    }
}
