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
    public async Task Every_document_and_bounded_operation_uses_an_ordinary_scoped_session()
    {
        var source = new TrackingSessionSource();
        var store = CreateStore(source);
        var bounded = (IBoundedDocumentStore)store;

        await store.SaveAsync(new SaveDocumentRequest("kind", "id", "1.0.0", "{}"));
        await store.LoadAsync("kind", "id");
        await store.DeleteAsync(new DeleteDocumentRequest("kind", "id"));
#pragma warning disable GW0004 // Drives the adapter's compatibility surface on purpose: this test proves each retired member still takes an ordinary scoped session.
        await store.QueryAsync(new DocumentStoreQuery("kind", "by-value", "value"));
        await store.QueryAsync(new PortableDocumentQuery("kind"));
        await store.FirstOrDefaultAsync(new PortableDocumentQuery("kind"));
        await store.AnyAsync(new PortableDocumentQuery("kind"));
#pragma warning restore GW0004
        var query = new DocumentQuery("kind", "bounded", []);
        await bounded.QueryAsync(query);
        await bounded.CountAsync(query);
        await bounded.FirstOrDefaultAsync(query);
        await bounded.AnyAsync(query);

        Assert.Equal(
        [
            "save",
            "load",
            "delete",
            "legacy-query",
            "portable-query",
            "portable-first",
            "portable-any",
            "bounded-query",
            "bounded-count",
            "bounded-first",
            "bounded-any"
        ], source.Calls);
        Assert.Equal(source.Calls.Count, source.Leases.Count);
        Assert.All(source.Leases, lease => Assert.Equal(1, lease.DisposeCount));
        var expectedAccess = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        Assert.All(source.Stores, openedStore => Assert.Equal(expectedAccess, openedStore.Access));
        Assert.Equal(expectedAccess, store.Access);
    }

    [Fact]
    public async Task Begin_failure_releases_the_acquired_session_and_preserves_the_exception()
    {
        var expected = new InvalidOperationException("begin failed");
        var source = new TrackingSessionSource
        {
            ConfigureStore = store => store.BeginFailure = expected
        };
        var store = CreateStore(source);

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.BeginAsync(DocumentCommitScope.Of("kind")));

        Assert.Same(expected, actual);
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

    [Fact]
    public async Task Public_session_constructor_enforces_the_complete_privilege_dependency_pair()
    {
        var ordinary = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        var privileged = GroundworkPersistenceAccessMapper.Map(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("repair")),
            PersistenceAccessPolicy.Privileged);
        var emitter = new RecordingEmitter();
        var audit = new GroundworkPrivilegedAccessAudit(
            Guid.NewGuid(),
            privileged.Kind,
            "global",
            "repair");

        await using var ordinarySession = new GroundworkStoreSession(ordinary, Resources(ordinary));
        var privilegedSession = new GroundworkStoreSession(privileged, Resources(privileged), emitter, audit);

        Assert.Throws<ArgumentException>(() => new GroundworkStoreSession(ordinary, Resources(ordinary), emitter));
        Assert.Throws<ArgumentException>(() => new GroundworkStoreSession(ordinary, Resources(ordinary), null, audit));
        Assert.Throws<ArgumentException>(() => new GroundworkStoreSession(privileged, Resources(privileged)));
        Assert.Throws<ArgumentException>(() => new GroundworkStoreSession(privileged, Resources(privileged), emitter));
        Assert.Throws<ArgumentException>(() => new GroundworkStoreSession(privileged, Resources(privileged), null, audit));

        await privilegedSession.DisposeAsync();
        Assert.Equal([(audit, GroundworkPrivilegedAccessOutcome.Abandoned)], emitter.Outcomes);
    }

    [Fact]
    public void Public_session_constructor_enforces_resource_and_audit_access_binding()
    {
        var access = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        var otherAccess = DocumentStoreAccess.Scoped(new StorageScope("tenant-b"));
        var privileged = GroundworkPersistenceAccessMapper.Map(
            PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("repair")),
            PersistenceAccessPolicy.Privileged);
        var emitter = new RecordingEmitter();
        var mismatchedAudit = new GroundworkPrivilegedAccessAudit(
            Guid.NewGuid(),
            DocumentStoreAccessKind.PrivilegedAcrossScopes,
            "across-scopes",
            "repair");
        var scopedPrivileged = GroundworkPersistenceAccessMapper.Map(
            PersistenceAccessContext.PrivilegedScoped(
                new PersistenceScope("tenant-a"),
                new PersistenceAccessPurpose("repair")),
            PersistenceAccessPolicy.Privileged);
        var recorder = new GroundworkPrivilegedAccessRecorder(new GroundworkPrivilegedAccessSink());
        var scopedAudit = recorder.RecordAcquisition(scopedPrivileged);

        Assert.Throws<InvalidOperationException>(() =>
            new GroundworkStoreSession(access, Resources(otherAccess)));
        Assert.Throws<ArgumentException>(() =>
            new GroundworkStoreSession(privileged, Resources(privileged), emitter, mismatchedAudit));
        Assert.Throws<ArgumentException>(() =>
            new GroundworkStoreSession(
                scopedPrivileged,
                Resources(scopedPrivileged),
                recorder,
                scopedAudit with { ScopeReference = "sha256:different-scope" }));
        Assert.Throws<ArgumentException>(() =>
            new GroundworkStoreSession(
                scopedPrivileged,
                Resources(scopedPrivileged),
                recorder,
                scopedAudit with { Purpose = "different-purpose" }));
    }

    [Fact]
    public async Task Public_session_constructor_preserves_default_and_cleanup_failure_behavior()
    {
        var access = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        var failure = new InvalidOperationException("lease failed");
        var lease = new TrackingLease(failure);
        var session = new GroundworkStoreSession(access, Resources(access, lease));

        Assert.Same(access, session.Access);
        Assert.Throws<ArgumentNullException>(() => new GroundworkStoreSession(null!, Resources(access)));
        Assert.Throws<ArgumentNullException>(() => new GroundworkStoreSession(access, null!));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());

        Assert.Same(failure, actual);
        Assert.Equal(1, lease.DisposeCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.DisposeAsync().AsTask());
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task Public_session_unit_of_work_forwards_every_operation_and_disposes_in_order_once()
    {
        var access = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        var lease = new TrackingLease();
        var inner = new TrackingUnitOfWork();
        var unitOfWork = new SessionUnitOfWork(inner, new GroundworkStoreSession(access, Resources(access, lease)));
        var save = new SaveDocumentRequest("kind", "id", "1.0.0", "{}");
        var delete = new DeleteDocumentRequest("kind", "id");
        using var cancellation = new CancellationTokenSource();

        await unitOfWork.SaveAsync(save, cancellation.Token);
        await unitOfWork.DeleteAsync(delete, cancellation.Token);
        await unitOfWork.LoadAsync("kind", "id", cancellation.Token);
        await unitOfWork.CommitAsync(cancellation.Token);
        await unitOfWork.RollbackAsync(cancellation.Token);
        await unitOfWork.DisposeAsync();
        await unitOfWork.DisposeAsync();

        Assert.Same(save, inner.SaveRequest);
        Assert.Same(delete, inner.DeleteRequest);
        Assert.Equal(("kind", "id"), inner.LoadRequest);
        Assert.All(inner.CancellationTokens, token => Assert.Equal(cancellation.Token, token));
        Assert.Equal(["save", "delete", "load", "commit", "rollback", "dispose"], inner.Calls);
        Assert.Equal(1, inner.DisposeCount);
        Assert.Equal(1, lease.DisposeCount);
    }

    [Fact]
    public async Task Public_session_unit_of_work_validates_dependencies_and_preserves_the_primary_disposal_failure()
    {
        var access = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        var unitFailure = new InvalidOperationException("unit failed");
        var sessionFailure = new InvalidOperationException("session failed");
        var inner = new TrackingUnitOfWork { DisposeFailure = unitFailure };
        var unitOfWork = new SessionUnitOfWork(
            inner,
            new GroundworkStoreSession(access, Resources(access, new TrackingLease(sessionFailure))));

        Assert.Throws<ArgumentNullException>(() => new SessionUnitOfWork(null!, new GroundworkStoreSession(access, Resources(access))));
        Assert.Throws<ArgumentNullException>(() => new SessionUnitOfWork(inner, null!));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.DisposeAsync().AsTask());

        Assert.Same(unitFailure, actual);
        Assert.Same(sessionFailure, actual.Data["Elsa.Persistence.Groundwork.SessionDisposalFailure"]);
        await unitOfWork.DisposeAsync();
        Assert.Equal(1, inner.DisposeCount);
    }

    [Fact]
    public async Task Public_session_unit_of_work_propagates_a_session_disposal_failure_when_the_unit_succeeds()
    {
        var access = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        var sessionFailure = new InvalidOperationException("session failed");
        var inner = new TrackingUnitOfWork();
        var unitOfWork = new SessionUnitOfWork(
            inner,
            new GroundworkStoreSession(access, Resources(access, new TrackingLease(sessionFailure))));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.DisposeAsync().AsTask());

        Assert.Same(sessionFailure, actual);
        Assert.Equal(1, inner.DisposeCount);
    }

    [Fact]
    public async Task Public_session_unit_of_work_propagates_an_inner_disposal_failure_after_cleaning_up_the_session()
    {
        var access = DocumentStoreAccess.Scoped(new StorageScope("tenant-a"));
        var unitFailure = new InvalidOperationException("unit failed");
        var lease = new TrackingLease();
        var inner = new TrackingUnitOfWork { DisposeFailure = unitFailure };
        var unitOfWork = new SessionUnitOfWork(
            inner,
            new GroundworkStoreSession(access, Resources(access, lease)));

        var actual = await Assert.ThrowsAsync<InvalidOperationException>(() => unitOfWork.DisposeAsync().AsTask());

        Assert.Same(unitFailure, actual);
        Assert.Equal(1, lease.DisposeCount);
    }

    private static GroundworkScopedDocumentStore CreateStore(TrackingSessionSource source)
    {
        var accessor = new FixedAccessContextAccessor(AccessContext);
        var sessions = new GroundworkStoreSessionFactory(accessor, source);
        return new GroundworkScopedDocumentStore(accessor, sessions);
    }

    private static GroundworkStoreSessionResources Resources(
        DocumentStoreAccess access,
        TrackingLease? lease = null)
    {
        var store = new TrackingDocumentStore(access, []);
        return new GroundworkStoreSessionResources(store, store, lease);
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
        public List<TrackingDocumentStore> Stores { get; } = [];
        public List<TrackingLease> Leases { get; } = [];
        public List<string> Calls { get; } = [];

        public ValueTask<GroundworkStoreSessionResources> OpenAsync(
            DocumentStoreAccess access,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Store = new TrackingDocumentStore(access, Calls);
            Stores.Add(Store);
            ConfigureStore?.Invoke(Store);
            var lease = new TrackingLease();
            Leases.Add(lease);
            return ValueTask.FromResult(new GroundworkStoreSessionResources(Store, Store, lease));
        }
    }

    private sealed class TrackingLease(Exception? failure = null) : IAsyncDisposable
    {
        public int DisposeCount { get; private set; }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            return failure is null ? ValueTask.CompletedTask : ValueTask.FromException(failure);
        }
    }

    private sealed class TrackingDocumentStore(
        DocumentStoreAccess access,
        ICollection<string> calls) : IDocumentStore, IBoundedDocumentStore
    {
        public DocumentStoreAccess Access { get; } = access;
        public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;
        public int SaveCount { get; private set; }
        public int BoundedQueryCount { get; private set; }
        public Exception? LoadFailure { get; set; }
        public Exception? BeginFailure { get; set; }
        public CancellationTokenSource? CancelDuringLoad { get; set; }
        public TrackingUnitOfWork UnitOfWork { get; } = new();

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            calls.Add("save");
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
            calls.Add("load");
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
            CancellationToken cancellationToken = default)
        {
            calls.Add("delete");
            return Task.FromResult(DocumentStoreWriteResult.NotFound);
        }

#pragma warning disable GW0004 // Required IDocumentStore compatibility members on the recording test double.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(
            DocumentStoreQuery query,
            CancellationToken cancellationToken = default)
        {
            calls.Add("legacy-query");
            return Task.FromResult<IReadOnlyList<DocumentEnvelope>>([]);
        }

        public Task<DocumentQueryResult> QueryAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            calls.Add("portable-query");
            return Task.FromResult(new DocumentQueryResult([], 0));
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            calls.Add("portable-first");
            return Task.FromResult<DocumentEnvelope?>(null);
        }

        public Task<bool> AnyAsync(
            PortableDocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            calls.Add("portable-any");
            return Task.FromResult(false);
        }
#pragma warning restore GW0004

        public Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default)
        {
            if (BeginFailure is not null)
                return Task.FromException<IDocumentUnitOfWork>(BeginFailure);
            calls.Add("begin");
            return Task.FromResult<IDocumentUnitOfWork>(UnitOfWork);
        }

        public Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            calls.Add("bounded-query");
            BoundedQueryCount++;
            return Task.FromResult(new DocumentQueryResult([], 0));
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            calls.Add("bounded-count");
            return Task.FromResult(0L);
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default)
        {
            calls.Add("bounded-first");
            return Task.FromResult<DocumentEnvelope?>(null);
        }

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            calls.Add("bounded-any");
            return Task.FromResult(false);
        }
    }

    private sealed class TrackingUnitOfWork : IDocumentUnitOfWork
    {
        public int DisposeCount { get; private set; }
        public List<string> Calls { get; } = [];
        public List<CancellationToken> CancellationTokens { get; } = [];
        public SaveDocumentRequest? SaveRequest { get; private set; }
        public DeleteDocumentRequest? DeleteRequest { get; private set; }
        public (string DocumentKind, string Id)? LoadRequest { get; private set; }
        public Exception? DisposeFailure { get; init; }

        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            SaveRequest = request;
            CancellationTokens.Add(cancellationToken);
            Calls.Add("save");
            return Task.FromResult(DocumentStoreWriteResult.NotFound);
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default)
        {
            DeleteRequest = request;
            CancellationTokens.Add(cancellationToken);
            Calls.Add("delete");
            return Task.FromResult(DocumentStoreWriteResult.NotFound);
        }

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default)
        {
            LoadRequest = (documentKind, id);
            CancellationTokens.Add(cancellationToken);
            Calls.Add("load");
            return Task.FromResult<DocumentEnvelope?>(null);
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            Calls.Add("commit");
            return Task.CompletedTask;
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            CancellationTokens.Add(cancellationToken);
            Calls.Add("rollback");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposeCount++;
            Calls.Add("dispose");
            return DisposeFailure is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeFailure);
        }
    }

    private sealed class RecordingEmitter : IGroundworkPrivilegedAccessEmitter
    {
        public List<(GroundworkPrivilegedAccessAudit Audit, GroundworkPrivilegedAccessOutcome Outcome)> Outcomes { get; } = [];

        public GroundworkPrivilegedAccessAudit RecordAcquisition(DocumentStoreAccess access) =>
            throw new NotSupportedException();

        public void RecordOutcome(
            GroundworkPrivilegedAccessAudit audit,
            GroundworkPrivilegedAccessOutcome outcome,
            Exception? failure = null,
            Exception? cleanupFailure = null)
        {
            Outcomes.Add((audit, outcome));
        }
    }
}
