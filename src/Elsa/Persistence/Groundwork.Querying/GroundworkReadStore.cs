using System.Linq.Expressions;
using System.Text.Json;
using Elsa.Persistence.Core.Queries;
using Elsa.Primitives.Entities;
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
    private readonly string _documentKind;
    private readonly string _collectionIndexName;
    private readonly string _collectionValue;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <param name="store">The provider-neutral document store the host wired to a concrete provider.</param>
    /// <param name="documentKind">The manifest document-kind backing <typeparamref name="TEntity"/>.</param>
    /// <param name="collectionIndexName">The by-collection keyword index used to enumerate every document of the kind.</param>
    /// <param name="collectionValue">The constant partition value stamped on every document of the kind.</param>
    /// <param name="jsonOptions">Serialization settings whose camelCase output matches the declared index field names.</param>
    public GroundworkReadStore(
        IDocumentStore store,
        string documentKind,
        string collectionIndexName,
        string collectionValue,
        JsonSerializerOptions jsonOptions)
    {
        _store = store;
        _documentKind = documentKind;
        _collectionIndexName = collectionIndexName;
        _collectionValue = collectionValue;
        _jsonOptions = jsonOptions;
    }

    /// <summary>Executes <paramref name="query"/> and returns every matching entity.</summary>
    public async Task<IReadOnlyList<TEntity>> QueryAsync(Query<TEntity> query, CancellationToken cancellationToken = default)
    {
        var candidates = await LoadAllAsync(cancellationToken);
        return InMemoryQueryEvaluator.Apply(candidates, query).ToList();
    }

    /// <summary>Executes <paramref name="query"/> and returns the first matching entity, or <c>null</c>.</summary>
    public async Task<TEntity?> FirstOrDefaultAsync(Query<TEntity> query, CancellationToken cancellationToken = default)
    {
        // Fast path: a pure "by id" lookup maps to the document id, so a single point read avoids
        // enumerating the collection.
        if (TryGetIdEquality(query, out var id))
        {
            var envelope = await _store.LoadAsync(_documentKind, id, cancellationToken);
            return envelope is null ? null : Deserialize(envelope);
        }

        var candidates = await LoadAllAsync(cancellationToken);
        return InMemoryQueryEvaluator.Apply(candidates, query).FirstOrDefault();
    }

    /// <summary>Determines whether any entity matches <paramref name="query"/>.</summary>
    public async Task<bool> AnyAsync(Query<TEntity> query, CancellationToken cancellationToken = default)
        => await FirstOrDefaultAsync(query, cancellationToken) is not null;

    private async Task<IReadOnlyList<TEntity>> LoadAllAsync(CancellationToken cancellationToken)
    {
        var envelopes = await _store.QueryAsync(
            new DocumentStoreQuery(_documentKind, _collectionIndexName, _collectionValue),
            cancellationToken);

        return envelopes.Select(Deserialize).ToList();
    }

    private TEntity Deserialize(DocumentEnvelope envelope)
    {
        var document = JsonSerializer.Deserialize<GroundworkDocument<TEntity>>(envelope.ContentJson, _jsonOptions);
        return document?.Entity
            ?? throw new InvalidOperationException($"Document '{envelope.Id}' of kind '{_documentKind}' could not be deserialized as {typeof(TEntity).Name}.");
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
