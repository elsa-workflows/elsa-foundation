using System.Globalization;
using System.Text.Json;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Queries;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

/// <summary>
/// Proves the exactly-once mechanics of <see cref="OpenIddictGroundworkAtomicWrite"/> (spec 106 T030): a
/// first attempt commits its staged mutation alongside a durable mutation receipt; every subsequent replay
/// of the same logical operation reads the receipt back and returns the ORIGINAL outcome instead of
/// re-running the caller's staging delegate. Uses a small self-contained in-memory <see cref="IDocumentStore"/>
/// double (see <see cref="FakeUnitOfWorkDocumentStore"/>) rather than the shared
/// <c>Elsa.Persistence.Groundwork.Testing</c> library: that library's csproj references
/// <c>Testcontainers.MongoDb</c>/<c>Testcontainers.MsSql</c>/<c>Testcontainers.PostgreSql</c>, and a
/// <c>ProjectReference</c> to it would pull those packages into this test project's build graph even though
/// this project's own csproj text stays free of the literal "Testcontainers" the CI fast-gate filter greps
/// for.
/// </summary>
public sealed class OpenIddictGroundworkAtomicWriteTests
{
    private const string TargetDocumentKind = "test-target-document";

    [Fact]
    public async Task First_execution_commits_the_staged_mutation_and_records_a_mutation_receipt()
    {
        var store = new FakeUnitOfWorkDocumentStore();
        var atomicWrite = AtomicWrite(store);
        var mutation = Mutation("redeem-token", "token-a");
        var executions = 0;

        var result = await atomicWrite.ExecuteAsync(
            mutation,
            (unitOfWork, cancellationToken) =>
            {
                executions++;
                return unitOfWork.SaveAsync(TargetRequest("token-a", "redeemed"), cancellationToken);
            },
            CancellationToken.None);

        Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status);
        Assert.Equal(1, executions);
        Assert.NotNull(await store.LoadAsync(TargetDocumentKind, "token-a", CancellationToken.None));

        var receiptEnvelope = await store.LoadAsync(
            OpenIddictGroundworkJson.MutationReceiptDocumentKind,
            mutation.MutationReceiptId,
            CancellationToken.None);
        Assert.NotNull(receiptEnvelope);
        using var receiptJson = JsonDocument.Parse(receiptEnvelope!.ContentJson);
        Assert.Equal(mutation.MutationReceiptId, receiptJson.RootElement.GetProperty("mutationReceiptId").GetString());
        Assert.Equal(mutation.OperationIdentity.Value, receiptJson.RootElement.GetProperty("operationIdentity").GetString());
        Assert.True(
            receiptJson.RootElement.GetProperty("createdAt").GetDateTimeOffset() <
            receiptJson.RootElement.GetProperty("expiresAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Replay_with_the_same_fingerprint_returns_the_original_outcome_without_re_executing_the_staged_mutation()
    {
        var store = new FakeUnitOfWorkDocumentStore();
        var atomicWrite = AtomicWrite(store);
        var mutation = Mutation("redeem-token", "token-b");
        var executions = 0;
        Task<DocumentStoreWriteResult> Stage(IDocumentUnitOfWork unitOfWork, CancellationToken cancellationToken)
        {
            executions++;
            return unitOfWork.SaveAsync(TargetRequest("token-b", "redeemed"), cancellationToken);
        }

        var first = await atomicWrite.ExecuteAsync(mutation, Stage, CancellationToken.None);
        var replay = await atomicWrite.ExecuteAsync(mutation, Stage, CancellationToken.None);

        // This is the exactly-once property: two ExecuteAsync calls for the same mutation, but the staging
        // delegate only ever ran once. The replay's result is byte-for-byte the receipt-derived outcome of
        // the first attempt, not a second execution against the (already-mutated) target document.
        Assert.Equal(1, executions);
        Assert.Equal(first, replay);
    }

    [Fact]
    public async Task Concurrent_attempts_for_the_same_mutation_converge_on_the_one_committed_receipt()
    {
        const int concurrency = 12;
        var store = new FakeUnitOfWorkDocumentStore
        {
            LoadBarrier = new AsyncBarrier(concurrency),
            BeginBarrier = new AsyncBarrier(concurrency)
        };
        var atomicWrite = AtomicWrite(store);
        var mutation = Mutation("redeem-token", "token-race");

        // Two rendezvous points are needed, not one: the first forces every attempt's initial "is there
        // already a receipt?" read to happen before any of them proceeds (so all `concurrency` attempts
        // see no receipt yet), and the second forces every attempt to have reached BeginAsync - i.e. to have
        // committed to attempting - before any of them is allowed to actually stage and save. Without the
        // second gate, the single fastest attempt can race straight through commit before the others even
        // resume past the first barrier, so they would find its already-committed receipt on their own
        // pre-check read instead of ever reaching the receipt-save step that raises ReceiptRaceException.
        var results = await Task.WhenAll(Enumerable.Range(0, concurrency).Select(_ =>
            atomicWrite.ExecuteAsync(
                mutation,
                (unitOfWork, cancellationToken) => unitOfWork.SaveAsync(TargetRequest("token-race", "redeemed"), cancellationToken),
                CancellationToken.None).AsTask()));

        // Every attempt observes the same authoritative outcome: the receipt race is handled internally
        // (as a ReceiptRaceException reconciled against the winning receipt) rather than surfacing to the
        // caller as a raw concurrency-conflict result or an unhandled exception.
        Assert.All(results, result => Assert.Equal(DocumentStoreWriteStatus.Saved, result.Status));
        Assert.All(results, result => Assert.Equal(results[0].Document?.ContentJson, result.Document?.ContentJson));
        Assert.True(store.ReceiptConcurrencyConflicts > 0, "The race must actually contend on the receipt save.");

        var receiptEnvelope = await store.LoadAsync(
            OpenIddictGroundworkJson.MutationReceiptDocumentKind,
            mutation.MutationReceiptId,
            CancellationToken.None);
        Assert.NotNull(receiptEnvelope);
        Assert.Equal(1, receiptEnvelope!.Version);
    }

    [Fact]
    public async Task Failed_staged_mutation_writes_no_receipt()
    {
        var store = new FakeUnitOfWorkDocumentStore();
        var atomicWrite = AtomicWrite(store);
        var mutation = Mutation("redeem-token", "token-conflict");
        await store.SaveAsync(TargetRequest("token-conflict", "existing"), CancellationToken.None);

        var result = await atomicWrite.ExecuteAsync(
            mutation,
            (unitOfWork, cancellationToken) =>
                unitOfWork.SaveAsync(TargetRequest("token-conflict", "redeemed") with { ExpectedVersion = 0 }, cancellationToken),
            CancellationToken.None);

        Assert.Equal(DocumentStoreWriteStatus.ConcurrencyConflict, result.Status);
        Assert.Null(await store.LoadAsync(
            OpenIddictGroundworkJson.MutationReceiptDocumentKind,
            mutation.MutationReceiptId,
            CancellationToken.None));
    }

    [Fact]
    public async Task Expired_receipts_are_cleaned_up_but_unexpired_receipts_are_not()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 8, 4, 10, 0, 0, TimeSpan.Zero));
        var store = new FakeUnitOfWorkDocumentStore();
        var atomicWrite = AtomicWrite(store, time, TimeSpan.FromHours(1));

        var expiring = new[]
        {
            Mutation("redeem-token", "token-expiring-1"),
            Mutation("redeem-token", "token-expiring-2"),
            Mutation("redeem-token", "token-expiring-3")
        };
        foreach (var mutation in expiring)
        {
            await atomicWrite.ExecuteAsync(
                mutation,
                (unitOfWork, cancellationToken) => unitOfWork.SaveAsync(TargetRequest(mutation.OperationIdentity.Value, "redeemed"), cancellationToken),
                CancellationToken.None);
        }

        time.Advance(TimeSpan.FromHours(2));
        var surviving = Mutation("redeem-token", "token-surviving");
        await atomicWrite.ExecuteAsync(
            surviving,
            (unitOfWork, cancellationToken) => unitOfWork.SaveAsync(TargetRequest(surviving.OperationIdentity.Value, "redeemed"), cancellationToken),
            CancellationToken.None);

        foreach (var mutation in expiring)
        {
            Assert.Null(await store.LoadAsync(
                OpenIddictGroundworkJson.MutationReceiptDocumentKind,
                mutation.MutationReceiptId,
                CancellationToken.None));
        }

        Assert.NotNull(await store.LoadAsync(
            OpenIddictGroundworkJson.MutationReceiptDocumentKind,
            surviving.MutationReceiptId,
            CancellationToken.None));
        Assert.Equal(3, store.DeleteCount);
    }

    [Fact]
    public void Commit_scope_rejects_a_caller_declared_receipt_kind()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            OpenIddictGroundworkAtomicMutation.Create(
                "redeem-token",
                ["token-a"],
                TargetDocumentKind,
                OpenIddictGroundworkJson.MutationReceiptDocumentKind));

        Assert.Contains("must not be declared by the caller", exception.Message, StringComparison.Ordinal);
    }

    private static OpenIddictGroundworkAtomicMutation Mutation(string operationName, string fingerprintComponent) =>
        OpenIddictGroundworkAtomicMutation.Create(operationName, [fingerprintComponent], TargetDocumentKind);

    private static SaveDocumentRequest TargetRequest(string id, string status) =>
        new(TargetDocumentKind, id, "v1", $$"""{"status":"{{status}}"}""");

    /// <summary>
    /// Builds a real <see cref="OpenIddictGroundworkStoreSessionFactory"/> (the production readiness-gating
    /// wrapper every OpenIddict store session goes through) over the given fake document store, so these
    /// tests exercise <see cref="OpenIddictGroundworkAtomicWrite"/> exactly as production wiring would rather
    /// than against a bespoke seam. The underlying <see cref="IGroundworkStoreSessionFactory"/> is a minimal
    /// stub that always hands back the same fake store wrapped in a fresh <see cref="GroundworkStoreSession"/>.
    /// </summary>
    private static OpenIddictGroundworkAtomicWrite AtomicWrite(
        FakeUnitOfWorkDocumentStore store,
        TimeProvider? timeProvider = null,
        TimeSpan? receiptLifetime = null)
    {
        var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySetAdmitted(
            (_, _) => throw new InvalidOperationException("Admission callback is not used by OpenIddictGroundworkStoreSessionFactory."),
            TransactionBoundary.CrossUnitAtomic));
        var sessionFactory = new OpenIddictGroundworkStoreSessionFactory(source, new StubGroundworkStoreSessionFactory(store));
        return new OpenIddictGroundworkAtomicWrite(sessionFactory, timeProvider, receiptLifetime: receiptLifetime);
    }

    private sealed class StubGroundworkStoreSessionFactory(FakeUnitOfWorkDocumentStore store) : IGroundworkStoreSessionFactory
    {
        public ValueTask<GroundworkStoreSession> CreateAsync(
            PersistenceAccessPolicy requiredPolicy,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("OpenIddict must not request a scoped or privileged session.");

        public ValueTask<GroundworkStoreSession> CreateOrdinaryGlobalAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new GroundworkStoreSession(
                DocumentStoreAccess.Global,
                new GroundworkStoreSessionResources(store, store)));
        }

        public ValueTask<TResult> ExecutePrivilegedAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the atomic-write suite.");

        public ValueTask<TResult> ExecutePrivilegedAcrossScopesAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the atomic-write suite.");
    }

    /// <summary>
    /// A minimal in-memory <see cref="IDocumentStore"/>/<see cref="IBoundedDocumentStore"/> double with
    /// working unit-of-work commit/rollback semantics (stage-then-apply, dispose-without-commit rolls back)
    /// and a single-slot commit gate so concurrent <c>BeginAsync</c> callers serialize the same way a real
    /// cross-document-atomic provider would. Only the query shape
    /// <see cref="OpenIddictGroundworkAtomicWrite"/>'s expired-receipt cleanup actually issues (one
    /// less-than-or-equal clause, ascending order, a bounded take) is implemented; every other bounded/legacy
    /// query member is not exercised by this suite and throws.
    /// </summary>
    private sealed class FakeUnitOfWorkDocumentStore : IDocumentStore, IBoundedDocumentStore
    {
        private readonly Dictionary<(string Kind, string Id), DocumentEnvelope> _documents = new();
        private readonly Lock _gate = new();
        private readonly SemaphoreSlim _unitOfWorkGate = new(1, 1);

        public int DeleteCount { get; private set; }
        public int ReceiptConcurrencyConflicts { get; private set; }
        public AsyncBarrier? LoadBarrier { get; set; }
        public AsyncBarrier? BeginBarrier { get; set; }

        public DocumentStoreAccess Access => DocumentStoreAccess.Global;
        public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                return Task.FromResult(SaveLocked(request));
        }

        private DocumentStoreWriteResult SaveLocked(SaveDocumentRequest request)
        {
            var key = (request.DocumentKind, request.Id);
            _documents.TryGetValue(key, out var existing);
            if (EvaluateSaveExpectedVersion(existing, request.ExpectedVersion) is { } failure)
            {
                if (request.DocumentKind == OpenIddictGroundworkJson.MutationReceiptDocumentKind &&
                    failure.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                    ReceiptConcurrencyConflicts++;
                return failure;
            }

            var now = DateTimeOffset.UtcNow;
            var envelope = new DocumentEnvelope(
                request.DocumentKind,
                request.Id,
                request.SchemaVersion,
                (existing?.Version ?? 0) + 1,
                request.ContentJson,
                existing?.CreatedAt ?? now,
                now);
            _documents[key] = envelope;
            return DocumentStoreWriteResult.Saved(envelope);
        }

        public async Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
        {
            if (LoadBarrier is not null && documentKind == OpenIddictGroundworkJson.MutationReceiptDocumentKind)
                await LoadBarrier.SignalAndWaitAsync(cancellationToken);

            lock (_gate)
                return _documents.TryGetValue((documentKind, id), out var envelope) ? envelope : null;
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var key = (request.DocumentKind, request.Id);
                if (!_documents.TryGetValue(key, out var existing))
                    return Task.FromResult(DocumentStoreWriteResult.NotFound);
                if (request.ExpectedVersion is { } expected && existing.Version != expected)
                    return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);

                DeleteCount++;
                _documents.Remove(key);
                return Task.FromResult(DocumentStoreWriteResult.Deleted(existing.Id));
            }
        }

        private static DocumentStoreWriteResult? EvaluateSaveExpectedVersion(DocumentEnvelope? existing, long? expectedVersion)
        {
            if (expectedVersion is not { } expected)
                return null;
            if (existing is null)
                return expected == 0 ? null : DocumentStoreWriteResult.NotFound;
            return existing.Version == expected ? null : DocumentStoreWriteResult.ConcurrencyConflict;
        }

#pragma warning disable CS0618 // Legacy portable surface: required by the interface, not exercised here.
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the atomic-write suite.");

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the atomic-write suite.");

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the atomic-write suite.");

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the atomic-write suite.");
#pragma warning restore CS0618

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var comparison = query.Clauses.Single().Comparisons.Single();
                var cutoff = DateTimeOffset.Parse(comparison.Values.Single()!, CultureInfo.InvariantCulture);
                var matches = _documents.Values
                    .Where(document => document.DocumentKind == query.DocumentKind)
                    .Where(document => ReadExpiresAt(document) <= cutoff)
                    .OrderBy(ReadExpiresAt)
                    .ThenBy(document => document.Id, StringComparer.Ordinal)
                    .ToArray();
                IReadOnlyList<DocumentEnvelope> window = query.Take is { } take
                    ? matches.Take(take).ToArray()
                    : matches;
                return Task.FromResult(new DocumentQueryResult(window, matches.Length));
            }
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the atomic-write suite.");

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the atomic-write suite.");

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the atomic-write suite.");

        public async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default)
        {
            if (BeginBarrier is not null)
                await BeginBarrier.SignalAndWaitAsync(cancellationToken);

            await _unitOfWorkGate.WaitAsync(cancellationToken);
            return new FakeUnitOfWork(this);
        }

        private static DateTimeOffset ReadExpiresAt(DocumentEnvelope envelope)
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            return document.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        }

        private sealed class FakeUnitOfWork(FakeUnitOfWorkDocumentStore store) : IDocumentUnitOfWork
        {
            private readonly Dictionary<(string Kind, string Id), DocumentEnvelope?> _staged = new();
            private readonly List<Func<Task>> _pending = new();
            private bool _completed;

            public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
            {
                EnsureActive();
                var key = (request.DocumentKind, request.Id);
                var existing = ResolveCurrent(key);
                if (EvaluateSaveExpectedVersion(existing, request.ExpectedVersion) is { } failure)
                {
                    if (request.DocumentKind == OpenIddictGroundworkJson.MutationReceiptDocumentKind &&
                        failure.Status == DocumentStoreWriteStatus.ConcurrencyConflict)
                        store.ReceiptConcurrencyConflicts++;
                    return Task.FromResult(failure);
                }

                var now = DateTimeOffset.UtcNow;
                var envelope = new DocumentEnvelope(
                    request.DocumentKind,
                    request.Id,
                    request.SchemaVersion,
                    (existing?.Version ?? 0) + 1,
                    request.ContentJson,
                    existing?.CreatedAt ?? now,
                    now);
                _staged[key] = envelope;
                _pending.Add(() => store.SaveAsync(request, cancellationToken));
                return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
            }

            public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
            {
                EnsureActive();
                var key = (request.DocumentKind, request.Id);
                var existing = ResolveCurrent(key);
                if (existing is null)
                    return Task.FromResult(DocumentStoreWriteResult.NotFound);
                if (request.ExpectedVersion is { } expected && existing.Version != expected)
                    return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);

                _staged[key] = null;
                _pending.Add(() => store.DeleteAsync(request, cancellationToken));
                return Task.FromResult(DocumentStoreWriteResult.Deleted(existing.Id));
            }

            public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
            {
                EnsureActive();
                return Task.FromResult(ResolveCurrent((documentKind, id)));
            }

            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                EnsureActive();
                foreach (var operation in _pending)
                    await operation();
                _completed = true;
            }

            public Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                EnsureActive();
                _pending.Clear();
                _staged.Clear();
                _completed = true;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                // Dispose-without-commit rolls back: staged operations are simply dropped.
                _completed = true;
                store._unitOfWorkGate.Release();
                return ValueTask.CompletedTask;
            }

            private DocumentEnvelope? ResolveCurrent((string Kind, string Id) key) =>
                _staged.TryGetValue(key, out var staged) ? staged : store._documents.GetValueOrDefault(key);

            private void EnsureActive()
            {
                if (_completed)
                    throw new InvalidOperationException("The unit of work has already completed.");
            }
        }
    }

    /// <summary>
    /// Rendezvous point for exactly <paramref name="participants"/> concurrent callers. Every caller,
    /// including the one whose signal completes the barrier, genuinely suspends via <see cref="Task.Yield"/>
    /// before returning: without that, the completing caller would observe its own already-signalled
    /// <see cref="TaskCompletionSource"/> as synchronously complete and race straight through the barrier
    /// alone (checked empirically - the very bug this comment is warning against), defeating the "all
    /// participants observe each other before any proceeds" guarantee the barrier exists to provide.
    /// </summary>
    private sealed class AsyncBarrier(int participants)
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _remaining = participants;

        public async Task SignalAndWaitAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Decrement(ref _remaining) == 0)
            {
                await Task.Yield();
                _released.TrySetResult();
                return;
            }

            await _released.Task.WaitAsync(cancellationToken);
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
