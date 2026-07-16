using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Stores;
using Groundwork.Core.Queries;
using Groundwork.Core.Scoping;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkScopedDocumentStoreTests
{
    private static readonly PersistenceAccessContext AccessContext =
        PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));

    [Fact]
    public async Task Successful_document_operation_releases_its_session()
    {
        var source = new TrackingSessionSource();
        var store = CreateStore(source);

        var result = await store.SaveAsync(new SaveDocumentRequest("kind", "id", "1.0.0", "{}"));

        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
        Assert.Equal(1, source.Store.SaveCount);
        Assert.Equal(1, Assert.Single(source.Leases).DisposeCount);
    }

    [Fact]
    public async Task Exceptional_document_operation_releases_its_session_and_preserves_the_exception()
    {
        var source = new TrackingSessionSource
        {
            ConfigureStore = store => store.LoadFailure = new InvalidOperationException("load failed")
        };
        var store = CreateStore(source);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.LoadAsync("kind", "id"));

        Assert.Equal("load failed", exception.Message);
        Assert.Equal(1, Assert.Single(source.Leases).DisposeCount);
    }

    [Fact]
    public async Task Cancellation_after_session_acquisition_releases_the_session()
    {
        using var cancellation = new CancellationTokenSource();
        var source = new TrackingSessionSource
        {
            ConfigureStore = store => store.CancelDuringLoad = cancellation
        };
        var store = CreateStore(source);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            store.LoadAsync("kind", "id", cancellation.Token));

        Assert.Equal(1, Assert.Single(source.Leases).DisposeCount);
    }

    [Fact]
    public async Task Bounded_query_uses_the_bounded_session_resource_and_releases_it()
    {
        var source = new TrackingSessionSource();
        var store = (IBoundedDocumentStore)CreateStore(source);

        var result = await store.QueryAsync(new DocumentQuery("kind", "by-collection", []));

        Assert.Empty(result.Documents);
        Assert.Equal(1, source.Store.BoundedQueryCount);
        Assert.Equal(1, Assert.Single(source.Leases).DisposeCount);
    }

    [Fact]
    public async Task Unit_of_work_retains_the_session_until_disposal_then_disposes_both_once()
    {
        var source = new TrackingSessionSource();
        var store = CreateStore(source);

        var unitOfWork = await store.BeginAsync(DocumentCommitScope.Of("kind"));
        var lease = Assert.Single(source.Leases);
        Assert.Equal(0, lease.DisposeCount);

        await unitOfWork.DisposeAsync();
        await unitOfWork.DisposeAsync();

        Assert.Equal(1, source.Store.UnitOfWork.DisposeCount);
        Assert.Equal(1, lease.DisposeCount);
    }

    private static GroundworkScopedDocumentStore CreateStore(TrackingSessionSource source)
    {
        var accessor = new FixedAccessContextAccessor(AccessContext);
        var sessions = new GroundworkStoreSessionFactory(accessor, source);
        return new GroundworkScopedDocumentStore(accessor, sessions);
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private sealed class TrackingSessionSource : IGroundworkStoreSessionSource
    {
        public Action<TrackingDocumentStore>? ConfigureStore { get; init; }
        public TrackingDocumentStore Store { get; private set; } = null!;
        public List<TrackingLease> Leases { get; } = [];

        public ValueTask<GroundworkStoreSessionResources> OpenAsync(
            DocumentStoreAccess access,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Store = new TrackingDocumentStore(access);
            ConfigureStore?.Invoke(Store);
            var lease = new TrackingLease();
            Leases.Add(lease);
            return ValueTask.FromResult(new GroundworkStoreSessionResources(Store, Store, lease));
        }
    }

    private sealed class TrackingLease : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TrackingDocumentStore(DocumentStoreAccess access) : IDocumentStore, IBoundedDocumentStore
    {
        public DocumentStoreAccess Access { get; } = access;
        public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;
        public int SaveCount { get; private set; }
        public int BoundedQueryCount { get; private set; }
        public Exception? LoadFailure { get; set; }
        public CancellationTokenSource? CancelDuringLoad { get; set; }
        public TrackingUnitOfWork UnitOfWork { get; } = new();

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveCount++;
            var now = DateTimeOffset.UtcNow;
            return Task.FromResult(DocumentStoreWriteResult.Saved(new DocumentEnvelope(
                request.DocumentKind,
                request.Id,
                request.SchemaVersion,
                1,
                request.ContentJson,
                now,
                now)));
        }

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default)
        {
            if (LoadFailure is not null)
                return Task.FromException<DocumentEnvelope?>(LoadFailure);
            if (CancelDuringLoad is not null)
            {
                CancelDuringLoad.Cancel();
                return Task.FromCanceled<DocumentEnvelope?>(cancellationToken);
            }

            return Task.FromResult<DocumentEnvelope?>(null);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(DocumentStoreWriteResult.NotFound);

#pragma warning disable GW0004
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<DocumentEnvelope>>([]);

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new DocumentQueryResult([], 0));

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DocumentEnvelope?>(null);

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
#pragma warning restore GW0004

        public Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IDocumentUnitOfWork>(UnitOfWork);

        public Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            BoundedQueryCount++;
            return Task.FromResult(new DocumentQueryResult([], 0));
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(0L);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<DocumentEnvelope?>(null);

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private sealed class TrackingUnitOfWork : IDocumentUnitOfWork
    {
        public int DisposeCount { get; private set; }

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return ValueTask.CompletedTask;
        }
    }
}
