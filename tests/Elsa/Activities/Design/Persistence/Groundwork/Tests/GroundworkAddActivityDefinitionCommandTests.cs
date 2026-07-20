using System.Text.Json;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Exceptions;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.Groundwork.Services;
using Elsa.Persistence.Core.Design;
using Elsa.Persistence.Groundwork.Querying;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

/// <summary>
/// Proves the Groundwork (document) <see cref="GroundworkAddActivityDefinitionCommand"/> atomically writes the
/// activity definition and its first version into one document store and that both read back through the matching
/// read ports — the document-store counterpart of the EF Core add command's single <c>SaveChangesAsync</c>.
/// </summary>
public class GroundworkAddActivityDefinitionCommandTests
{
    private static readonly FakePayloadSerializer Payloads = new();

    [Fact]
    public async Task Mismatched_version_tenant_rejects_the_complete_batch_before_staging()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = new GroundworkAddActivityDefinitionCommand(
            Payloads,
            GroundworkTestAccess.AccessContext("tenant-a"),
            CreateDefinitionStore(store),
            new ImmediateDistributedLockProvider(),
            new GroundworkDesignAtomicWrite(store));
        var definition = new ActivityDefinition
        {
            Id = "def-1",
            ActivityTypeKey = "Acme.Send",
            Category = "General",
            TenantId = "tenant-a"
        };
        var version = Version("def-1", "ver-1", tenantId: "tenant-b");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.Execute(new DesignOperationKey("tenant-mismatch"), definition, version, CancellationToken.None));

        Assert.Equal(0, store.BeginCount);
        Assert.Empty(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Empty(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_explicit_wrong_tenant_before_store_io()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = new GroundworkAddActivityDefinitionVersionCommand(
            Payloads,
            GroundworkTestAccess.AccessContext("tenant-a"),
            CreateVersionStore(store),
            new ImmediateDistributedLockProvider(),
            new GroundworkDesignAtomicWrite(store));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            command.Execute(new DesignOperationKey("tenant-mismatch"), Version("def-1", "ver-1", tenantId: "tenant-b")));

        Assert.Equal(0, store.SaveCount);
        Assert.Empty(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Create_commits_definition_and_version_and_returns_the_authoritative_ids()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = new GroundworkAddActivityDefinitionCommand(
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            CreateDefinitionStore(store),
            new ImmediateDistributedLockProvider(),
            new GroundworkDesignAtomicWrite(store));

        var definition = new ActivityDefinition { Id = "def-1", ActivityTypeKey = "Acme.Send", Category = "General", DisplayName = "Send" };
        var version = Version("def-1", "ver-1");

        var result = await command.Execute(
            new DesignOperationKey("create-1"),
            definition,
            version,
            CancellationToken.None);

        var definitionStore = new GroundworkActivityDefinitionStore(store);
        var versionStore = new GroundworkActivityDefinitionVersionStore(store, definitionStore, Payloads);

        var readDefinition = await definitionStore.GetAsync("def-1");
        var readVersion = await versionStore.GetAsync("ver-1");

        Assert.Equal("Acme.Send", readDefinition.ActivityTypeKey);
        Assert.Equal("Acme.SendActivity", readVersion.DescriptorType);
        Assert.Equal("def-1", readVersion.DefinitionId);
        Assert.Equal("def-1", result.DefinitionId);
        Assert.Equal("ver-1", result.VersionId);
    }

    [Fact]
    public async Task Create_exact_replay_returns_original_ids_without_staging_the_second_request()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = CreateDefinitionCommand(store);

        var first = await command.Execute(
            new DesignOperationKey("create-replay"),
            Definition("def-1"),
            Version("def-1", "ver-1"));
        var replay = await command.Execute(
            new DesignOperationKey("create-replay"),
            Definition("def-2"),
            Version("def-2", "ver-2"));

        Assert.Equal(first, replay);
        Assert.Equal("def-1", replay.DefinitionId);
        Assert.Equal("ver-1", replay.VersionId);
        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Create_changed_material_for_the_same_key_conflicts_without_mutation()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = CreateDefinitionCommand(store);
        var key = new DesignOperationKey("create-conflict");

        await command.Execute(key, Definition("def-1"), Version("def-1", "ver-1"));

        await Assert.ThrowsAsync<GroundworkDesignOperationConflictException>(() =>
            command.Execute(key, Definition("def-2", category: "Changed"), Version("def-2", "ver-2")));

        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Create_rejects_a_different_operation_key_that_collides_with_an_existing_activity_type()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = CreateDefinitionCommand(store);

        await command.Execute(
            new DesignOperationKey("create-first"),
            Definition("def-1"),
            Version("def-1", "ver-1"));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            command.Execute(
                new DesignOperationKey("create-collision"),
                Definition("def-2"),
                Version("def-2", "ver-2")));

        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Create_rejects_a_provider_write_failure_without_persisting_any_document()
    {
        var inner = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var store = new RejectingActivityVersionDocumentStore(inner);
        var command = new GroundworkAddActivityDefinitionCommand(
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            CreateDefinitionStore(inner),
            new ImmediateDistributedLockProvider(),
            new GroundworkDesignAtomicWrite(store));

        await Assert.ThrowsAsync<GroundworkDesignOperationRejectedException>(() =>
            command.Execute(
                new DesignOperationKey("create-rejected"),
                Definition("def-1"),
                Version("def-1", "ver-1")));

        Assert.Empty(inner.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Empty(inner.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_commits_and_returns_the_authoritative_id()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = CreateVersionCommand(store);

        var result = await command.Execute(
            new DesignOperationKey("version-1"),
            Version("def-1", "ver-1"));

        Assert.Equal("ver-1", result.VersionId);
        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_exact_replay_returns_original_id_without_staging_the_second_request()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = CreateVersionCommand(store);
        var key = new DesignOperationKey("version-replay");

        var first = await command.Execute(key, Version("def-1", "ver-1"));
        var replay = await command.Execute(key, Version("def-1", "ver-2"));

        Assert.Equal(first, replay);
        Assert.Equal("ver-1", replay.VersionId);
        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_changed_material_for_the_same_key_conflicts_without_mutation()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = CreateVersionCommand(store);
        var key = new DesignOperationKey("version-conflict");

        await command.Execute(key, Version("def-1", "ver-1"));

        await Assert.ThrowsAsync<GroundworkDesignOperationConflictException>(() =>
            command.Execute(key, Version("def-1", "ver-2", version: "2.0.0")));

        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_a_different_operation_key_that_collides_with_an_existing_semantic_version()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = CreateVersionCommand(store);

        await command.Execute(new DesignOperationKey("version-first"), Version("def-1", "ver-1"));

        await Assert.ThrowsAsync<ActivityDefinitionVersionConflictException>(() =>
            command.Execute(new DesignOperationKey("version-collision"), Version("def-1", "ver-2")));

        Assert.Single(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_rejects_a_provider_write_failure_without_persisting_a_document()
    {
        var inner = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var store = new RejectingActivityVersionDocumentStore(inner);
        // The write wrapper intentionally rejects the version save; reads still target the admitted
        // underlying store so the collision guard can run inside the atomic stage first.
        var command = new GroundworkAddActivityDefinitionVersionCommand(
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            CreateVersionStore(inner),
            new ImmediateDistributedLockProvider(),
            new GroundworkDesignAtomicWrite(store));

        await Assert.ThrowsAsync<GroundworkDesignOperationRejectedException>(() =>
            command.Execute(
                new DesignOperationKey("version-rejected"),
                Version("def-1", "ver-1")));

        Assert.Empty(inner.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Create_observes_caller_cancellation_before_the_atomic_attempt()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = CreateDefinitionCommand(store);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            command.Execute(
                new DesignOperationKey("create-cancelled"),
                Definition("def-1"),
                Version("def-1", "ver-1"),
                cancellation.Token));

        Assert.Equal(0, store.BeginCount);
        Assert.Empty(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Empty(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_observes_caller_cancellation_before_the_atomic_attempt()
    {
        var store = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        var command = CreateVersionCommand(store);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            command.Execute(
                new DesignOperationKey("version-cancelled"),
                Version("def-1", "ver-1"),
                cancellation.Token));

        Assert.Equal(0, store.BeginCount);
        Assert.Empty(store.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Create_reconciles_a_lost_commit_acknowledgement_and_retries_from_its_durable_result()
    {
        var inner = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        using var callerCancellation = new CancellationTokenSource();
        var store = new AcknowledgementLostAfterCommitDocumentStore(inner, callerCancellation);
        var command = CreateDefinitionCommand(store, new NoExistingActivityDefinitionStore());
        var key = new DesignOperationKey("create-lost-acknowledgement");

        var reconciled = await command.Execute(
            key,
            Definition("def-1"),
            Version("def-1", "ver-1"),
            callerCancellation.Token);
        var replay = await command.Execute(
            key,
            Definition("def-2"),
            Version("def-2", "ver-2"));

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.True(store.ReconciliationUsedFreshToken);
        Assert.Equal(reconciled, replay);
        Assert.Equal("def-1", replay.DefinitionId);
        Assert.Equal("ver-1", replay.VersionId);
        Assert.Equal(1, inner.BeginCount);
        Assert.Single(inner.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind));
        Assert.Single(inner.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    [Fact]
    public async Task Add_version_reconciles_a_lost_commit_acknowledgement_and_retries_from_its_durable_result()
    {
        var inner = new InMemoryDocumentStore(ActivitiesDesignStorageManifest.Create());
        using var callerCancellation = new CancellationTokenSource();
        var store = new AcknowledgementLostAfterCommitDocumentStore(inner, callerCancellation);
        var command = CreateVersionCommand(store, new NoExistingActivityDefinitionVersionStore());
        var key = new DesignOperationKey("version-lost-acknowledgement");

        var reconciled = await command.Execute(
            key,
            Version("def-1", "ver-1"),
            callerCancellation.Token);
        var replay = await command.Execute(
            key,
            Version("def-1", "ver-2"));

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.True(store.ReconciliationUsedFreshToken);
        Assert.Equal(reconciled, replay);
        Assert.Equal("ver-1", replay.VersionId);
        Assert.Equal(1, inner.BeginCount);
        Assert.Single(inner.Snapshot(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind));
    }

    private static GroundworkAddActivityDefinitionCommand CreateDefinitionCommand(
        IDocumentStore store,
        IActivityDefinitionStore? definitionStore = null) =>
        new(
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            definitionStore ?? CreateDefinitionStore(store),
            new ImmediateDistributedLockProvider(),
            new GroundworkDesignAtomicWrite(store));

    private static GroundworkAddActivityDefinitionVersionCommand CreateVersionCommand(
        IDocumentStore store,
        IActivityDefinitionVersionStore? versionStore = null) =>
        new(
            Payloads,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            versionStore ?? CreateVersionStore(store),
            new ImmediateDistributedLockProvider(),
            new GroundworkDesignAtomicWrite(store));

    private static GroundworkActivityDefinitionVersionStore CreateVersionStore(IDocumentStore store) =>
        new(store, CreateDefinitionStore(store), Payloads);

    private static GroundworkActivityDefinitionStore CreateDefinitionStore(IDocumentStore store) => new(store);

    private static ActivityDefinition Definition(string id, string category = "General") => new()
    {
        Id = id,
        ActivityTypeKey = "Acme.Send",
        Category = category,
        DisplayName = "Send"
    };

    private static ActivityDefinitionVersion Version(
        string definitionId,
        string id,
        string version = "1.0.0",
        string? tenantId = null) => new(version, definitionId)
    {
        Id = id,
        DescriptorType = "Acme.SendActivity",
        DescriptorPayload = JsonSerializer.SerializeToElement(new { kind = "send" }),
        SourceKind = "Json",
        SourceId = "asset-1",
        DesignFacets = [],
        TenantId = tenantId
    };

    private sealed class RejectingActivityVersionDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        public global::Groundwork.Documents.Scoping.DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<global::Groundwork.Documents.Store.DocumentQueryResult> QueryAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new RejectingActivityVersionUnitOfWork(await inner.BeginAsync(scope, cancellationToken));
    }

    private sealed class RejectingActivityVersionUnitOfWork(IDocumentUnitOfWork inner) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            request.DocumentKind == ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind
                ? Task.FromResult(DocumentStoreWriteResult.NotFound)
                : inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) => inner.CommitAsync(cancellationToken);
        public Task RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class AcknowledgementLostAfterCommitDocumentStore(
        IDocumentStore inner,
        CancellationTokenSource callerCancellation) : IDocumentStore
    {
        private int _ledgerLoads;

        public bool ReconciliationUsedFreshToken { get; private set; }
        public global::Groundwork.Documents.Scoping.DocumentStoreAccess Access => inner.Access;
        public TransactionBoundary TransactionBoundary => inner.TransactionBoundary;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
        {
            if (documentKind == GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind &&
                Interlocked.Increment(ref _ledgerLoads) > 1)
            {
                ReconciliationUsedFreshToken = !cancellationToken.IsCancellationRequested;
            }

            return inner.LoadAsync(documentKind, id, cancellationToken);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, cancellationToken);

        public Task<global::Groundwork.Documents.Store.DocumentQueryResult> QueryAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.QueryAsync(query, cancellationToken);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.FirstOrDefaultAsync(query, cancellationToken);

        public Task<bool> AnyAsync(
            global::Groundwork.Documents.Store.PortableDocumentQuery query,
            CancellationToken cancellationToken = default) => inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new AcknowledgementLostAfterCommitUnitOfWork(
                await inner.BeginAsync(scope, cancellationToken),
                callerCancellation);
    }

    private sealed class AcknowledgementLostAfterCommitUnitOfWork(
        IDocumentUnitOfWork inner,
        CancellationTokenSource callerCancellation) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public async Task CommitAsync(CancellationToken cancellationToken = default)
        {
            await inner.CommitAsync(cancellationToken);
            await callerCancellation.CancelAsync();
            throw new DocumentCommitAcknowledgementUncertainException(
                [GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind]);
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default) => inner.RollbackAsync(cancellationToken);
        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class NoExistingActivityDefinitionStore : IActivityDefinitionStore
    {
        public Task<ActivityDefinition> GetAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromException<ActivityDefinition>(new KeyNotFoundException(id));

        public Task<ActivityDefinition?> FindAsync(
            global::Elsa.Activities.Design.Persistence.Core.Filters.ActivityDefinitionFilter filter,
            CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(null);

        public Task<IReadOnlyList<ActivityDefinition>> ListAsync(
            global::Elsa.Activities.Design.Persistence.Core.Filters.ActivityDefinitionFilter filter,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinition>>([]);

        public Task<ActivityDefinition?> FindByIdOrActivityTypeKeyAsync(
            string id,
            string activityTypeKey,
            CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinition?>(null);

        public Task<bool> ExistsByActivityTypeKeyAsync(string activityTypeKey, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class NoExistingActivityDefinitionVersionStore : IActivityDefinitionVersionStore
    {
        public Task<ActivityDefinitionVersion> GetAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromException<ActivityDefinitionVersion>(new KeyNotFoundException(versionId));

        public Task<ActivityDefinitionVersion> GetWithDefinitionAsync(string versionId, CancellationToken cancellationToken = default) =>
            Task.FromException<ActivityDefinitionVersion>(new KeyNotFoundException(versionId));

        public Task<ActivityDefinitionVersion?> FindByDefinitionAndSortKeyAsync(
            string definitionId,
            string semVerSortKey,
            CancellationToken cancellationToken = default) => Task.FromResult<ActivityDefinitionVersion?>(null);

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionAsync(
            string definitionId,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListByDefinitionIdsAsync(
            IEnumerable<string> definitionIds,
            CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);

        public Task<IReadOnlyList<ActivityDefinitionVersion>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ActivityDefinitionVersion>>([]);
    }
}
