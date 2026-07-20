using System.Linq.Expressions;
using System.Text.Json;
using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Primitives.Entities;
using Groundwork.Core.Queries;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Generic Groundwork document read store that executes the closed, provider-neutral
/// <see cref="Query{TEntity}"/> spec — the document-store analogue of the relational
/// <c>EFCoreReadStore</c>. It is the implementation behind the named design persistence ports (e.g.
/// <c>IWorkflowDefinitionStore</c>) when the host selects a Groundwork (document) provider, so the same
/// host-selected provider can back every Elsa module.
/// <para>
/// Groundwork's portable document query today supports only <b>equality on a declared index</b> plus
/// offset paging. This store therefore satisfies the richer closed contract (<c>IN</c>, substring
/// <c>Contains</c>, <c>OR</c>-composition, single-field ordering) with the canonical fallback described
/// in the Groundwork closed-query capability spec: pull the candidate set through the by-collection
/// equality index, then apply <see cref="InMemoryQueryEvaluator"/> — whose semantics are identical to
/// the EF Core translator, so swapping providers yields the same result set. A by-id read short-circuits
/// to a direct <see cref="IDocumentStore.LoadAsync"/>. As Groundwork ships native operators, an adapter
/// can push individual clauses down without changing this contract.
/// </para>
/// </summary>
/// <typeparam name="TEntity">The entity type.</typeparam>
public class GroundworkReadStore<TEntity> where TEntity : Entity
{
    private readonly IDocumentStore _store;
    private readonly IBoundedDocumentStore? _boundedStore;
    private readonly string _documentKind;
    private readonly string _collectionQueryIdentity;
    private readonly string _collectionFieldPath;
    private readonly string _collectionValue;
    private readonly IReadOnlyList<DocumentQueryOrder>? _collectionOrder;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly IGroundworkStoreSessionFactory? _sessions;

    /// <param name="store">The provider-neutral document store the host wired to a concrete provider.</param>
    /// <param name="documentKind">The manifest document-kind backing <typeparamref name="TEntity"/>.</param>
    /// <param name="collectionQueryIdentity">The admitted bounded route used to enumerate every document of the kind.</param>
    /// <param name="collectionFieldPath">The constant-partition field constrained by the bounded route.</param>
    /// <param name="collectionValue">The constant partition value stamped on every document of the kind.</param>
    /// <param name="jsonOptions">Serialization settings whose camelCase output matches the declared index field names.</param>
    public GroundworkReadStore(
        IDocumentStore store,
        string documentKind,
        string collectionQueryIdentity,
        string collectionFieldPath,
        string collectionValue,
        JsonSerializerOptions jsonOptions,
        IBoundedDocumentStore? boundedStore = null,
        IGroundworkStoreSessionFactory? sessions = null,
        IReadOnlyList<DocumentQueryOrder>? collectionOrder = null)
    {
        _store = store;
        _boundedStore = boundedStore ?? store as IBoundedDocumentStore;
        _documentKind = documentKind;
        _collectionQueryIdentity = collectionQueryIdentity;
        _collectionFieldPath = collectionFieldPath;
        _collectionValue = collectionValue;
        _collectionOrder = collectionOrder;
        _jsonOptions = jsonOptions;
        _sessions = sessions;
    }

    /// <summary>Executes <paramref name="query"/> and returns every matching entity.</summary>
    public async Task<IReadOnlyList<TEntity>> QueryAsync(Query<TEntity> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return await WithCandidatesAsync(
            query,
            candidates => InMemoryQueryEvaluator.Apply(candidates, query).ToList(),
            cancellationToken);
    }

    /// <summary>Executes <paramref name="query"/> and returns the first matching entity, or <c>null</c>.</summary>
    public async Task<TEntity?> FirstOrDefaultAsync(Query<TEntity> query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        // Fast path: a pure "by id" lookup maps to the document id, so a single point read avoids
        // enumerating the collection. Across-scope access must use an admitted bounded query because
        // a point read has no explicit target partition.
        if (!query.TenantAgnostic && TryGetIdEquality(query, out var id))
        {
            DocumentEnvelope? envelope;
            try
            {
                envelope = await _store.LoadAsync(_documentKind, id, cancellationToken);
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
                throw new GroundworkProviderFailureException(
                    $"Provider point read for document '{id}' of kind '{_documentKind}' failed.",
                    _documentKind,
                    null,
                    id,
                    typeof(TEntity),
                    exception);
            }

            return envelope is null ? null : Deserialize(envelope);
        }

        return await WithCandidatesAsync(
            query,
            candidates => InMemoryQueryEvaluator.Apply(candidates, query).FirstOrDefault(),
            cancellationToken);
    }

    /// <summary>Determines whether any entity matches <paramref name="query"/>.</summary>
    public async Task<bool> AnyAsync(Query<TEntity> query, CancellationToken cancellationToken = default)
        => await FirstOrDefaultAsync(query, cancellationToken) is not null;

    private async Task<TResult> WithCandidatesAsync<TResult>(
        Query<TEntity> query,
        Func<IReadOnlyList<TEntity>, TResult> evaluate,
        CancellationToken cancellationToken)
    {
        if (!query.TenantAgnostic)
            return evaluate(await LoadAllAsync(BoundedStore(), cancellationToken));

        var sessions = _sessions ?? throw new GroundworkQueryReadinessException(
            $"Tenant-agnostic queries for '{_documentKind}' require an authorized Groundwork session factory.",
            _documentKind,
            _collectionQueryIdentity,
            null,
            typeof(TEntity));

        return await sessions.ExecutePrivilegedAcrossScopesAsync(
            async (session, token) =>
            {
                if (session.Access.Kind != DocumentStoreAccessKind.PrivilegedAcrossScopes)
                {
                    throw new GroundworkQueryReadinessException(
                        "Tenant-agnostic persistence queries require explicit privileged across-scope access.",
                        _documentKind,
                        _collectionQueryIdentity,
                        null,
                        typeof(TEntity));
                }

                return evaluate(await LoadAllAsync(session.BoundedDocumentStore, token));
            },
            cancellationToken);
    }

    private async Task<IReadOnlyList<TEntity>> LoadAllAsync(
        IBoundedDocumentStore boundedStore,
        CancellationToken cancellationToken)
    {
        var collectionOrder = CollectionOrder();
        IReadOnlyList<DocumentEnvelope> envelopes;
        try
        {
            envelopes = await BoundedDocumentQueryPager.QueryAllOffsetAsync(
                boundedStore,
                _documentKind,
                _collectionQueryIdentity,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(_collectionFieldPath, _collectionValue))],
                collectionOrder,
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
            throw new GroundworkProviderFailureException(
                $"Provider bounded query '{_collectionQueryIdentity}' for kind '{_documentKind}' failed.",
                _documentKind,
                _collectionQueryIdentity,
                null,
                typeof(TEntity),
                exception);
        }

        return envelopes.Select(Deserialize).ToList();
    }

    private IBoundedDocumentStore BoundedStore() => _boundedStore ?? throw new GroundworkQueryReadinessException(
        $"Queries for '{_documentKind}' require an admitted bounded document-store runtime.",
        _documentKind,
        _collectionQueryIdentity,
        null,
        typeof(TEntity));

    private IReadOnlyList<DocumentQueryOrder> CollectionOrder() => _collectionOrder is { Count: > 0 }
        ? _collectionOrder
        : throw new GroundworkQueryReadinessException(
            $"Queries for '{_documentKind}' require a deterministic declared order for exhaustive offset paging.",
            _documentKind,
            _collectionQueryIdentity,
            null,
            typeof(TEntity));

    private TEntity Deserialize(DocumentEnvelope envelope)
    {
        try
        {
            var document = JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(envelope.ContentJson, _jsonOptions);
            return document?.Entity
                ?? throw new GroundworkCorruptPayloadException(
                    $"Document '{envelope.Id}' of kind '{_documentKind}' could not be deserialized as {typeof(TEntity).Name}.",
                    _documentKind,
                    envelope.Id,
                    typeof(TEntity));
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new GroundworkCorruptPayloadException(
                $"Document '{envelope.Id}' of kind '{_documentKind}' could not be deserialized as {typeof(TEntity).Name}.",
                _documentKind,
                envelope.Id,
                typeof(TEntity),
                exception);
        }
    }

    // A query is a pure by-id read when it is a single equality comparison on the Id field, with no
    // disjunction, no extra clauses and no ordering.
    private static bool TryGetIdEquality(Query<TEntity> query, out string id)
    {
        id = string.Empty;

        if (query.Order != null || query.Clauses.Count != 1)
            return false;

        var clause = query.Clauses[0];
        if (clause.Count != 1)
            return false;

        var comparison = clause[0];
        if (comparison.Operator != QueryOp.Equal || comparison.Value is not string value)
            return false;

        if (!string.Equals(MemberName(comparison.FieldSelector.Body), nameof(Entity.Id), StringComparison.Ordinal))
            return false;

        id = value;
        return true;
    }

    private static string MemberName(Expression body)
    {
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        return body is MemberExpression member ? member.Member.Name : string.Empty;
    }
}
