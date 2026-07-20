using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
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

        var result = _translator.Translate(DocumentKind, QueryIdentity, source);

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

        var result = _translator.Translate(DocumentKind, QueryIdentity, source);

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

        var result = _translator.Translate(DocumentKind, QueryIdentity, source);

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
    public void Translates_single_field_order(bool descending, PhysicalSortDirection expectedDirection)
    {
        var source = descending
            ? Query<DesignDocument>.All().OrderByDescending(x => x.Sequence)
            : Query<DesignDocument>.All().OrderBy(x => x.Sequence);

        var result = _translator.Translate(DocumentKind, QueryIdentity, source);

        var order = Assert.Single(result.Order);
        Assert.Equal("entity.sequence", order.Path);
        Assert.Equal(expectedDirection, order.Direction);
    }

    [Theory]
    [InlineData(BoundedQueryResultOperation.Documents)]
    [InlineData(BoundedQueryResultOperation.Count)]
    [InlineData(BoundedQueryResultOperation.Any)]
    [InlineData(BoundedQueryResultOperation.First)]
    public void Makes_result_operation_and_offset_page_explicit(BoundedQueryResultOperation operation)
    {
        var result = _translator.Translate(
            DocumentKind,
            QueryIdentity,
            Query<DesignDocument>.All(),
            operation,
            skip: 50,
            take: 25);

        Assert.Equal(operation, result.ResultOperation);
        Assert.Equal(50, result.Skip);
        Assert.Equal(25, result.Take);
    }

    [Fact]
    public void Preserves_null_and_missing_equality_semantics()
    {
        var source = Query<DesignDocument>.Where(x => x.Description, QueryOp.Equal, null);

        var result = _translator.Translate(DocumentKind, QueryIdentity, source);

        var comparison = Assert.Single(Assert.Single(result.Clauses).Comparisons);
        Assert.Equal("entity.description", comparison.Path);
        Assert.Equal([null], comparison.Values);
    }

    [Fact]
    public void Empty_membership_is_a_match_none_clause()
    {
        var source = Query<DesignDocument>.Where(x => x.Category, QueryOp.In, Array.Empty<string>());

        var result = _translator.Translate(DocumentKind, QueryIdentity, source);

        Assert.Empty(Assert.Single(result.Clauses).Comparisons);
    }

    [Fact]
    public void Rejects_contains_null_before_a_provider_can_execute()
    {
        var source = Query<DesignDocument>.Where(x => x.Description, QueryOp.Contains, null);

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            _translator.Translate(DocumentKind, QueryIdentity, source));

        Assert.Contains(nameof(QueryOp.Contains), exception.Message, StringComparison.Ordinal);
        Assert.Contains(nameof(DesignDocument.Description), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Rejects_membership_with_a_scalar_before_a_provider_can_execute()
    {
        var source = Query<DesignDocument>.Where(x => x.Category, QueryOp.In, "not-a-set");

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            _translator.Translate(DocumentKind, QueryIdentity, source));

        Assert.Contains(nameof(QueryOp.In), exception.Message, StringComparison.Ordinal);
        Assert.Contains("set", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_non_member_selectors_before_a_provider_can_execute()
    {
        var source = Query<DesignDocument>.Where(x => x.Title.ToLowerInvariant(), QueryOp.Equal, "invoice");

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            _translator.Translate(DocumentKind, QueryIdentity, source));

        Assert.Contains("member", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_properties_excluded_from_canonical_json_before_a_provider_can_execute()
    {
        var source = Query<DesignDocument>.Where(x => x.TransientLabel, QueryOp.Equal, "invoice");

        var exception = Assert.Throws<GroundworkQueryTranslationException>(() =>
            _translator.Translate(DocumentKind, QueryIdentity, source));

        Assert.Contains("excluded", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(nameof(DesignDocument.TransientLabel), exception.Message, StringComparison.Ordinal);
    }

    private sealed class DesignDocument : Entity
    {
        [JsonPropertyName("title_text")]
        public string Title { get; init; } = "";

        public string? Description { get; init; }

        public string Category { get; init; } = "";

        public int Sequence { get; init; }

        public DateTimeOffset PublishedAt { get; init; }

        [JsonIgnore]
        public string TransientLabel { get; init; } = "";
    }
}
