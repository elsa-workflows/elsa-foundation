using System.Text.Json;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Primitives.Entities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Executes one entity family's admitted named queries against resources owned by an already-acquired
/// <see cref="GroundworkStoreSession"/>. The executor never acquires or disposes a session, so callers retain
/// one immutable access boundary across related point and bounded reads.
/// </summary>
/// <typeparam name="TEntity">The canonical entity payload stored inside <see cref="GroundworkDocument{TEntity}"/>.</typeparam>
public sealed class GroundworkNamedQueryExecutor<TEntity> where TEntity : Entity
{
    private readonly GroundworkStoreSession _session;
    private readonly string _documentKind;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly GroundworkQueryTranslator<TEntity> _translator;

    public GroundworkNamedQueryExecutor(
        GroundworkStoreSession session,
        string documentKind,
        JsonSerializerOptions jsonOptions)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        ArgumentNullException.ThrowIfNull(jsonOptions);
        _documentKind = documentKind;
        _jsonOptions = jsonOptions;
        _translator = new GroundworkQueryTranslator<TEntity>(jsonOptions);
    }

    /// <summary>
    /// Loads one document by storage identity through this session's access-bound point-read surface.
    /// Across-scope sessions must use an admitted bounded query because a point identity has no target scope.
    /// </summary>
    public async Task<TEntity?> LoadAsync(
        string documentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentId);
        if (_session.Access.Kind == global::Groundwork.Documents.Scoping.DocumentStoreAccessKind.PrivilegedAcrossScopes)
        {
            throw new GroundworkQueryReadinessException(
                $"Across-scope point reads for '{_documentKind}' require an admitted bounded query.",
                _documentKind,
                null,
                documentId,
                typeof(TEntity));
        }

        DocumentEnvelope? envelope;
        try
        {
            envelope = await _session.DocumentStore.LoadAsync(_documentKind, documentId, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw ProviderFailure(
                $"Provider point read for document '{documentId}' of kind '{_documentKind}' failed.",
                null,
                documentId,
                exception);
        }

        return envelope is null ? null : Deserialize(envelope);
    }

    /// <summary>Executes a translated named query using the route's <c>First</c> result operation.</summary>
    public Task<TEntity?> FirstOrDefaultAsync(
        string queryIdentity,
        Query<TEntity> query,
        CancellationToken cancellationToken = default) =>
        FirstOrDefaultCoreAsync(queryIdentity, query, null, cancellationToken);

    /// <summary>
    /// Executes a translated named query using the route's <c>First</c> result operation and sends the
    /// complete declared order after verifying that the Elsa query's requested order is a compatible prefix.
    /// </summary>
    public Task<TEntity?> FirstOrDefaultAsync(
        string queryIdentity,
        Query<TEntity> query,
        IReadOnlyList<DocumentQueryOrder> exactDeclaredOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(exactDeclaredOrder);
        return FirstOrDefaultCoreAsync(queryIdentity, query, exactDeclaredOrder, cancellationToken);
    }

    /// <summary>Executes a translated named query using the route's <c>Any</c> result operation.</summary>
    public async Task<bool> AnyAsync(
        string queryIdentity,
        Query<TEntity> query,
        CancellationToken cancellationToken = default)
    {
        ValidateQueryArguments(queryIdentity, query);
        var translated = _translator.Translate(
            _documentKind,
            queryIdentity,
            query,
            BoundedQueryResultOperation.Any);

        try
        {
            return await _session.BoundedDocumentStore.AnyAsync(translated, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw ProviderFailure(
                $"Provider bounded query '{queryIdentity}' for kind '{_documentKind}' failed.",
                queryIdentity,
                null,
                exception);
        }
    }

    /// <summary>
    /// Exhausts a translated offset-paged <c>Documents</c> route using its complete deterministic declared
    /// order. The Elsa query's requested order, when present, must be an exact prefix of that route order.
    /// </summary>
    public async Task<IReadOnlyList<TEntity>> QueryAsync(
        string queryIdentity,
        Query<TEntity> query,
        IReadOnlyList<DocumentQueryOrder> exactDeclaredOrder,
        CancellationToken cancellationToken = default)
    {
        ValidateQueryArguments(queryIdentity, query);
        ArgumentNullException.ThrowIfNull(exactDeclaredOrder);
        if (exactDeclaredOrder.Count == 0)
        {
            throw new GroundworkQueryReadinessException(
                $"Bounded query '{queryIdentity}' for '{_documentKind}' requires a deterministic declared order for exhaustive offset paging.",
                _documentKind,
                queryIdentity,
                null,
                typeof(TEntity));
        }

        var translated = _translator.Translate(
            _documentKind,
            queryIdentity,
            query,
            BoundedQueryResultOperation.Documents,
            take: ElsaGroundworkQueryRoutes.MaximumResultCount);
        ValidateOrderPrefix(queryIdentity, translated.Order, exactDeclaredOrder);

        IReadOnlyList<DocumentEnvelope> envelopes;
        try
        {
            envelopes = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
                _session.BoundedDocumentStore,
                _documentKind,
                queryIdentity,
                translated.Clauses,
                exactDeclaredOrder,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw ProviderFailure(
                $"Provider bounded query '{queryIdentity}' for kind '{_documentKind}' failed.",
                queryIdentity,
                null,
                exception);
        }

        return envelopes.Select(Deserialize).ToArray();
    }

    private async Task<TEntity?> FirstOrDefaultCoreAsync(
        string queryIdentity,
        Query<TEntity> query,
        IReadOnlyList<DocumentQueryOrder>? exactDeclaredOrder,
        CancellationToken cancellationToken)
    {
        ValidateQueryArguments(queryIdentity, query);
        var translated = _translator.Translate(
            _documentKind,
            queryIdentity,
            query,
            BoundedQueryResultOperation.First);
        if (exactDeclaredOrder is not null)
        {
            ValidateOrderPrefix(queryIdentity, translated.Order, exactDeclaredOrder);
            translated = new DocumentQuery(
                _documentKind,
                queryIdentity,
                translated.Clauses,
                exactDeclaredOrder,
                resultOperation: BoundedQueryResultOperation.First);
        }

        DocumentEnvelope? envelope;
        try
        {
            envelope = await _session.BoundedDocumentStore.FirstOrDefaultAsync(translated, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw ProviderFailure(
                $"Provider bounded query '{queryIdentity}' for kind '{_documentKind}' failed.",
                queryIdentity,
                null,
                exception);
        }

        return envelope is null ? null : Deserialize(envelope);
    }

    private static void ValidateQueryArguments(string queryIdentity, Query<TEntity> query)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryIdentity);
        ArgumentNullException.ThrowIfNull(query);
    }

    private void ValidateOrderPrefix(
        string queryIdentity,
        IReadOnlyList<DocumentQueryOrder> requested,
        IReadOnlyList<DocumentQueryOrder> declared)
    {
        if (requested.Count <= declared.Count &&
            requested.Select((order, index) => order == declared[index]).All(matches => matches))
        {
            return;
        }

        throw new GroundworkQueryTranslationException(
            $"The requested order is not a prefix of the exact declared order for bounded query '{queryIdentity}'.",
            _documentKind,
            queryIdentity,
            typeof(TEntity));
    }

    private TEntity Deserialize(DocumentEnvelope envelope)
    {
        try
        {
            var document = JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(
                envelope.ContentJson,
                _jsonOptions);
            return document?.Entity
                ?? throw CorruptPayload(envelope.Id);
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw CorruptPayload(envelope.Id, exception);
        }
    }

    private GroundworkCorruptPayloadException CorruptPayload(
        string documentId,
        Exception? innerException = null) =>
        new(
            $"Document '{documentId}' of kind '{_documentKind}' could not be deserialized as {typeof(TEntity).Name}.",
            _documentKind,
            documentId,
            typeof(TEntity),
            innerException);

    private GroundworkProviderFailureException ProviderFailure(
        string message,
        string? queryIdentity,
        string? documentId,
        Exception exception) =>
        new(
            message,
            _documentKind,
            queryIdentity,
            documentId,
            typeof(TEntity),
            exception);
}
