using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Persistence.Core.Queries;
using Elsa.Primitives.Entities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Translates Elsa's closed query contract into one named, bounded Groundwork document query.
/// </summary>
public sealed class GroundworkQueryTranslator<TEntity> where TEntity : Entity
{
    private const string EntityPath = "entity";
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly JsonTypeInfo _entityTypeInfo;

    public GroundworkQueryTranslator(JsonSerializerOptions jsonOptions)
    {
        ArgumentNullException.ThrowIfNull(jsonOptions);
        _jsonOptions = new JsonSerializerOptions(jsonOptions);
        _jsonOptions.TypeInfoResolver ??= new DefaultJsonTypeInfoResolver();
        _jsonOptions.MakeReadOnly();
        _entityTypeInfo = _jsonOptions.GetTypeInfo(typeof(TEntity));
    }

    public DocumentQuery Translate(
        string documentKind,
        string queryIdentity,
        Query<TEntity> query,
        BoundedQueryResultOperation resultOperation = BoundedQueryResultOperation.Documents,
        int? skip = null,
        int? take = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ValidateResultShape(documentKind, queryIdentity, resultOperation, take);

        var clauses = query.Clauses
            .Select(clause => TranslateClause(clause, documentKind, queryIdentity))
            .ToArray();
        var order = query.Order is null
            ? Array.Empty<DocumentQueryOrder>()
            : [new DocumentQueryOrder(
                ResolvePath(query.Order.FieldSelector, documentKind, queryIdentity),
                query.Order.Direction == Elsa.Primitives.Persistence.OrderDirection.Descending
                    ? PhysicalSortDirection.Descending
                    : PhysicalSortDirection.Ascending)];

        return new DocumentQuery(
            documentKind,
            queryIdentity,
            clauses,
            order,
            skip,
            take,
            resultOperation: resultOperation);
    }

    private static void ValidateResultShape(
        string documentKind,
        string queryIdentity,
        BoundedQueryResultOperation resultOperation,
        int? take)
    {
        if (!Enum.IsDefined(resultOperation))
        {
            throw new GroundworkQueryTranslationException(
                $"Query result operation '{resultOperation}' is not supported by the Groundwork translator.",
                documentKind,
                queryIdentity,
                typeof(TEntity));
        }

        if (resultOperation != BoundedQueryResultOperation.Documents)
            return;

        if (take is not > 0)
        {
            throw new GroundworkQueryTranslationException(
                "Document query results require a positive take bound before they can be sent to a provider.",
                documentKind,
                queryIdentity,
                typeof(TEntity));
        }
    }

    private DocumentQueryClause TranslateClause(
        IReadOnlyList<QueryComparison<TEntity>> clause,
        string documentKind,
        string queryIdentity)
    {
        if (clause.Count == 0)
            return DocumentQueryClause.MatchNone;

        var comparisons = clause
            .Select(comparison => TranslateComparison(comparison, documentKind, queryIdentity))
            .Where(comparison => comparison is not null)
            .Cast<DocumentQueryComparison>()
            .ToArray();
        return comparisons.Length == 0
            ? DocumentQueryClause.MatchNone
            : new DocumentQueryClause(comparisons);
    }

    private DocumentQueryComparison? TranslateComparison(
        QueryComparison<TEntity> comparison,
        string documentKind,
        string queryIdentity)
    {
        var path = ResolvePath(comparison.FieldSelector, documentKind, queryIdentity);
        return comparison.Operator switch
        {
            QueryOp.Equal => DocumentQueryComparison.Equal(path, SerializeScalar(comparison.Value, comparison, documentKind, queryIdentity)),
            QueryOp.In => TranslateMembership(path, comparison, documentKind, queryIdentity),
            QueryOp.Contains => TranslateContains(path, comparison, documentKind, queryIdentity),
            _ => throw Failure(
                comparison,
                $"Query operator '{comparison.Operator}' is not supported by the Groundwork translator.",
                documentKind,
                queryIdentity)
        };
    }

    private DocumentQueryComparison? TranslateMembership(
        string path,
        QueryComparison<TEntity> comparison,
        string documentKind,
        string queryIdentity)
    {
        if (comparison.Value is string || comparison.Value is not IEnumerable values)
        {
            throw Failure(
                comparison,
                $"Query operator '{QueryOp.In}' requires a set value.",
                documentKind,
                queryIdentity);
        }

        var serialized = values.Cast<object?>()
            .Select(value => SerializeScalar(value, comparison, documentKind, queryIdentity))
            .ToArray();
        return serialized.Length == 0
            ? null
            : DocumentQueryComparison.In(path, serialized);
    }

    private DocumentQueryComparison TranslateContains(
        string path,
        QueryComparison<TEntity> comparison,
        string documentKind,
        string queryIdentity)
    {
        if (comparison.Value is not string value)
        {
            throw Failure(
                comparison,
                $"Query operator '{QueryOp.Contains}' requires a non-null string value.",
                documentKind,
                queryIdentity);
        }

        return DocumentQueryComparison.Contains(path, value);
    }

    private string ResolvePath(
        LambdaExpression selector,
        string documentKind,
        string queryIdentity)
    {
        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            body = unary.Operand;

        if (body is not MemberExpression { Expression: ParameterExpression } member ||
            member.Member is not PropertyInfo property)
        {
            throw new GroundworkQueryTranslationException(
                $"The Groundwork query translator requires a direct property member selector; got '{body}'.",
                documentKind,
                queryIdentity,
                typeof(TEntity));
        }

        return ResolvePath(property, documentKind, queryIdentity);
    }

    private string ResolvePath(
        PropertyInfo property,
        string documentKind,
        string queryIdentity)
    {
        var jsonProperty = _entityTypeInfo.Properties.SingleOrDefault(candidate =>
            candidate.AttributeProvider is PropertyInfo candidateProperty && candidateProperty == property);
        if (jsonProperty is null)
        {
            throw new GroundworkQueryTranslationException(
                $"Property '{property.DeclaringType?.FullName}.{property.Name}' is excluded from canonical JSON and cannot be queried.",
                documentKind,
                queryIdentity,
                typeof(TEntity));
        }

        var serializationStatus = GroundworkDocumentSerialization.GetPropertySerializationStatus(
            _jsonOptions,
            jsonProperty);
        if (serializationStatus == GroundworkDocumentSerialization.PropertySerializationStatus.Excluded)
        {
            throw new GroundworkQueryTranslationException(
                $"Property '{property.DeclaringType?.FullName}.{property.Name}' is excluded from canonical JSON and cannot be queried.",
                documentKind,
                queryIdentity,
                typeof(TEntity));
        }

        if (serializationStatus == GroundworkDocumentSerialization.PropertySerializationStatus.Conditional)
        {
            throw new GroundworkQueryTranslationException(
                $"Property '{property.DeclaringType?.FullName}.{property.Name}' does not have stable presence in canonical JSON and cannot be queried.",
                documentKind,
                queryIdentity,
                typeof(TEntity));
        }

        var serializedName = jsonProperty.Name;
        if (string.IsNullOrWhiteSpace(serializedName))
        {
            throw new GroundworkQueryTranslationException(
                $"Property '{property.DeclaringType?.FullName}.{property.Name}' has no stable serialized name.",
                documentKind,
                queryIdentity,
                typeof(TEntity));
        }

        return $"{EntityPath}.{serializedName}";
    }

    private string? SerializeScalar(
        object? value,
        QueryComparison<TEntity> comparison,
        string documentKind,
        string queryIdentity)
    {
        if (value is null)
            return null;

        try
        {
            var element = JsonSerializer.SerializeToElement(value, value.GetType(), _jsonOptions);
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetRawText(),
                JsonValueKind.True => bool.TrueString.ToLowerInvariant(),
                JsonValueKind.False => bool.FalseString.ToLowerInvariant(),
                JsonValueKind.Null => null,
                _ => throw Failure(
                    comparison,
                    $"Value type '{value.GetType().FullName}' is not a scalar JSON query value.",
                    documentKind,
                    queryIdentity)
            };
        }
        catch (GroundworkQueryException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new GroundworkQueryTranslationException(
                $"Value for property '{comparison.FieldName}' could not be serialized as a stable query scalar.",
                documentKind,
                queryIdentity,
                typeof(TEntity),
                exception);
        }
    }

    private static GroundworkQueryTranslationException Failure(
        QueryComparison<TEntity> comparison,
        string message,
        string documentKind,
        string queryIdentity) =>
        new($"{message} Property: '{comparison.FieldName}'.", documentKind, queryIdentity, typeof(TEntity));
}
