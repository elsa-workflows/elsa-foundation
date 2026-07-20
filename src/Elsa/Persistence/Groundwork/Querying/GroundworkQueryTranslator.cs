using System.Collections;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        ValidateResultShape(query, resultOperation, take);

        var clauses = query.Clauses.Select(TranslateClause).ToArray();
        var order = query.Order is null
            ? Array.Empty<DocumentQueryOrder>()
            : [new DocumentQueryOrder(
                ResolvePath(query.Order.FieldSelector),
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
        Query<TEntity> query,
        BoundedQueryResultOperation resultOperation,
        int? take)
    {
        if (!Enum.IsDefined(resultOperation))
        {
            throw new GroundworkQueryTranslationException(
                $"Query result operation '{resultOperation}' is not supported by the Groundwork translator.");
        }

        if (resultOperation != BoundedQueryResultOperation.Documents)
            return;

        if (take is not > 0)
        {
            throw new GroundworkQueryTranslationException(
                "Document query results require a positive take bound before they can be sent to a provider.");
        }

        if (query.Order is null)
        {
            throw new GroundworkQueryTranslationException(
                "Document query results require a deterministic order before they can be sent to a provider.");
        }
    }

    private DocumentQueryClause TranslateClause(IReadOnlyList<QueryComparison<TEntity>> clause)
    {
        if (clause.Count == 0)
            return DocumentQueryClause.MatchNone;

        var comparisons = clause
            .Select(TranslateComparison)
            .Where(comparison => comparison is not null)
            .Cast<DocumentQueryComparison>()
            .ToArray();
        return comparisons.Length == 0
            ? DocumentQueryClause.MatchNone
            : new DocumentQueryClause(comparisons);
    }

    private DocumentQueryComparison? TranslateComparison(QueryComparison<TEntity> comparison)
    {
        var path = ResolvePath(comparison.FieldSelector);
        return comparison.Operator switch
        {
            QueryOp.Equal => DocumentQueryComparison.Equal(path, SerializeScalar(comparison.Value, comparison)),
            QueryOp.In => TranslateMembership(path, comparison),
            QueryOp.Contains => TranslateContains(path, comparison),
            _ => throw Failure(
                comparison,
                $"Query operator '{comparison.Operator}' is not supported by the Groundwork translator.")
        };
    }

    private DocumentQueryComparison? TranslateMembership(
        string path,
        QueryComparison<TEntity> comparison)
    {
        if (comparison.Value is string || comparison.Value is not IEnumerable values)
        {
            throw Failure(
                comparison,
                $"Query operator '{QueryOp.In}' requires a set value.");
        }

        var serialized = values.Cast<object?>()
            .Select(value => SerializeScalar(value, comparison))
            .ToArray();
        return serialized.Length == 0
            ? null
            : DocumentQueryComparison.In(path, serialized);
    }

    private DocumentQueryComparison TranslateContains(
        string path,
        QueryComparison<TEntity> comparison)
    {
        if (comparison.Value is not string value)
        {
            throw Failure(
                comparison,
                $"Query operator '{QueryOp.Contains}' requires a non-null string value.");
        }

        return DocumentQueryComparison.Contains(path, value);
    }

    private string ResolvePath(LambdaExpression selector)
    {
        var body = selector.Body;
        if (body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary)
            body = unary.Operand;

        if (body is not MemberExpression { Expression: ParameterExpression } member ||
            member.Member is not PropertyInfo property)
        {
            throw new GroundworkQueryTranslationException(
                $"The Groundwork query translator requires a direct property member selector; got '{body}'.");
        }

        return ResolvePath(property);
    }

    private string ResolvePath(PropertyInfo property)
    {
        var jsonProperty = _entityTypeInfo.Properties.SingleOrDefault(candidate =>
            candidate.AttributeProvider is PropertyInfo candidateProperty && candidateProperty == property);
        if (jsonProperty is null ||
            jsonProperty.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), true)
                .OfType<JsonIgnoreAttribute>()
                .Any(attribute => attribute.Condition == JsonIgnoreCondition.Always) == true ||
            GroundworkDocumentSerialization.IsExplicitlyExcluded(_jsonOptions, jsonProperty))
        {
            throw new GroundworkQueryTranslationException(
                $"Property '{property.DeclaringType?.FullName}.{property.Name}' is excluded from canonical JSON and cannot be queried.");
        }

        var serializedName = jsonProperty.Name;
        if (string.IsNullOrWhiteSpace(serializedName))
        {
            throw new GroundworkQueryTranslationException(
                $"Property '{property.DeclaringType?.FullName}.{property.Name}' has no stable serialized name.");
        }

        return $"{EntityPath}.{serializedName}";
    }

    private string? SerializeScalar(
        object? value,
        QueryComparison<TEntity> comparison)
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
                    $"Value type '{value.GetType().FullName}' is not a scalar JSON query value.")
            };
        }
        catch (GroundworkQueryTranslationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException)
        {
            throw new GroundworkQueryTranslationException(
                $"Value for property '{comparison.FieldName}' could not be serialized as a stable query scalar.",
                exception);
        }
    }

    private static GroundworkQueryTranslationException Failure(
        QueryComparison<TEntity> comparison,
        string message) =>
        new($"{message} Property: '{comparison.FieldName}'.");
}
