using Elsa.Persistence.Core.Queries;
using Elsa.Persistence.EFCore.Queries;
using Elsa.Primitives.Entities;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Persistence.EFCore.Tests;

/// <summary>
/// Proves the closed <see cref="Query{TEntity}"/> + <see cref="EFCoreQueryTranslator"/> can reproduce
/// every query shape the design lanes express today through <c>IFilter&lt;T&gt;</c>/<c>IQueries&lt;T&gt;</c>:
/// equality, set membership (<c>IN</c>), case-insensitive substring search, <c>OR</c>-fan-out within a
/// clause, <c>AND</c> across clauses, and single-field ordering. This is the crux the whole
/// host-configurable-provider plan rests on, validated here in isolation (LINQ-to-objects) before any
/// consumer migration.
/// </summary>
public class EFCoreQueryTranslatorTests
{
    private sealed class Doc : Entity
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string Category { get; set; } = string.Empty;
        public string SortKey { get; set; } = string.Empty;
    }

    private sealed class DocContext(DbContextOptions<DocContext> options) : DbContext(options)
    {
        public DbSet<Doc> Docs => Set<Doc>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Doc>().Ignore(x => x.RowNumber);
        }
    }

    private static IQueryable<Doc> Sample() => new[]
    {
        new Doc { Id = "a", Name = "Order Processing", Description = "Handles orders", Category = "sales", SortKey = "0000000003" },
        new Doc { Id = "b", Name = "invoice generator", Description = null, Category = "finance", SortKey = "0000000001" },
        new Doc { Id = "c", Name = "Shipping", Description = "ORDER fulfilment", Category = "ops", SortKey = "0000000002" },
    }.AsQueryable();

    [Fact]
    public void Equal_matches_exact_field()
    {
        var q = Query<Doc>.Where(x => x.Category, QueryOp.Equal, "finance");
        var result = EFCoreQueryTranslator.Apply(Sample(), q).ToList();
        Assert.Equal(["b"], result.Select(x => x.Id));
    }

    [Fact]
    public void In_matches_set_membership()
    {
        var q = Query<Doc>.Where(x => x.Id, QueryOp.In, new[] { "a", "c" });
        var result = EFCoreQueryTranslator.Apply(Sample(), q).ToList();
        Assert.Equal(["a", "c"], result.Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public void Contains_is_case_insensitive_substring()
    {
        var q = Query<Doc>.Where(x => x.Name, QueryOp.Contains, "ORDER");
        var result = EFCoreQueryTranslator.Apply(Sample(), q).ToList();
        Assert.Equal(["a"], result.Select(x => x.Id));
    }

    [Fact]
    public void Or_fans_out_within_a_clause_like_search_term()
    {
        // Mirrors WorkflowDefinitionFilter.SearchTerm: Name OR Description OR Id contains, case-insensitive.
        const string term = "order";
        var q = Query<Doc>.Where(x => x.Name, QueryOp.Contains, term)
                          .Or(x => x.Description, QueryOp.Contains, term)
                          .Or(x => x.Id, QueryOp.Contains, term);

        var result = EFCoreQueryTranslator.Apply(Sample(), q).Select(x => x.Id).OrderBy(x => x).ToList();

        // "a" (Name "Order Processing"), "c" (Description "ORDER fulfilment").
        Assert.Equal(["a", "c"], result);
    }

    [Fact]
    public async Task Contains_translates_for_sqlite_search_terms()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<DocContext>()
            .UseSqlite(connection)
            .Options;
        await using var context = new DocContext(options);
        await context.Database.EnsureCreatedAsync();
        context.Docs.AddRange(Sample());
        await context.SaveChangesAsync();
        const string term = "order";
        var q = Query<Doc>.Where(x => x.Name, QueryOp.Contains, term)
                          .Or(x => x.Description, QueryOp.Contains, term)
                          .Or(x => x.Id, QueryOp.Contains, term);

        var result = await EFCoreQueryTranslator.Apply(context.Docs, q)
            .Select(x => x.Id)
            .OrderBy(x => x)
            .ToListAsync();

        Assert.Equal(["a", "c"], result);
    }

    [Fact]
    public void And_across_clauses_intersects()
    {
        var q = Query<Doc>.Where(x => x.Name, QueryOp.Contains, "ship")
                          .And(x => x.Category, QueryOp.Equal, "ops");
        var result = EFCoreQueryTranslator.Apply(Sample(), q).ToList();
        Assert.Equal(["c"], result.Select(x => x.Id));
    }

    [Fact]
    public void OrderByDescending_sorts_like_latest_version_resolution()
    {
        // Mirrors FindLastVersion: order by SemVerSortKey desc, take first.
        var q = Query<Doc>.All().OrderByDescending(x => x.SortKey);
        var result = EFCoreQueryTranslator.Apply(Sample(), q).ToList();
        Assert.Equal(["a", "c", "b"], result.Select(x => x.Id));
    }

    [Fact]
    public void Empty_query_matches_everything()
    {
        var result = EFCoreQueryTranslator.Apply(Sample(), Query<Doc>.All()).ToList();
        Assert.Equal(3, result.Count);
    }
}
