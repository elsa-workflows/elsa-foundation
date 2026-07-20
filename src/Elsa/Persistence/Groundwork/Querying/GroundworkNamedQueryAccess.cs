using System.Runtime.ExceptionServices;
using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Primitives.Entities;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Opens one access-bound session for a named-query operation and presents its executor to the caller.
/// Production adapters acquire fresh resources through <see cref="IGroundworkStoreSessionFactory"/>; direct
/// stores remain available for already-owned unit-of-work resources and focused adapter tests.
/// </summary>
public sealed class GroundworkNamedQueryAccess<TEntity> where TEntity : Entity
{
    private const string SessionCompletionFailureKey =
        "Elsa.Persistence.Groundwork.SessionCompletionFailure";

    private readonly IDocumentStore _documentStore;
    private readonly IBoundedDocumentStore? _boundedDocumentStore;
    private readonly IGroundworkStoreSessionFactory? _sessions;
    private readonly string _documentKind;
    private readonly JsonSerializerOptions _jsonOptions;

    public GroundworkNamedQueryAccess(
        IDocumentStore documentStore,
        string documentKind,
        JsonSerializerOptions jsonOptions,
        IBoundedDocumentStore? boundedDocumentStore = null,
        IGroundworkStoreSessionFactory? sessions = null)
    {
        _documentStore = documentStore ?? throw new ArgumentNullException(nameof(documentStore));
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        _boundedDocumentStore = boundedDocumentStore
                                ?? documentStore as IBoundedDocumentStore;
        _sessions = sessions;
        _documentKind = documentKind;
        _jsonOptions = jsonOptions;
    }

    /// <summary>
    /// Executes against an ordinary session, or an explicitly audited across-scope session when requested.
    /// </summary>
    public async Task<TResult> ExecuteAsync<TResult>(
        bool acrossScopes,
        Func<GroundworkNamedQueryExecutor<TEntity>, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        cancellationToken.ThrowIfCancellationRequested();

        if (acrossScopes)
        {
            var sessions = _sessions ?? throw new GroundworkQueryReadinessException(
                $"Across-scope named queries for '{_documentKind}' require an authorized Groundwork session factory.",
                _documentKind,
                null,
                null,
                typeof(TEntity));
            return await sessions.ExecutePrivilegedAcrossScopesAsync(
                (session, token) => new ValueTask<TResult>(
                    operation(CreateExecutor(session), token)),
                cancellationToken);
        }

        var session = _sessions is null
            ? new GroundworkStoreSession(
                _documentStore.Access,
                new GroundworkStoreSessionResources(
                    _documentStore,
                    _boundedDocumentStore ?? new UnavailableBoundedDocumentStore(
                        _documentKind,
                        typeof(TEntity))))
            : await _sessions.CreateAsync(PersistenceAccessPolicy.Ordinary, cancellationToken);
        return await ExecuteAndDisposeAsync(session, operation, cancellationToken);
    }

    private async Task<TResult> ExecuteAndDisposeAsync<TResult>(
        GroundworkStoreSession session,
        Func<GroundworkNamedQueryExecutor<TEntity>, CancellationToken, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        TResult result = default!;
        Exception? operationFailure = null;
        try
        {
            result = await operation(CreateExecutor(session), cancellationToken);
        }
        catch (Exception exception)
        {
            operationFailure = exception;
        }

        Exception? completionFailure = null;
        try
        {
            await session.DisposeAsync();
        }
        catch (Exception exception)
        {
            completionFailure = exception;
        }

        if (operationFailure is not null)
        {
            if (completionFailure is not null)
                operationFailure.Data[SessionCompletionFailureKey] = completionFailure;
            ExceptionDispatchInfo.Capture(operationFailure).Throw();
        }

        if (completionFailure is not null)
            ExceptionDispatchInfo.Capture(completionFailure).Throw();

        return result;
    }

    private GroundworkNamedQueryExecutor<TEntity> CreateExecutor(GroundworkStoreSession session) =>
        new(session, _documentKind, _jsonOptions);

    private sealed class UnavailableBoundedDocumentStore(
        string documentKind,
        Type entityType) : IBoundedDocumentStore
    {
        public Task<DocumentQueryResult> QueryAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Missing<DocumentQueryResult>(query.QueryIdentity);

        public Task<long> CountAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Missing<long>(query.QueryIdentity);

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Missing<DocumentEnvelope?>(query.QueryIdentity);

        public Task<bool> AnyAsync(
            DocumentQuery query,
            CancellationToken cancellationToken = default) =>
            Missing<bool>(query.QueryIdentity);

        private Task<TResult> Missing<TResult>(string queryIdentity) =>
            Task.FromException<TResult>(
                new GroundworkQueryReadinessException(
                    $"Named query '{queryIdentity}' for '{documentKind}' requires an admitted bounded document-store runtime.",
                    documentKind,
                    queryIdentity,
                    null,
                    entityType));
    }
}
