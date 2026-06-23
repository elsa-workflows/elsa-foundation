using Elsa.Persistence.Core.Queries;
using Elsa.Primitives.Entities;
using Xunit;

namespace Elsa.Persistence.Core.Tests;

/// <summary>
/// Proves <see cref="InMemoryQueryEvaluator"/> reproduces every design-lane query shape with semantics
/// identical to the EF Core translator. These are the exact cases <c>EFCoreQueryTranslatorTests</c>
/// asserts, run here against the provider-neutral in-memory fallback a non-relational adapter (e.g.
/// Groundwork) relies on — so swapping providers yields the same result set.
/// </summary>
public class InMemoryQueryEvaluatorTests
{
    private sealed class Doc : Entity
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Category { get; set; } = string.Empty;
        public string SortKey { get; set; } = string.Empty;
    }

    private static IEnumerable<Doc> Sample() =>
    [
        new Doc { Id = "a", Name = "Order Processing", Description = "Handles orders", Category = "sales", SortKey = "0000000003" },
        new Doc { Id = "b", Name = "invoice generator", Description = null, Category = "finance", SortKey = "0000000001" },
        new Doc { Id = "c", Name = "Shipping", Description = "ORDER fulfilment", Category = "ops", SortKey = "0000000002" },
    ];

    [Fact]
    public void Equal_matches_exact_field()
    {
        var q = Query<Doc>.Where(x => x.Category, QueryOp.Equal, "finance");
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Equal(["b"], result.Select(x => x.Id));
    }

    [Fact]
    public void Equal_uses_exact_string_equality()
    {
        var q = Query<Doc>.Where(x => x.Category, QueryOp.Equal, "FINANCE");
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void Equal_with_null_value_matches_null_field()
    {
        var q = Query<Doc>.Where(x => x.Description, QueryOp.Equal, null);
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Equal(["b"], result.Select(x => x.Id));
    }

    [Fact]
    public void In_matches_set_membership()
    {
        var q = Query<Doc>.Where(x => x.Id, QueryOp.In, new[] { "a", "c" });
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Equal(["a", "c"], result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public void In_uses_exact_string_equality()
    {
        var q = Query<Doc>.Where(x => x.Id, QueryOp.In, new[] { "A", "C" });
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void In_with_empty_set_matches_nothing()
    {
        var q = Query<Doc>.Where(x => x.Id, QueryOp.In, Array.Empty<string>());
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void Contains_is_case_insensitive_substring()
    {
        var q = Query<Doc>.Where(x => x.Name, QueryOp.Contains, "ORDER");
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Equal(["a"], result.Select(x => x.Id));
    }

    [Fact]
    public void Contains_on_null_field_yields_no_match()
    {
        // Description of "b" is null; must not throw and must not match.
        var q = Query<Doc>.Where(x => x.Description, QueryOp.Contains, "anything");
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.DoesNotContain(result, x => x.Id == "b");
    }

    [Fact]
    public void Or_fans_out_within_a_clause_like_search_term()
    {
        const string term = "order";
        var q = Query<Doc>.Where(x => x.Name, QueryOp.Contains, term)
                          .Or(x => x.Description, QueryOp.Contains, term)
                          .Or(x => x.Id, QueryOp.Contains, term);

        var result = InMemoryQueryEvaluator.Apply(Sample(), q).Select(x => x.Id).OrderBy(x => x).ToList();

        Assert.Equal(["a", "c"], result);
    }

    [Fact]
    public void And_across_clauses_intersects()
    {
        var q = Query<Doc>.Where(x => x.Name, QueryOp.Contains, "ship")
                          .And(x => x.Category, QueryOp.Equal, "ops");
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Equal(["c"], result.Select(x => x.Id));
    }

    [Fact]
    public void OrderByDescending_sorts_like_latest_version_resolution()
    {
        var q = Query<Doc>.All().OrderByDescending(x => x.SortKey);
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Equal(["a", "c", "b"], result.Select(x => x.Id));
    }

    [Fact]
    public void OrderBy_ascending_sorts_by_field()
    {
        var q = Query<Doc>.All().OrderBy(x => x.SortKey);
        var result = InMemoryQueryEvaluator.Apply(Sample(), q).ToList();
        Assert.Equal(["b", "c", "a"], result.Select(x => x.Id));
    }

    [Fact]
    public void Empty_query_matches_everything()
    {
        var result = InMemoryQueryEvaluator.Apply(Sample(), Query<Doc>.All()).ToList();
        Assert.Equal(3, result.Count);
    }
}
