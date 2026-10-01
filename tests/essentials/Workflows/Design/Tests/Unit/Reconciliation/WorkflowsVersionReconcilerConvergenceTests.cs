using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Reconciliation.Handlers;
using Elsa.Workflows.Design.Reconciliation.Models;
using Elsa.Workflows.Design.Reconciliation.Options;
using Elsa.Workflows.Design.Reconciliation.Services;
using Elsa.Workflows.Design.Tests.Infrastructure;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Tests.Unit.Reconciliation;

/// <summary>
/// Drives <see cref="WorkflowsVersionReconciler"/> through the real EF design commands and
/// <c>EfDesignAtomicWriter</c> on SQLite, so every pass meets the operation markers earlier passes left
/// behind (#2187). <see cref="WorkflowsVersionReconcilerTests"/> uses spy commands that never write one.
/// Each pass offers the same version, so only the definition's metadata changes between passes.
/// </summary>
public sealed class WorkflowsVersionReconcilerConvergenceTests : IAsyncLifetime
{
    private const string DefinitionId = "wf-converge";
    private const string SiblingId = "wf-converge-sibling";
    private const string Version = "1.0.0";
    private static readonly string SortKey = SemVer.ToSortKey(Version);
    private static readonly WorkflowDefinitionState EmptyState = new([], null, [], [], null);
    private static readonly WorkflowDefinitionState OtherState = new([new VariableDefinition("counter", "Counter", new TypeReference("Int32"), null, null)], null, [], [], null);
    private WorkflowsDesignTestHost _host = null!;

    public async Task InitializeAsync() => _host = await WorkflowsDesignTestHost.CreateAsync();

    public Task DisposeAsync()
    {
        _host.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Rename_rename_and_rename_back_converge_on_each_name()
    {
        // The last name is one an earlier metadata write produced, so a key derived from the desired
        // metadata alone would replay that write's marker and leave "Renamed again" in place.
        foreach (var name in new[] { "Original", "Renamed", "Renamed again", "Renamed" })
        {
            await ReconcileAsync(name: name);
            Assert.Equal(name, (await GetDefinitionAsync())!.Name);
        }
    }

    [Fact]
    public async Task Delete_and_undelete_converge_on_each_state_however_often_they_repeat()
    {
        await ReconcileAsync();

        foreach (var deleted in new[] { true, false, true, false })
        {
            await ReconcileAsync(deleted: deleted);
            Assert.Equal(deleted, (await GetDefinitionAsync())!.DeletedAt is not null);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // The source still says deleted: the sources stamp a fresh DeletedAt on every read.
    public async Task Repeating_a_pass_whose_change_already_landed_writes_nothing(bool deleted)
    {
        await ReconcileAsync(name: "Original");
        await ReconcileAsync(name: "Renamed", deleted: deleted);
        var settled = await GetDefinitionAsync();
        var markers = await ListMarkersAsync();

        await ReconcileAsync(name: "Renamed", deleted: deleted);

        var current = await GetDefinitionAsync();
        Assert.Equal(settled!.LastModifiedAt, current!.LastModifiedAt);
        Assert.Equal(settled.DeletedAt, current.DeletedAt);
        Assert.Equal(markers, await ListMarkersAsync());
    }

    [Theory]
    [InlineData("Renamed")] // Two nodes apply the same change.
    [InlineData("Renamed elsewhere")] // Another writer applies a different one, so the paused pass has to write again.
    public async Task A_metadata_write_that_loses_a_race_converges(string otherWritersName)
    {
        await ReconcileAsync(name: "Original");
        var read = new Pause();
        var paused = ReconcileAsync(name: "Renamed", definitions: store => new PausingDefinitionStore(store, read));
        await read.ReachedBy(paused);

        // The other writer commits between the paused pass's read and its write, which then fails its
        // LastModifiedAt check, and its own key has no marker to replay.
        await ReconcileAsync(name: otherWritersName);
        read.Resume();
        await paused;

        Assert.Equal("Renamed", (await GetDefinitionAsync())!.Name);
    }

    [Fact]
    public async Task A_metadata_write_that_lost_its_race_to_a_permanent_delete_fails_with_that_lost_race()
    {
        await ReconcileAsync(name: "Original");
        await ReconcileAsync(name: "Original", deleted: true);
        var read = new Pause();
        var reread = new Pause();
        var failures = new List<DesignPersistenceException>();
        var paused = ReconcileAsync(
            name: "Renamed",
            deleted: true,
            definitions: store => new PausingDefinitionStore(store, read, reread),
            saves: save => new FailureRecordingSaveCommand(save, failures));
        await read.ReachedBy(paused);

        // Another pass commits a rename, so the paused write loses its race, and the definition is deleted before
        // the paused pass reads it again: nothing is left to compare against.
        await ReconcileAsync(name: "Renamed elsewhere", deleted: true);
        read.Resume();
        await reread.ReachedBy(paused);
        await DeletePermanentlyAsync();
        reread.Resume();

        var thrown = await Assert.ThrowsAsync<DesignPersistenceException>(() => paused);
        Assert.Same(Assert.Single(failures), thrown);
        Assert.Equal(DesignPersistenceFailureKind.Concurrency, thrown.FailureKind);
        Assert.Null(await GetDefinitionAsync());
    }

    [Fact]
    public async Task A_marker_written_under_the_per_version_metadata_key_does_not_block_a_later_rename()
    {
        await ReconcileAsync(name: "Original");
        // What a database written before #2187 holds after one rename at this version.
        await RenameAsync(LegacyMetadataKey(DefinitionId), "Renamed");

        await ReconcileAsync(name: "Renamed again");

        Assert.Equal("Renamed again", (await GetDefinitionAsync())!.Name);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_permanently_deleted_definition_the_source_still_lists_is_imported_again(bool sameVersionId)
    {
        // Same version id: every materialization request matches its first marker exactly, so a surviving
        // marker replays without writing a row and the pass reports success. A fresh id, which is what
        // the git and JSON sources produce, conflicts with the surviving version marker instead.
        await ReconcileAsync(name: "Original", versionId: "first-import");
        await ReconcileAsync(name: "Original", deleted: true);
        await DeletePermanentlyAsync();

        var reimportedVersionId = sameVersionId ? "first-import" : "second-import";
        await ReconcileAsync(name: "Original", versionId: reimportedVersionId);

        var definition = await GetDefinitionAsync();
        Assert.NotNull(definition);
        Assert.Equal("Original", definition.Name);
        Assert.Null(definition.DeletedAt);
        Assert.Equal(reimportedVersionId, (await GetLatestVersionAsync())?.Id);
    }

    [Fact]
    public async Task A_permanent_delete_retires_every_reconciliation_marker_of_that_definition_and_no_others()
    {
        // Both definitions get every kind of marker: materialization, a pre-#2187 metadata write, and a current one.
        foreach (var definitionId in new[] { DefinitionId, SiblingId })
        {
            await ReconcileAsync(name: "Original", definitionId: definitionId);
            await RenameAsync(LegacyMetadataKey(definitionId), "Renamed", definitionId);
            await ReconcileAsync(name: "Original", deleted: true, definitionId: definitionId);
        }
        var before = await ListMarkersAsync();

        await DeletePermanentlyAsync();

        var after = await ListMarkersAsync();
        Assert.Collection(
            before.Except(after),
            marker => Assert.Equal(Marker(EfMaterializeWorkflowDefinitionCommand.OperationKind, WorkflowReconciliationOperationKeys.Definition(DefinitionId)), marker),
            marker => Assert.StartsWith($"{EfSaveWorkflowDefinitionCommand.OperationKind} {WorkflowReconciliationOperationKeys.DefinitionMetadataWritePrefix(DefinitionId)}", marker),
            marker => Assert.Equal(Marker(EfSaveWorkflowDefinitionCommand.OperationKind, LegacyMetadataKey(DefinitionId)), marker),
            marker => Assert.Equal(Marker(EfMaterializeWorkflowDefinitionVersionCommand.OperationKind, WorkflowReconciliationOperationKeys.Version(DefinitionId, SortKey)), marker));
        var siblingMarkers = before.Where(marker => marker.Contains($":{SiblingId.Length}:{SiblingId}", StringComparison.Ordinal)).ToArray();
        Assert.Equal(4, siblingMarkers.Length);
        Assert.All(siblingMarkers, marker => Assert.Contains(marker, after));
        Assert.StartsWith($"{EfDeleteWorkflowDefinitionPermanentlyCommand.OperationKind} ", Assert.Single(after.Except(before)));
    }

    [Fact]
    public async Task A_version_materialized_under_a_generated_id_keeps_it_when_its_source_is_reconciled_again()
    {
        // What a database written before #2189 holds: the version under an id one node generated. The source now
        // contributes it under its derived id, and the pass must find the stored version rather than add a second.
        await ReconcileAsync(versionId: "generated-before-2189");
        var markers = await ListMarkersAsync();

        await ReconcileAsync(contributions: FromSource);

        Assert.Equal("generated-before-2189", Assert.Single(await ListVersionIdsAsync()));
        Assert.Equal(markers, await ListMarkersAsync());
    }

    [Fact]
    public async Task Two_passes_that_bring_different_content_for_one_new_version_still_conflict()
    {
        // Every node derives the same version id now, so only the content in the request tells two writes apart. The
        // pass that commits second must fail rather than report the other pass's version as its own.
        await _host.EnsureDefinition(DefinitionId, "Original");
        var versionId = WorkflowReconciliationVersionIds.For(DefinitionId, SortKey);
        var emptyCheck = new Pause();
        var otherCheck = new Pause();
        var empty = ReconcileAsync(versionId: versionId, versions: store => new PausingVersionStore(store, emptyCheck));
        var other = ReconcileAsync(versionId: versionId, state: OtherState, versions: store => new PausingVersionStore(store, otherCheck));
        await Pause.ReleaseTogether((empty, emptyCheck), (other, otherCheck));

        var failures = await Task.WhenAll(FailureOf(empty), FailureOf(other));

        var conflict = Assert.IsType<InvalidOperationException>(Assert.Single(failures, failure => failure is not null));
        Assert.Contains($"'{EfMaterializeWorkflowDefinitionVersionCommand.OperationKind}/", conflict.Message);
        Assert.Contains("conflicts with an earlier request", conflict.Message);
        var otherCommitted = failures[1] is null;
        var stored = await GetLatestVersionAsync();
        Assert.Equal(versionId, stored!.Id);
        Assert.Equal(otherCommitted, stored.State.Variables.Any());
    }

    private Task ReconcileAsync(
        string name = "Original",
        bool deleted = false,
        string? versionId = null,
        string definitionId = DefinitionId,
        WorkflowDefinitionState? state = null,
        Func<IWorkflowDefinitionStore, IWorkflowDefinitionStore>? definitions = null,
        Func<IWorkflowDefinitionVersionStore, IWorkflowDefinitionVersionStore>? versions = null,
        Func<ISaveWorkflowDefinitionCommand, ISaveWorkflowDefinitionCommand>? saves = null,
        Func<IServiceProvider, IInlineEventPublisher>? contributions = null) => InScopeAsync(services =>
    {
        var definition = services.GetRequiredService<IWorkflowDefinitionFactory>().Create(name, id: definitionId, deleted: deleted);
        var version = services.GetRequiredService<IWorkflowDefinitionVersionFactory>().Create(definition, Version, state ?? EmptyState, id: versionId);
        var definitionStore = services.GetRequiredService<IWorkflowDefinitionStore>();
        var versionStore = services.GetRequiredService<IWorkflowDefinitionVersionStore>();
        var saveCommand = services.GetRequiredService<ISaveWorkflowDefinitionCommand>();
        var reconciler = ActivatorUtilities.CreateInstance<WorkflowsVersionReconciler>(
            services,
            contributions?.Invoke(services) ?? new ContributingPublisher(version),
            Microsoft.Extensions.Options.Options.Create(new WorkflowVersionReconcilerOptions()),
            definitions?.Invoke(definitionStore) ?? definitionStore,
            versions?.Invoke(versionStore) ?? versionStore,
            saves?.Invoke(saveCommand) ?? saveCommand);
        return reconciler.Reconcile(CancellationToken.None);
    });

    /// <summary>Contributes the definition's one version through the real aggregating handler, as a source read does.</summary>
    private static IInlineEventPublisher FromSource(IServiceProvider services) =>
        new HandlingPublisher<WorkflowVersionsReconciling>(new WorkflowVersionsReconcilingHandler(
            services.GetRequiredService<IWorkflowDefinitionFactory>(),
            services.GetRequiredService<IWorkflowDefinitionVersionFactory>(),
            [new StaticWorkflowSource(new WorkflowVersionReconciliationModel(DefinitionId, "Original", null, Version, EmptyState))]));

    private static async Task<Exception?> FailureOf(Task pass)
    {
        try
        {
            await pass;
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }

    private Task RenameAsync(DesignOperationKey key, string name, string definitionId = DefinitionId) => InScopeAsync(async services =>
    {
        var definition = (await services.GetRequiredService<IWorkflowDefinitionStore>().FindByIdAsync(definitionId))!;
        definition.Name = name;
        await services.GetRequiredService<ISaveWorkflowDefinitionCommand>().Execute(key, definition);
    });

    private Task DeletePermanentlyAsync() => InScopeAsync(services =>
        new EfDeleteWorkflowDefinitionPermanentlyCommand(
                services.GetRequiredService<WorkflowsDesignDbContext>(),
                services.GetRequiredService<IPersistenceAccessContextAccessor>(),
                services.GetRequiredService<IDesignAtomicWriter>(),
                [new NeverPublishedGuard()])
            .Execute(new DesignOperationKey($"permanent-delete-{Guid.NewGuid():N}"), DefinitionId));

    private Task<WorkflowDefinition?> GetDefinitionAsync() =>
        InScopeAsync(services => services.GetRequiredService<IWorkflowDefinitionStore>().FindByIdAsync(DefinitionId));

    private Task<WorkflowDefinitionVersion?> GetLatestVersionAsync() =>
        InScopeAsync(services => services.GetRequiredService<IWorkflowDefinitionVersionStore>().FindLatestVersionAsync(DefinitionId));

    private Task<string[]> ListVersionIdsAsync() => InScopeAsync(async services =>
        (await services.GetRequiredService<IWorkflowDefinitionVersionStore>().ListByDefinitionAsync(DefinitionId)).Select(version => version.Id).ToArray());

    private Task<string[]> ListMarkersAsync() => InScopeAsync(async services =>
    {
        var markers = await services.GetRequiredService<WorkflowsDesignDbContext>().Operations
            .AsNoTracking()
            .Select(marker => marker.OperationKind + " " + marker.OperationKey)
            .ToListAsync();
        return markers.Order(StringComparer.Ordinal).ToArray();
    });

    /// <summary>Runs one unit of work in its own DI scope, as each reconciliation pass and API request gets one.</summary>
    private async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> work)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await work(scope.ServiceProvider);
    }

    private Task InScopeAsync(Func<IServiceProvider, Task> work) => InScopeAsync(async services =>
    {
        await work(services);
        return true;
    });

    /// <summary>The key metadata writes used before #2187, spelled out to pin what existing databases hold.</summary>
    private static DesignOperationKey LegacyMetadataKey(string definitionId) =>
        new($"workflow-reconciliation:definition-metadata:{definitionId.Length}:{definitionId}{SortKey.Length}:{SortKey}");

    private static string Marker(string operationKind, DesignOperationKey key) => operationKind + " " + key.Value;

    /// <summary>Records each persistence failure the real save command throws, then lets it propagate.</summary>
    private sealed class FailureRecordingSaveCommand(ISaveWorkflowDefinitionCommand inner, List<DesignPersistenceException> failures) : ISaveWorkflowDefinitionCommand
    {
        public async Task Execute(DesignOperationKey operationKey, WorkflowDefinition definition, CancellationToken cancellationToken = default)
        {
            try
            {
                await inner.Execute(operationKey, definition, cancellationToken);
            }
            catch (DesignPersistenceException failure)
            {
                failures.Add(failure);
                throw;
            }
        }
    }

    /// <summary>Stands in for the Publishing module's guard: these definitions were never published.</summary>
    private sealed class NeverPublishedGuard : IWorkflowDefinitionPublicationDeletionGuard
    {
        public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
