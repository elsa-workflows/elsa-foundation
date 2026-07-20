using Elsa.Persistence.Groundwork.Querying;
using System.Text.Json;
using Groundwork.Core.Intents;
using Groundwork.Core.Manifests;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

/// <summary>
/// Specifies the design-operation ledger contract independently of workflow and activity commands.
/// The helper must own the marker transaction, replay inspection, and acknowledgement reconciliation;
/// command adapters only provide their staged domain writes and their authoritative result payload.
/// </summary>
public sealed class GroundworkDesignAtomicWriteTests
{
    private const string AggregateDocumentKind = "designAggregate";
    private const string SchemaVersion = "1.0.0";
    private const string ResultFingerprint = "result:workflow-create:v1";
    private const string ResultJson = "{\"definitionId\":\"definition-1\"}";

    [Fact]
    public async Task Rejects_a_non_atomic_store_before_any_document_io()
    {
        var inner = CreateStore();
        var documents = new PerOperationDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(documents);

        await Assert.ThrowsAsync<GroundworkDesignAtomicWriteReadinessException>(() =>
            write.ExecuteAsync(Request(), AcceptedWithoutDomainWritesAsync, CancellationToken.None));

        Assert.Equal(0, inner.LoadCount);
        Assert.Equal(0, inner.SaveCount);
        Assert.Equal(0, inner.DeleteCount);
        Assert.Equal(0, inner.BeginCount);
    }

    [Fact]
    public async Task Commits_staged_domain_writes_and_the_durable_operation_marker_in_one_scope()
    {
        var inner = CreateStore();
        var documents = new ScopeRecordingDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(documents);

        var result = await write.ExecuteAsync(
            Request(),
            async (unitOfWork, cancellationToken) =>
            {
                var saved = await unitOfWork.SaveAsync(SaveAggregate("definition-1"), cancellationToken);
                Assert.Equal(DocumentStoreWriteStatus.Saved, saved.Status);
                return GroundworkDesignAtomicWriteStageResult.Accepted(ResultFingerprint, ResultJson);
            },
            CancellationToken.None);

        Assert.Equal(GroundworkDesignAtomicWriteStatus.Committed, result.Status);
        Assert.Equal(ResultFingerprint, result.AuthoritativeResultFingerprint);
        Assert.Equal(ResultJson, result.AuthoritativeResultJson);
        Assert.NotNull(await inner.LoadAsync(AggregateDocumentKind, "definition-1"));

        Assert.NotNull(documents.LastScope);
        Assert.Contains(AggregateDocumentKind, documents.LastScope!);
        Assert.Contains(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind, documents.LastScope!);
        Assert.Equal(2, documents.LastScope!.Count);

        var marker = Assert.Single(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
        Assert.Contains(ResultFingerprint, marker.ContentJson, StringComparison.Ordinal);
        using var markerJson = JsonDocument.Parse(marker.ContentJson);
        Assert.Equal(
            ResultJson,
            markerJson.RootElement.GetProperty("authoritativeResultJson").GetString());
    }

    [Fact]
    public async Task Rejected_stage_rolls_back_without_a_domain_write_or_marker()
    {
        var inner = CreateStore();
        var documents = new RollbackRecordingDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(documents);

        var result = await write.ExecuteAsync(
            Request(),
            async (unitOfWork, cancellationToken) =>
            {
                var staged = await unitOfWork.SaveAsync(
                    SaveAggregate("definition-1"),
                    cancellationToken);
                Assert.Equal(DocumentStoreWriteStatus.Saved, staged.Status);
                return GroundworkDesignAtomicWriteStageResult.Rejected();
            },
            CancellationToken.None);

        Assert.Equal(GroundworkDesignAtomicWriteStatus.Rejected, result.Status);
        Assert.Equal(1, documents.RollbackCount);
        Assert.Empty(inner.Snapshot(AggregateDocumentKind));
        Assert.Empty(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Non_success_provider_decision_rejects_and_rolls_back_every_staged_write()
    {
        var inner = CreateStore();
        var documents = new RollbackRecordingDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(documents);

        var result = await write.ExecuteAsync(
            Request(),
            async (context, cancellationToken) =>
            {
                var staged = await context.SaveAsync(
                    SaveAggregate("definition-1"),
                    cancellationToken);
                Assert.Equal(DocumentStoreWriteStatus.Saved, staged.Status);
                var rejected = await context.SaveAsync(
                    new SaveDocumentRequest(
                        AggregateDocumentKind,
                        "definition-2",
                        SchemaVersion,
                        "{}",
                        ExpectedVersion: 99),
                    cancellationToken);
                Assert.Equal(DocumentStoreWriteStatus.NotFound, rejected.Status);
                return GroundworkDesignAtomicWriteStageResult.Accepted(ResultFingerprint, ResultJson);
            },
            CancellationToken.None);

        Assert.Equal(GroundworkDesignAtomicWriteStatus.Rejected, result.Status);
        Assert.Equal(1, documents.RollbackCount);
        Assert.Empty(inner.Snapshot(AggregateDocumentKind));
        Assert.Empty(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Non_success_marker_decision_rejects_and_rolls_back_every_staged_write()
    {
        var inner = CreateStore();
        var documents = new RejectingMarkerSaveDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(documents);

        var result = await write.ExecuteAsync(
            Request(),
            async (context, cancellationToken) =>
            {
                var staged = await context.SaveAsync(SaveAggregate("definition-1"), cancellationToken);
                Assert.Equal(DocumentStoreWriteStatus.Saved, staged.Status);
                return GroundworkDesignAtomicWriteStageResult.Accepted(ResultFingerprint, ResultJson);
            },
            CancellationToken.None);

        Assert.Equal(GroundworkDesignAtomicWriteStatus.Rejected, result.Status);
        Assert.Equal(1, documents.RollbackCount);
        Assert.Empty(inner.Snapshot(AggregateDocumentKind));
        Assert.Empty(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Stage_fault_rolls_back_all_staged_writes_and_does_not_create_a_marker()
    {
        var inner = CreateStore();
        var documents = new RollbackRecordingDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(documents);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            write.ExecuteAsync(
                Request(),
                async (unitOfWork, cancellationToken) =>
                {
                    await unitOfWork.SaveAsync(SaveAggregate("definition-1"), cancellationToken);
                    throw new InvalidOperationException("staging failed");
                },
                CancellationToken.None));

        Assert.Equal(1, documents.RollbackCount);
        Assert.Empty(inner.Snapshot(AggregateDocumentKind));
        Assert.Empty(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Stage_fault_preserves_the_primary_exception_and_attaches_a_rollback_failure()
    {
        var inner = CreateStore();
        var documents = new RollbackThrowingDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(documents);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            write.ExecuteAsync(
                Request(),
                async (context, cancellationToken) =>
                {
                    await context.SaveAsync(SaveAggregate("definition-1"), cancellationToken);
                    throw new InvalidOperationException("staging failed");
                },
                CancellationToken.None));

        Assert.Equal("staging failed", exception.Message);
        var rollbackFailure = Assert.Single(exception.Data.Values.Cast<object>());
        Assert.IsType<IOException>(rollbackFailure);
        Assert.Equal("rollback failed", ((IOException)rollbackFailure).Message);
        Assert.Empty(inner.Snapshot(AggregateDocumentKind));
        Assert.Empty(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Cancellation_rolls_back_staged_writes_and_preserves_cancellation()
    {
        var inner = CreateStore();
        var documents = new RollbackRecordingDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(documents);
        using var cancellation = new CancellationTokenSource();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            write.ExecuteAsync(
                Request(),
                async (unitOfWork, cancellationToken) =>
                {
                    await unitOfWork.SaveAsync(SaveAggregate("definition-1"), cancellationToken);
                    await cancellation.CancelAsync();
                    throw new OperationCanceledException(cancellation.Token);
                },
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, documents.RollbackCount);
        Assert.Empty(inner.Snapshot(AggregateDocumentKind));
        Assert.Empty(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Exact_operation_identity_and_fingerprint_replays_the_authoritative_result_without_restaging()
    {
        var store = CreateStore();
        var write = new GroundworkDesignAtomicWrite(store);
        var stageCalls = 0;

        var committed = await write.ExecuteAsync(
            Request(),
            async (unitOfWork, cancellationToken) =>
            {
                stageCalls++;
                await unitOfWork.SaveAsync(SaveAggregate("definition-1"), cancellationToken);
                return GroundworkDesignAtomicWriteStageResult.Accepted(ResultFingerprint, ResultJson);
            },
            CancellationToken.None);

        var replay = await write.ExecuteAsync(
            Request(),
            (_, _) => throw new Xunit.Sdk.XunitException("A durable exact replay must not re-stage domain writes."),
            CancellationToken.None);

        Assert.Equal(GroundworkDesignAtomicWriteStatus.Committed, committed.Status);
        Assert.Equal(GroundworkDesignAtomicWriteStatus.Replayed, replay.Status);
        Assert.Equal(ResultFingerprint, replay.AuthoritativeResultFingerprint);
        Assert.Equal(ResultJson, replay.AuthoritativeResultJson);
        Assert.Equal(1, stageCalls);
        Assert.Single(store.Snapshot(AggregateDocumentKind));
        Assert.Single(store.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Reusing_an_operation_identity_with_a_changed_fingerprint_conflicts_without_restaging()
    {
        var store = CreateStore();
        var write = new GroundworkDesignAtomicWrite(store);

        await write.ExecuteAsync(Request(), AcceptedWithoutDomainWritesAsync, CancellationToken.None);

        var result = await write.ExecuteAsync(
            Request(fingerprint: "canonical:workflow-create:v2"),
            (_, _) => throw new Xunit.Sdk.XunitException("A fingerprint conflict must not re-stage domain writes."),
            CancellationToken.None);

        Assert.Equal(GroundworkDesignAtomicWriteStatus.Conflict, result.Status);
        Assert.Null(result.AuthoritativeResultFingerprint);
        Assert.Null(result.AuthoritativeResultJson);
        Assert.Single(store.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Marker_create_race_reloads_the_winner_and_replays_without_a_second_stage()
    {
        var inner = CreateStore();
        var original = new GroundworkDesignAtomicWrite(inner);
        await original.ExecuteAsync(Request(), AcceptedWithoutDomainWritesAsync, CancellationToken.None);

        var documents = new HideFirstLedgerLoadDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(documents);
        var stageCalls = 0;

        var result = await write.ExecuteAsync(
            Request(),
            (_, _) =>
            {
                stageCalls++;
                return Task.FromResult(GroundworkDesignAtomicWriteStageResult.Accepted("ignored", "{}"));
            },
            CancellationToken.None);

        Assert.Equal(GroundworkDesignAtomicWriteStatus.Replayed, result.Status);
        Assert.Equal(ResultFingerprint, result.AuthoritativeResultFingerprint);
        Assert.Equal(ResultJson, result.AuthoritativeResultJson);
        Assert.Equal(1, stageCalls);
        Assert.Equal(1, documents.RollbackCount);
        Assert.Single(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Corrupt_marker_json_is_rejected_before_staging()
    {
        var store = CreateStore();
        var write = new GroundworkDesignAtomicWrite(store);
        await write.ExecuteAsync(Request(), AcceptedWithoutDomainWritesAsync, CancellationToken.None);
        var marker = Assert.Single(
            store.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
        await store.SaveAsync(
            new SaveDocumentRequest(
                marker.DocumentKind,
                marker.Id,
                marker.SchemaVersion,
                "{",
                marker.Version),
            CancellationToken.None);
        var stageCalls = 0;

        await Assert.ThrowsAsync<GroundworkDesignAtomicWriteCorruptMarkerException>(() =>
            write.ExecuteAsync(
                Request(),
                (_, _) =>
                {
                    stageCalls++;
                    return Task.FromResult(
                        GroundworkDesignAtomicWriteStageResult.Accepted(ResultFingerprint, ResultJson));
                },
                CancellationToken.None));

        Assert.Equal(0, stageCalls);
    }

    [Fact]
    public async Task Unsupported_marker_schema_version_is_rejected_before_staging()
    {
        var store = CreateStore();
        var write = new GroundworkDesignAtomicWrite(store);
        await write.ExecuteAsync(Request(), AcceptedWithoutDomainWritesAsync, CancellationToken.None);
        var marker = Assert.Single(
            store.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
        await store.SaveAsync(
            new SaveDocumentRequest(
                marker.DocumentKind,
                marker.Id,
                "2.0.0",
                marker.ContentJson,
                marker.Version),
            CancellationToken.None);
        var stageCalls = 0;

        await Assert.ThrowsAsync<GroundworkDesignAtomicWriteCorruptMarkerException>(() =>
            write.ExecuteAsync(
                Request(),
                (_, _) =>
                {
                    stageCalls++;
                    return Task.FromResult(
                        GroundworkDesignAtomicWriteStageResult.Accepted(ResultFingerprint, ResultJson));
                },
                CancellationToken.None));

        Assert.Equal(0, stageCalls);
    }

    [Fact]
    public async Task Uncertain_acknowledgement_reconciles_with_a_fresh_token_and_does_not_restage()
    {
        var inner = CreateStore();
        using var callerCancellation = new CancellationTokenSource();
        var documents = new UncertainAfterCommitDocumentStore(inner, callerCancellation);
        var write = new GroundworkDesignAtomicWrite(
            documents,
            TimeProvider.System,
            reconciliationTimeout: TimeSpan.FromSeconds(1));
        var stageCalls = 0;

        var result = await write.ExecuteAsync(
            Request(),
            (_, _) =>
            {
                stageCalls++;
                return Task.FromResult(GroundworkDesignAtomicWriteStageResult.Accepted(ResultFingerprint, ResultJson));
            },
            callerCancellation.Token);

        Assert.True(callerCancellation.IsCancellationRequested);
        Assert.Equal(GroundworkDesignAtomicWriteStatus.Reconciled, result.Status);
        Assert.Equal(ResultFingerprint, result.AuthoritativeResultFingerprint);
        Assert.Equal(ResultJson, result.AuthoritativeResultJson);
        Assert.True(documents.ReconciliationUsedFreshToken);
        Assert.Equal(1, stageCalls);
        Assert.Single(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Uncertain_acknowledgement_without_a_marker_surfaces_uncertain_and_does_not_restage()
    {
        var inner = CreateStore();
        var documents = new UncertainWithoutCommitDocumentStore(inner);
        var write = new GroundworkDesignAtomicWrite(
            documents,
            TimeProvider.System,
            reconciliationTimeout: TimeSpan.FromSeconds(1));
        var stageCalls = 0;

        await Assert.ThrowsAsync<GroundworkDesignAtomicWriteUncertainCommitException>(() =>
            write.ExecuteAsync(
                Request(),
                (_, _) =>
                {
                    stageCalls++;
                    return Task.FromResult(
                        GroundworkDesignAtomicWriteStageResult.Accepted(ResultFingerprint, ResultJson));
                },
                CancellationToken.None));

        Assert.Equal(1, stageCalls);
        Assert.Empty(inner.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    [Fact]
    public async Task Same_operation_identity_is_local_to_each_independent_scope_store()
    {
        var firstScope = CreateStore();
        var secondScope = CreateStore();

        var first = await new GroundworkDesignAtomicWrite(firstScope)
            .ExecuteAsync(Request(), AcceptedWithoutDomainWritesAsync, CancellationToken.None);
        var second = await new GroundworkDesignAtomicWrite(secondScope)
            .ExecuteAsync(Request(), AcceptedWithoutDomainWritesAsync, CancellationToken.None);

        Assert.Equal(GroundworkDesignAtomicWriteStatus.Committed, first.Status);
        Assert.Equal(GroundworkDesignAtomicWriteStatus.Committed, second.Status);
        Assert.Single(firstScope.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
        Assert.Single(secondScope.Snapshot(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind));
    }

    private static GroundworkDesignAtomicWriteRequest Request(
        string fingerprint = "canonical:workflow-create:v1") =>
        new(
            new GroundworkDesignOperationIdentity("workflow.create", "operation-1"),
            fingerprint,
            [AggregateDocumentKind]);

    private static Task<GroundworkDesignAtomicWriteStageResult> AcceptedWithoutDomainWritesAsync(
        GroundworkDesignAtomicWriteContext _,
        CancellationToken __) =>
        Task.FromResult(GroundworkDesignAtomicWriteStageResult.Accepted(ResultFingerprint, ResultJson));

    private static SaveDocumentRequest SaveAggregate(string id) =>
        new(AggregateDocumentKind, id, SchemaVersion, "{}");

    private static InMemoryDocumentStore CreateStore() => new(BuildManifest());

    private static StorageManifest BuildManifest() => new(
        new StorageManifestIdentity("elsa-groundwork-design-atomic-write-tests"),
        new StorageManifestOwner("elsa.persistence.groundwork.querying.tests"),
        new StorageManifestVersion(SchemaVersion),
        [
            Unit(AggregateDocumentKind, "Design aggregate"),
            Unit(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind, "Design operation ledger")
        ],
        new HashSet<string> { "optimistic-concurrency" },
        []);

    private static StorageUnit Unit(string kind, string label) =>
        new(
            new StorageUnitIdentity(kind),
            label,
            StorageIntent.PortableDocument(),
            LifecyclePolicy.Mutable,
            IdentityPolicy.StringId(),
            TenancyPolicy.Global,
            ConcurrencyPolicy.Optimistic(),
            SerializationPolicy.Json(),
            [],
            [],
            PhysicalizationPolicy.Portable);

    private class DelegatingDocumentStore(IDocumentStore inner) : IDocumentStore
    {
        protected IDocumentStore Inner { get; } = inner;

        public virtual DocumentStoreAccess Access => Inner.Access;
        public virtual TransactionBoundary TransactionBoundary => Inner.TransactionBoundary;

        public virtual Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default) =>
            Inner.SaveAsync(request, cancellationToken);

        public virtual Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default) =>
            Inner.LoadAsync(documentKind, id, cancellationToken);

        public virtual Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default) =>
            Inner.DeleteAsync(request, cancellationToken);

#pragma warning disable GW0004 // The wrappers delegate the complete compatibility surface unchanged.
        public virtual Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            Inner.QueryAsync(query, cancellationToken);

        public virtual Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            Inner.QueryAsync(query, cancellationToken);

        public virtual Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            Inner.FirstOrDefaultAsync(query, cancellationToken);

        public virtual Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            Inner.AnyAsync(query, cancellationToken);
#pragma warning restore GW0004

        public virtual Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            Inner.BeginAsync(scope, cancellationToken);
    }

    private sealed class PerOperationDocumentStore(IDocumentStore inner) : DelegatingDocumentStore(inner)
    {
        public override TransactionBoundary TransactionBoundary => TransactionBoundary.PerOperation;
    }

    private sealed class ScopeRecordingDocumentStore(IDocumentStore inner) : DelegatingDocumentStore(inner)
    {
        public IReadOnlyList<string>? LastScope { get; private set; }

        public override async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default)
        {
            LastScope = scope.Kinds;
            return await base.BeginAsync(scope, cancellationToken);
        }
    }

    private sealed class RollbackRecordingDocumentStore(IDocumentStore inner) : DelegatingDocumentStore(inner)
    {
        private int _rollbackCount;

        public int RollbackCount => Volatile.Read(ref _rollbackCount);

        public override async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new RollbackRecordingUnitOfWork(
                await base.BeginAsync(scope, cancellationToken),
                () => Interlocked.Increment(ref _rollbackCount));
    }

    private sealed class RollbackRecordingUnitOfWork(
        IDocumentUnitOfWork inner,
        Action recordRollback) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            recordRollback();
            return inner.RollbackAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class RollbackThrowingDocumentStore(IDocumentStore inner) : DelegatingDocumentStore(inner)
    {
        public override async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new RollbackThrowingUnitOfWork(
                await base.BeginAsync(scope, cancellationToken));
    }

    private sealed class RollbackThrowingUnitOfWork(
        IDocumentUnitOfWork inner) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("rollback failed");

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class RejectingMarkerSaveDocumentStore(IDocumentStore inner) : DelegatingDocumentStore(inner)
    {
        private int _rollbackCount;

        public int RollbackCount => Volatile.Read(ref _rollbackCount);

        public override async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new RejectingMarkerSaveUnitOfWork(
                await base.BeginAsync(scope, cancellationToken),
                () => Interlocked.Increment(ref _rollbackCount));
    }

    private sealed class RejectingMarkerSaveUnitOfWork(
        IDocumentUnitOfWork inner,
        Action recordRollback) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            request.DocumentKind == GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind
                ? Task.FromResult(DocumentStoreWriteResult.NotFound)
                : inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            inner.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            recordRollback();
            return inner.RollbackAsync(cancellationToken);
        }

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class HideFirstLedgerLoadDocumentStore(IDocumentStore inner) : DelegatingDocumentStore(inner)
    {
        private int _ledgerLoads;
        private int _rollbackCount;

        public int RollbackCount => Volatile.Read(ref _rollbackCount);

        public override Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default)
        {
            if (documentKind == GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind &&
                Interlocked.Increment(ref _ledgerLoads) == 1)
                return Task.FromResult<DocumentEnvelope?>(null);

            return base.LoadAsync(documentKind, id, cancellationToken);
        }

        public override async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new RollbackRecordingUnitOfWork(
                await base.BeginAsync(scope, cancellationToken),
                () => Interlocked.Increment(ref _rollbackCount));
    }

    private sealed class UncertainAfterCommitDocumentStore(
        IDocumentStore inner,
        CancellationTokenSource callerCancellation) : DelegatingDocumentStore(inner)
    {
        private int _ledgerLoads;

        public bool ReconciliationUsedFreshToken { get; private set; }

        public override Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default)
        {
            if (documentKind == GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind &&
                Interlocked.Increment(ref _ledgerLoads) > 1)
                ReconciliationUsedFreshToken = !cancellationToken.IsCancellationRequested;

            return base.LoadAsync(documentKind, id, cancellationToken);
        }

        public override async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new UncertainAfterCommitUnitOfWork(
                await base.BeginAsync(scope, cancellationToken),
                callerCancellation);
    }

    private sealed class UncertainAfterCommitUnitOfWork(
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

    private sealed class UncertainWithoutCommitDocumentStore(IDocumentStore inner) : DelegatingDocumentStore(inner)
    {
        public override async Task<IDocumentUnitOfWork> BeginAsync(
            DocumentCommitScope scope,
            CancellationToken cancellationToken = default) =>
            new UncertainWithoutCommitUnitOfWork(
                await base.BeginAsync(scope, cancellationToken));
    }

    private sealed class UncertainWithoutCommitUnitOfWork(
        IDocumentUnitOfWork inner) : IDocumentUnitOfWork
    {
        public Task<DocumentStoreWriteResult> SaveAsync(
            SaveDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.SaveAsync(request, cancellationToken);

        public Task<DocumentStoreWriteResult> DeleteAsync(
            DeleteDocumentRequest request,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(request, cancellationToken);

        public Task<DocumentEnvelope?> LoadAsync(
            string documentKind,
            string id,
            CancellationToken cancellationToken = default) =>
            inner.LoadAsync(documentKind, id, cancellationToken);

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            throw new DocumentCommitAcknowledgementUncertainException(
                [GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind]);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            inner.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }
}
