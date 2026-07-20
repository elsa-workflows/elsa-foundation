using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Elsa.Persistence.Core.Queries;
using Elsa.Primitives.Entities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Documents.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

public sealed class GroundworkQueryTranslatorTests
{
    private const string DocumentKind = "designDocument";
    private const string QueryIdentity = "search-design-documents";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly GroundworkQueryTranslator<DesignDocument> _translator = new(Json);

    [Theory]
    [InlineData(QueryOp.Equal, QueryComparisonOperator.Equal)]
    [InlineData(QueryOp.In, QueryComparisonOperator.In)]
    [InlineData(QueryOp.Contains, QueryComparisonOperator.Contains)]
    public void Translates_every_closed_query_operator(
        QueryOp sourceOperator,
        QueryComparisonOperator expectedOperator)
    {
        object value = sourceOperator == QueryOp.In ? new[] { "alpha", "beta" } : "alpha";
        var source = Query<DesignDocument>.Where(x => x.Category, sourceOperator, value);

        var result = TranslateDocuments(source);

        var comparison = Assert.Single(Assert.Single(result.Clauses).Comparisons);
        Assert.Equal("entity.category", comparison.Path);
        Assert.Equal(expectedOperator, comparison.Operator);
        Assert.Equal(
            sourceOperator == QueryOp.In ? ["alpha", "beta"] : ["alpha"],
            comparison.Values);
    }

    [Fact]
    public void Preserves_and_of_or_clause_structure()
    {
        var source = Query<DesignDocument>
            .Where(x => x.Title, QueryOp.Contains, "order")
            .Or(x => x.Description, QueryOp.Contains, "order")
            .And(x => x.Category, QueryOp.Equal, "sales");

        var result = TranslateDocuments(source);

        Assert.Collection(
            result.Clauses,
            clause => Assert.Equal(
                ["entity.title_text", "entity.description"],
                clause.Comparisons.Select(x => x.Path)),
            clause => Assert.Equal(
                ["entity.category"],
                clause.Comparisons.Select(x => x.Path)));
    }

    [Fact]
    public void Uses_json_serialized_names_and_invariant_scalar_values()
    {
        var source = Query<DesignDocument>
            .Where(x => x.Title, QueryOp.Equal, "Invoice")
            .And(x => x.Sequence, QueryOp.Equal, 42)
            .And(x => x.PublishedAt, QueryOp.Equal, DateTimeOffset.Parse(
                "2026-07-20T03:21:30+00:00",
                CultureInfo.InvariantCulture));

        var result = TranslateDocuments(source);

        Assert.Equal(
            ["entity.title_text", "entity.sequence", "entity.publishedAt"],
            result.Clauses.SelectMany(x => x.Comparisons).Select(x => x.Path));
        Assert.Equal(
            ["Invoice", "42", "2026-07-20T03:21:30+00:00"],
            result.Clauses.SelectMany(x => x.Comparisons).SelectMany(x => x.Values));
    }

    [Theory]
    [InlineData(false, PhysicalSortDirection.Ascending)]
    [InlineData(true, PhysicalSortDirection.Descending)]
    public void Emits_only_the_callers_declared_order_prefix(
        bool descending,
        PhysicalSortDirection expectedDirection)
    {
        var source = descending
            ? Query<DesignDocument>.All().OrderByDescending(x => x.Sequence)
            : Query<DesignDocument>.All().OrderBy(x => x.Sequence);

        var result = _translator.Translate(DocumentKind, QueryIdentity, source, take: 25);

        var order = Assert.Single(result.Order);
        Assert.Equal("entity.sequence", order.Path);
        Assert.Equal(expectedDirection, order.Direction);
    }

    [Theory]
    [InlineData(BoundedQueryResultOperation.Count)]
    [InlineData(BoundedQueryResultOperation.Any)]
    [InlineData(BoundedQueryResultOperation.First)]
    public void Accepts_bounded_terminal_result_operations_without_a_document_page(BoundedQueryResultOperation operation)
    {
        var result = _translator.Translate(
            DocumentKind,
            QueryIdentity,
            Query<DesignDocument>.All(),
            operation);

        Assert.Equal(operation, result.ResultOperation);
        Assert.Null(result.Skip);
        Assert.Null(result.Take);
        Assert.Empty(result.Order);
    }

    [Fact]
    public void Accepts_bounded_ordered_document_results()
    {
        var result = _translator.Translate(
            DocumentKind,
            QueryIdentity,
            Query<DesignDocument>.All().OrderBy(x => x.Id),
            BoundedQueryResultOperation.Documents,
            skip: 50,
            take: 25);

        Assert.Equal(BoundedQueryResultOperation.Documents, result.ResultOperation);
        Assert.Equal(50, result.Skip);
        Assert.Equal(25, result.Take);
        Assert.Equal("entity.id", Assert.Single(result.Order).Path);
    }

    [Fact]
    public void Rejects_document_results_without_a_take_bound_before_a_provider_can_execute()
    {
        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            _translator.Translate(
                DocumentKind,
                QueryIdentity,
                Query<DesignDocument>.All().OrderBy(x => x.Id)));

        Assert.Contains("positive take", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Rejects_document_results_with_a_non_positive_take_bound_before_a_provider_can_execute(int take)
    {
        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            _translator.Translate(
                DocumentKind,
                QueryIdentity,
                Query<DesignDocument>.All().OrderBy(x => x.Id),
                take: take));

        Assert.Contains("positive take", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_document_results_without_a_deterministic_order_before_a_provider_can_execute()
    {
        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            _translator.Translate(
                DocumentKind,
                QueryIdentity,
                Query<DesignDocument>.All(),
                take: 1));

        Assert.Contains("deterministic order", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_an_undefined_result_operation_before_a_provider_can_execute()
    {
        var invalidOperation = (BoundedQueryResultOperation)(-1);

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            _translator.Translate(
                DocumentKind,
                QueryIdentity,
                Query<DesignDocument>.All(),
                invalidOperation));

        Assert.Contains("result operation", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("-1", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Preserves_null_and_missing_equality_semantics()
    {
        var source = Query<DesignDocument>.Where(x => x.Description, QueryOp.Equal, null);

        var result = TranslateDocuments(source);

        var comparison = Assert.Single(Assert.Single(result.Clauses).Comparisons);
        Assert.Equal("entity.description", comparison.Path);
        Assert.Equal([null], comparison.Values);
    }

    [Fact]
    public void Empty_membership_is_a_match_none_clause()
    {
        var source = Query<DesignDocument>.Where(x => x.Category, QueryOp.In, Array.Empty<string>());

        var result = TranslateDocuments(source);

        Assert.Empty(Assert.Single(result.Clauses).Comparisons);
    }

    [Fact]
    public void Rejects_contains_null_before_a_provider_can_execute()
    {
        var source = Query<DesignDocument>.Where(x => x.Description, QueryOp.Contains, null);

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            TranslateDocuments(source));

        Assert.Contains(nameof(QueryOp.Contains), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DesignDocument.Description), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_membership_with_a_scalar_before_a_provider_can_execute()
    {
        var source = Query<DesignDocument>.Where(x => x.Category, QueryOp.In, "not-a-set");

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            TranslateDocuments(source));

        Assert.Contains(nameof(QueryOp.In), exception.Message, StringComparison.Ordinal);
        Assert.Contains("set", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_non_member_selectors_before_a_provider_can_execute()
    {
        var source = Query<DesignDocument>.Where(x => x.Title.ToLowerInvariant(), QueryOp.Equal, "invoice");

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            TranslateDocuments(source));

        Assert.Contains("member", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_properties_excluded_from_canonical_json_before_a_provider_can_execute()
    {
        var source = Query<DesignDocument>.Where(x => x.TransientLabel, QueryOp.Equal, "invoice");

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            TranslateDocuments(source));

        Assert.Contains("excluded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(DesignDocument.TransientLabel), exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(NonScalarValues))]
    public void Rejects_non_scalar_json_values_before_a_provider_can_execute(object value)
    {
        var source = Query<DesignDocument>.Where(x => x.Category, QueryOp.Equal, value);

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            TranslateDocuments(source));

        Assert.Contains("scalar", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(DesignDocument.Category), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Wraps_json_serialization_failures_in_the_translation_boundary()
    {
        var options = new JsonSerializerOptions(Json);
        options.Converters.Add(new FailingValueConverter());
        var translator = new GroundworkQueryTranslator<DesignDocument>(options);
        var source = Query<DesignDocument>.Where(
            x => x.Category,
            QueryOp.Equal,
            new FailingValue());

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            TranslateDocuments(translator, source));

        Assert.Contains("serialized", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(DesignDocument.Category), exception.Message, StringComparison.Ordinal);
        Assert.IsType<JsonException>(exception.InnerException);
    }

    public static TheoryData<object> NonScalarValues => new()
    {
        new { Value = "object" },
        new[] { "array" }
    };

    [Fact]
    public void Rejects_members_with_explicit_constant_false_canonical_suppression()
    {
        var translator = new GroundworkQueryTranslator<DesignDocument>(
            GroundworkDocumentSerialization.Create([nameof(Entity.RowNumber)]));
        var source = Query<DesignDocument>.Where(x => x.RowNumber, QueryOp.Equal, 1L);

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            TranslateDocuments(translator, source));

        Assert.Contains(nameof(Entity.RowNumber), exception.Message, StringComparison.Ordinal);
        Assert.Contains("excluded", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Keeps_when_writing_default_properties_queryable()
    {
        var source = Query<DesignDocument>.Where(
            x => x.DefaultOmittedSequence,
            QueryOp.Equal,
            0);

        var result = TranslateDocuments(source);

        Assert.Equal(
            "entity.defaultOmittedSequence",
            Assert.Single(Assert.Single(result.Clauses).Comparisons).Path);
    }

    [Fact]
    public void Keeps_when_writing_null_properties_queryable()
    {
        var source = Query<DesignDocument>.Where(
            x => x.NullOmittedLabel,
            QueryOp.Equal,
            null);

        var result = TranslateDocuments(source);

        Assert.Equal(
            "entity.nullOmittedLabel",
            Assert.Single(Assert.Single(result.Clauses).Comparisons).Path);
    }

    [Fact]
    public void Keeps_value_dependent_resolver_predicates_queryable()
    {
        var options = new JsonSerializerOptions(Json)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    typeInfo =>
                    {
                        if (typeInfo.Type == typeof(DesignDocument))
                        {
                            typeInfo.Properties.Single(property => property.Name == "sequence")
                                .ShouldSerialize = static (_, value) => value is int number && number > 0;
                        }
                    }
                }
            }
        };
        var translator = new GroundworkQueryTranslator<DesignDocument>(options);
        var source = Query<DesignDocument>.Where(x => x.Sequence, QueryOp.Equal, 0);

        var result = TranslateDocuments(translator, source);

        Assert.Equal(
            "entity.sequence",
            Assert.Single(Assert.Single(result.Clauses).Comparisons).Path);
    }

    [Fact]
    public void Uses_property_names_renamed_by_the_canonical_type_info_resolver()
    {
        var options = new JsonSerializerOptions(Json)
        {
            TypeInfoResolver = new DefaultJsonTypeInfoResolver
            {
                Modifiers =
                {
                    typeInfo =>
                    {
                        if (typeInfo.Type == typeof(DesignDocument))
                        {
                            typeInfo.Properties.Single(property => property.Name == "category").Name = "classification";
                        }
                    }
                }
            }
        };
        var translator = new GroundworkQueryTranslator<DesignDocument>(options);
        var source = Query<DesignDocument>.Where(x => x.Category, QueryOp.Equal, "sales");

        var result = TranslateDocuments(translator, source);

        Assert.Equal("entity.classification", Assert.Single(Assert.Single(result.Clauses).Comparisons).Path);
    }

    private DocumentQuery TranslateDocuments(Query<DesignDocument> source, int take = 25) =>
        TranslateDocuments(_translator, source, take);

    private static DocumentQuery TranslateDocuments(
        GroundworkQueryTranslator<DesignDocument> translator,
        Query<DesignDocument> source,
        int take = 25)
    {
        if (source.Order is null)
            source.OrderBy(x => x.Id);

        return translator.Translate(
            DocumentKind,
            QueryIdentity,
            source,
            BoundedQueryResultOperation.Documents,
            take: take);
    }

    private sealed class DesignDocument : Entity
    {
        [JsonPropertyName("title_text")]
        public string Title { get; init; } = "";

        public string? Description { get; init; }

        public string Category { get; init; } = "";

        public int Sequence { get; init; }

        public DateTimeOffset PublishedAt { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
        public int DefaultOmittedSequence { get; init; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? NullOmittedLabel { get; init; }

        [JsonIgnore]
        public string TransientLabel { get; init; } = "";
    }

    private sealed class FailingValue;

    private sealed class FailingValueConverter : JsonConverter<FailingValue>
    {
        public override FailingValue? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options) =>
            throw new NotSupportedException();

        public override void Write(
            Utf8JsonWriter writer,
            FailingValue value,
            JsonSerializerOptions options) =>
            throw new JsonException("deliberate-test-failure");
    }
}
