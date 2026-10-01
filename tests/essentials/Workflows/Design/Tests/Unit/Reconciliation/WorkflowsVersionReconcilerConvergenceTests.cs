using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Versioning;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Reconciliation;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
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
    private static readonly WorkflowDefinitionState EmptyState = new([], null, [], [], null);
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

    [Fact]
    public async Task An_unchanged_source_writes_nothing()
    {
        await ReconcileAsync(name: "Original");
        await ReconcileAsync(name: "Renamed");
        var settled = await GetDefinitionAsync();
        var markers = await ListMarkersAsync();

        await ReconcileAsync(name: "Renamed");

        Assert.Equal(settled!.LastModifiedAt, (await GetDefinitionAsync())!.LastModifiedAt);
        Assert.Equal(markers, await ListMarkersAsync());
    }

    [Fact]
    public async Task A_marker_written_under_the_per_version_metadata_key_does_not_block_a_later_rename()
    {
        await ReconcileAsync(name: "Original");
        // What a database written before #2187 holds after one rename at this version: the metadata
        // marker keyed on the definition and the sort key of its latest version.
        var sortKey = SemVer.ToSortKey(Version);
        var legacyKey = new DesignOperationKey(
            $"workflow-reconciliation:definition-metadata:{DefinitionId.Length}:{DefinitionId}{sortKey.Length}:{sortKey}");
        await RenameAsync(legacyKey, "Renamed");

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
    public async Task A_permanent_delete_retires_that_definitions_materialization_markers_and_no_others()
    {
        await ReconcileAsync(name: "Original");
        await ReconcileAsync(name: "Sibling", definitionId: SiblingId);
        await ReconcileAsync(name: "Original", deleted: true);
        var before = await ListMarkersAsync();

        await DeletePermanentlyAsync();

        var after = await ListMarkersAsync();
        var sortKey = SemVer.ToSortKey(Version);
        Assert.Equal(
            [
                Marker("workflow.definition.materialize.v1", WorkflowReconciliationOperationKeys.Definition(DefinitionId)),
                Marker("workflow.version.materialize.v1", WorkflowReconciliationOperationKeys.Version(DefinitionId, sortKey))
            ],
            before.Except(after).Order(StringComparer.Ordinal));
        Assert.StartsWith("workflow.definition.permanent-delete.v1 ", Assert.Single(after.Except(before)));
    }

    private async Task ReconcileAsync(string name = "Original", bool deleted = false, string? versionId = null, string definitionId = DefinitionId)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var definition = services.GetRequiredService<IWorkflowDefinitionFactory>().Create(name, id: definitionId, deleted: deleted);
        var version = services.GetRequiredService<IWorkflowDefinitionVersionFactory>().Create(definition, Version, EmptyState, id: versionId);
        var reconciler = ActivatorUtilities.CreateInstance<WorkflowsVersionReconciler>(
            services,
            new ContributingPublisher(version),
            Microsoft.Extensions.Options.Options.Create(new WorkflowVersionReconcilerOptions()));

        await reconciler.Reconcile(CancellationToken.None);
    }

    private async Task RenameAsync(DesignOperationKey key, string name)
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var definition = (await services.GetRequiredService<IWorkflowDefinitionStore>().FindByIdAsync(DefinitionId))!;
        definition.Name = name;
        await services.GetRequiredService<ISaveWorkflowDefinitionCommand>().Execute(key, definition);
    }

    private async Task DeletePermanentlyAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var command = new EfDeleteWorkflowDefinitionPermanentlyCommand(
            services.GetRequiredService<WorkflowsDesignDbContext>(),
            services.GetRequiredService<IPersistenceAccessContextAccessor>(),
            services.GetRequiredService<IDesignAtomicWriter>(),
            [new NeverPublishedGuard()]);
        await command.Execute(new DesignOperationKey($"permanent-delete-{Guid.NewGuid():N}"), DefinitionId);
    }

    private async Task<WorkflowDefinition?> GetDefinitionAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionStore>().FindByIdAsync(DefinitionId);
    }

    private async Task<WorkflowDefinitionVersion?> GetLatestVersionAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowDefinitionVersionStore>().FindLatestVersionAsync(DefinitionId);
    }

    private async Task<string[]> ListMarkersAsync()
    {
        await using var scope = _host.Services.CreateAsyncScope();
        var markers = await scope.ServiceProvider.GetRequiredService<WorkflowsDesignDbContext>().Operations
            .AsNoTracking()
            .Select(marker => marker.OperationKind + " " + marker.OperationKey)
            .ToListAsync();
        return markers.Order(StringComparer.Ordinal).ToArray();
    }

    private static string Marker(string operationKind, DesignOperationKey key) => operationKind + " " + key.Value;

    /// <summary>Contributes the given versions to the pass, as the aggregating handler does for real sources.</summary>
    private sealed class ContributingPublisher(params IWorkflowDefinitionVersion[] versions) : IInlineEventPublisher
    {
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            if (@event is WorkflowVersionsReconciling reconciling)
                foreach (var version in versions)
                    reconciling.Versions.Add(version);
            return Task.CompletedTask;
        }
    }

    /// <summary>Stands in for the Publishing module's guard: these definitions were never published.</summary>
    private sealed class NeverPublishedGuard : IWorkflowDefinitionPublicationDeletionGuard
    {
        public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
