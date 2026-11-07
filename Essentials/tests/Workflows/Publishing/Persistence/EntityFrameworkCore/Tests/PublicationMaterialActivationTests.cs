using System.Text.Json;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Publishing.Api.Handlers;
using Elsa.Workflows.Publishing.Api.Requests;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Publishing.Persistence.EntityFrameworkCore.Tests;

public sealed class PublicationMaterialActivationTests
{
    [Fact]
    public async Task Model_publication_and_same_artifact_no_op_journal_the_actual_serving_logical_source()
    {
        await using var host = await Harness.OpenAsync();
        var material = await host.ModelAsync("shared-behavior");
        var first = await host.Activator().ActivateMaterialAsync(host.Request(material, "publication-1", "version-1"));
        var noOp = await host.Activator().ActivateMaterialAsync(host.Request(material, "publication-2", "version-2", first.Slot.Revision));

        Assert.True(first.Succeeded);
        Assert.Equal(PublicationStatus.Active, first.Publication.Status);
        Assert.Equal("logical-definition", first.Publication.WorkflowDefinitionId);
        Assert.NotEqual(material.Identity.DefinitionId, first.Publication.WorkflowDefinitionId);
        Assert.True(noOp.Succeeded);
        Assert.Equal(first.Publication, noOp.Publication);
        Assert.Equal("version-1", noOp.Publication.WorkflowDefinitionVersionId);
        Assert.Equal(PublicationStatus.Failed, (await host.Publications.FindAsync("publication-2"))!.Status);
        Assert.Equal(PublicationFailureCodes.ArtifactAlreadyServing, (await host.Publications.FindAsync("publication-2"))!.Failure!.Code);
        Assert.Null(await host.References.FindAsync(WorkflowActivationReferenceIdentity.Create("publication-2")));
        Assert.Equal("version-1", (await host.References.FindAsync(first.Publication.SourceReferenceId!))!.DefinitionVersionId);
        Assert.Null(await host.Materials.FindAsync(material.Identity.ArtifactId));
    }

    [Fact]
    public async Task Model_replacement_and_observer_compensation_keep_the_journal_with_runtime_authority()
    {
        await using var host = await Harness.OpenAsync();
        var first = await host.Activator().ActivateMaterialAsync(host.Request(await host.ModelAsync("first"), "publication-1", "version-1"));
        var failed = await host.Activator(new ThrowOnceObserver()).ActivateMaterialAsync(
            host.Request(await host.ModelAsync("second"), "publication-failed", "version-2", first.Slot.Revision));

        Assert.False(failed.Succeeded);
        Assert.Equal(PublicationStatus.Failed, failed.Publication.Status);
        Assert.Equal("publication-1", failed.Slot.ActiveActivationId);
        Assert.Equal(PublicationStatus.Active, (await host.Publications.FindAsync("publication-1"))!.Status);
        Assert.Null((await host.References.FindAsync(first.Publication.SourceReferenceId!))!.DeletedAt);

        var replaced = await host.Activator().ActivateMaterialAsync(
            host.Request(await host.ModelAsync("third"), "publication-3", "version-3", failed.Slot.Revision));
        Assert.True(replaced.Succeeded);
        Assert.Equal("publication-1", replaced.ReplacedPublicationId);
        Assert.Equal(PublicationStatus.Retired, (await host.Publications.FindAsync("publication-1"))!.Status);
        Assert.Equal(PublicationStatus.Active, replaced.Publication.Status);
        Assert.NotNull((await host.References.FindAsync(first.Publication.SourceReferenceId!))!.DeletedAt);
    }

    [Fact]
    public async Task Material_candidate_with_different_source_identity_is_rejected_before_journal_or_runtime_mutation()
    {
        await using var host = await Harness.OpenAsync();
        var request = host.Request(await host.ModelAsync("model"), "publication-invalid", "version-1");
        request = request with { Reference = request.Reference with { DefinitionId = "other-definition" } };

        await Assert.ThrowsAsync<ArgumentException>(() => host.Activator().ActivateMaterialAsync(request).AsTask());

        Assert.Null(await host.Publications.FindAsync("publication-invalid"));
        Assert.Null(await host.Authority.FindAsync("logical-definition", "default"));
        Assert.Null(await host.References.FindAsync(WorkflowActivationReferenceIdentity.Create("publication-invalid")));
    }

    [Fact]
    public async Task Custom_native_only_activators_refuse_material_publication_without_calling_the_native_operation()
    {
        await using var host = await Harness.OpenAsync();
        var activator = new NativeOnlyActivator();
        IPublicationActivator contract = activator;
        var request = host.Request(await host.ModelAsync("model"), "publication", "version");

        await Assert.ThrowsAsync<NotSupportedException>(() => contract.ActivateMaterialAsync(request).AsTask());

        Assert.False(activator.NativeCalled);
        Assert.Null(await host.Publications.FindAsync("publication"));
    }

    [Fact]
    public async Task Ordinary_slot_management_unpublishes_and_restores_the_actual_model_publication_and_logical_source()
    {
        await using var host = await Harness.OpenAsync();
        var material = await host.ModelAsync("shared-model");
        var original = await host.Activator().ActivateMaterialAsync(host.Request(material, "publication-original", "version-original"));
        var noOp = await host.Activator().ActivateMaterialAsync(host.Request(material, "publication-unused", "version-unused", original.Slot.Revision));
        Assert.Equal(original.Publication, noOp.Publication);

        var inactive = await host.Unpublisher().Handle(new("logical-definition", "default"), default);
        Assert.Null(inactive.ActiveActivationId);
        Assert.Equal(original.Slot.Revision + 1, inactive.Revision);
        Assert.Equal(PublicationStatus.Retired, (await host.Publications.FindAsync("publication-original"))!.Status);
        var retired = (await host.References.FindAsync(original.Publication.SourceReferenceId!))!;
        Assert.Equal("publication-unpublished", retired.DeletedReason);
        var restored = await host.Restorer().Handle(new("logical-definition", "default"), default);
        Assert.Equal(inactive.Revision + 1, restored.Revision);
        Assert.NotEqual(original.Publication.PublicationId, restored.ActiveActivationId);
        var publication = (await host.Publications.FindAsync(restored.ActiveActivationId!))!;
        var reference = (await host.References.FindAsync(publication.SourceReferenceId!))!;
        Assert.Equal(PublicationStatus.Active, publication.Status);
        Assert.Equal("logical-definition", reference.DefinitionId);
        Assert.Equal("version-original", reference.DefinitionVersionId);
        Assert.Equal(retired.SourceId, reference.SourceId);
        Assert.Equal(retired.SourceVersion, reference.SourceVersion);
        Assert.Equal(material.Identity.ArtifactId, reference.ArtifactId);
        Assert.Equal(WorkflowActivationReferenceIdentity.Create(publication.PublicationId), reference.SourceReferenceId);
        Assert.Equal(publication.PublicationId, reference.ActivationId);
        Assert.Null(reference.DeletedAt);
        Assert.Null(reference.DeletedReason);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(retired, (await host.References.FindAsync(retired.SourceReferenceId))!));
        Assert.Null(await host.Materials.FindAsync(material.Identity.ArtifactId));
        Assert.Equal(material.IncarnationId, (await host.Materials.FindMaterialAsync(material.Identity.ArtifactId))!.IncarnationId);
    }

    [Fact]
    public async Task Model_unpublish_cannot_retire_the_publication_that_replaced_its_observed_revision()
    {
        await using var host = await Harness.OpenAsync();
        var original = await host.Activator().ActivateMaterialAsync(host.Request(await host.ModelAsync("original"), "publication-original", "version-original"));
        var replacement = host.Request(await host.ModelAsync("replacement"), "publication-replacement", "version-replacement", original.Slot.Revision);
        var raced = new BeforeDeactivation(host.Coordinator(), async () => Assert.True((await host.Activator().ActivateMaterialAsync(replacement)).Succeeded));
        var handler = new UnpublishPublicationSlotRequestHandler(host.Authority, raced, host.Publications, host.Materials, host.References, TimeProvider.System);

        var refusal = await Assert.ThrowsAsync<PublicationActivationException>(() => handler.Handle(new("logical-definition", "default"), default));

        Assert.Equal(PublicationFailureCodes.SlotRevisionConflict, refusal.Code);
        Assert.Equal("publication-replacement", (await host.Authority.FindAsync("logical-definition", "default"))!.ActiveActivationId);
        Assert.Equal(PublicationStatus.Active, (await host.Publications.FindAsync("publication-replacement"))!.Status);
        Assert.Null((await host.References.FindAsync(replacement.Candidate.SourceReferenceId!))!.DeletedAt);
        Assert.NotEqual("publication-unpublished", (await host.References.FindAsync(original.Publication.SourceReferenceId!))!.DeletedReason);
    }

    [Fact]
    public async Task Model_restore_same_artifact_race_returns_the_actual_serving_source_without_minting_its_unused_clone()
    {
        await using var host = await Harness.OpenAsync();
        var material = await host.ModelAsync("same-model");
        var original = await host.Activator().ActivateMaterialAsync(host.Request(material, "publication-original", "version-original"));
        var inactive = await host.Unpublisher().Handle(new("logical-definition", "default"), default);
        var replacement = host.Request(material, "publication-race", "version-race", inactive.Revision);
        var raced = new BeforeMaterialActivation(host.Activator(), async () => Assert.True((await host.Activator().ActivateMaterialAsync(replacement)).Succeeded));

        var restored = await host.Restorer(raced).Handle(new("logical-definition", "default"), default);

        Assert.Equal("publication-race", restored.ActiveActivationId);
        Assert.Equal(inactive.Revision + 1, restored.Revision);
        var unused = Assert.Single(await host.Publications.ListBySlotAsync(restored.SlotId),
            item => item.PublicationId is not ("publication-original" or "publication-race"));
        Assert.Equal(PublicationStatus.Failed, unused.Status);
        Assert.Equal(PublicationFailureCodes.ArtifactAlreadyServing, unused.Failure!.Code);
        Assert.Null(await host.References.FindAsync(unused.SourceReferenceId!));
        Assert.Equal("version-race", (await host.References.FindAsync(replacement.Candidate.SourceReferenceId!))!.DefinitionVersionId);
        Assert.Equal(PublicationStatus.Retired, (await host.Publications.FindAsync(original.Publication.PublicationId))!.Status);
    }

    private sealed class BeforeDeactivation(IWorkflowActivationCoordinator inner, Func<Task> before) : IWorkflowActivationCoordinator
    {
        public ValueTask<WorkflowActivationResult> ActivateAsync(WorkflowActivationCommand command, CancellationToken cancellationToken = default) => inner.ActivateAsync(command, cancellationToken);
        public ValueTask<WorkflowActivationResult> DeactivateAsync(WorkflowDeactivationCommand command, CancellationToken cancellationToken = default) => inner.DeactivateAsync(command, cancellationToken);
        public ValueTask<WorkflowActivationResult> EnsureServingAsync(string definition, string slot, CancellationToken cancellationToken = default) => inner.EnsureServingAsync(definition, slot, cancellationToken);
        public async ValueTask<WorkflowActivationResult> DeactivateMaterialAsync(WorkflowMaterialDeactivationCommand command, CancellationToken cancellationToken = default)
        { await before(); return await inner.DeactivateMaterialAsync(command, cancellationToken); }
    }

    private sealed class BeforeMaterialActivation(IPublicationActivator inner, Func<Task> before) : IPublicationActivator
    {
        public ValueTask<PublicationActivationResult> ActivateAsync(PublicationActivationRequest request, CancellationToken cancellationToken = default) => inner.ActivateAsync(request, cancellationToken);
        public ValueTask<PublicationCompletionResult> CompleteAsync(string definition, string slot, CancellationToken cancellationToken = default) => inner.CompleteAsync(definition, slot, cancellationToken);
        public async ValueTask<PublicationActivationResult> ActivateMaterialAsync(PublicationMaterialActivationRequest request, CancellationToken cancellationToken = default)
        { await before(); return await inner.ActivateMaterialAsync(request, cancellationToken); }
    }

    private sealed class NativeOnlyActivator : IPublicationActivator
    {
        public bool NativeCalled { get; private set; }
        public ValueTask<PublicationActivationResult> ActivateAsync(PublicationActivationRequest request, CancellationToken cancellationToken = default)
        {
            NativeCalled = true;
            throw new InvalidOperationException("Material publication must not fabricate a native request.");
        }
        public ValueTask<PublicationCompletionResult> CompleteAsync(string workflowDefinitionId, string slotName, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Unsupported material publication must not complete a slot.");
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
        private readonly SqliteTestDatabase _database;
        private readonly RuntimeSqliteDbContext _context;
        private readonly IWorkflowActivationSwitch _switch;
        private readonly IWorkflowTriggerBindingStore _bindings;
        private readonly IRecurringTriggerScheduleStore _schedules;
        public IWorkflowExecutableStore Materials { get; }
        public IWorkflowExecutableSourceReferenceStore References { get; }
        public IWorkflowActivationAuthority Authority { get; }
        public InMemoryPublicationRecordStore Publications { get; } = new();

        private Harness(SqliteTestDatabase database, RuntimeSqliteDbContext context)
        {
            _database = database; _context = context;
            var access = new Access();
            var codec = new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions
            {
                SigningKey = "material-publication-test-signing-key-32-bytes",
                AllowEphemeralDevelopmentKey = false
            }));
            Materials = new EfWorkflowExecutableStore(context, access);
            References = new EfWorkflowExecutableSourceReferenceStore(context, access, codec);
            Authority = new EfWorkflowActivationAuthority(context, access);
            _bindings = new EfWorkflowTriggerBindingStore(context, access);
            _schedules = new EfRecurringTriggerScheduleStore(context, access, codec);
            _switch = new EfWorkflowActivationSwitch(Authority, _bindings, References, access, TimeProvider.System, _schedules);
        }

        public static async Task<Harness> OpenAsync()
        {
            var database = await SqliteTestDatabase.CreateAsync<RuntimeSqliteDbContext>(options => new(options));
            return new(database, new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(database.ConnectionString).Options));
        }

        public WorkflowActivationCoordinator Coordinator(IWorkflowTriggerIndexObserver? observer = null) =>
            new(Materials, Authority, _switch, References,
                new WorkflowExecutableRootWriteLeaseManager(Materials, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), TimeProvider.System),
                TimeProvider.System, triggerBindingStore: _bindings, recurringScheduleStore: _schedules,
                triggerObservers: observer is null ? [] : [observer]);
        public PublicationActivator Activator(IWorkflowTriggerIndexObserver? observer = null) => new(
            Coordinator(observer), Publications, Authority, References, TimeProvider.System);
        public UnpublishPublicationSlotRequestHandler Unpublisher() => new(Authority, Coordinator(), Publications, Materials, References, TimeProvider.System);
        public RestorePublicationSlotRequestHandler Restorer(IPublicationActivator? activator = null) => new(Authority, Publications,
            activator ?? Activator(), Materials, References, TimeProvider.System);

        public async Task<RuntimeExecutionMaterial> ModelAsync(string behavior)
        {
            var definition = JsonSerializer.SerializeToElement(new { behavior });
            var seed = new WorkflowExecutableIdentity("pending", "material-producer", "material-version", "1", "pending");
            var draft = new RuntimeModelMaterial(1, seed, new("test.model", 1), "test-model-v1", definition, new(1, []), [], [], []);
            var hash = draft.ComputeContentHash();
            await Materials.SaveModelAsync(new(1, seed with { ArtifactId = hash, ArtifactHash = hash }, draft.Protocol, draft.Profile,
                definition, draft.InputContract, [], [], []));
            return (await Materials.FindMaterialAsync(hash))!;
        }

        public PublicationMaterialActivationRequest Request(RuntimeExecutionMaterial material, string publication, string version, long revision = 0)
        {
            var now = DateTimeOffset.UtcNow;
            var sourceId = WorkflowActivationReferenceIdentity.Create(publication);
            return new(new(publication, WorkflowActivationSlotIdentity.Create("logical-definition", "default"), "logical-definition", version,
                    material.Identity.ArtifactId, sourceId, revision, PublicationStatus.Candidate, now, null, null, null), material,
                new(sourceId, material.Identity.ArtifactId, "authored-model", version, "1", "logical-definition", version, "1", now, now,
                    WorkflowExecutableReferenceScope.Published));
        }

        public async ValueTask DisposeAsync()
        {
            await _context.DisposeAsync();
            await _database.DisposeAsync();
        }
    }

    private sealed class Access : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new("default"));
    }
}
