using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// Pins the decision on elsa-workflows/elsa-foundation#1837: one binary collation per provider, chosen
/// once. Six modules previously spelled the same intent four ways, and SQL Server had two values.
/// </summary>
public sealed class EfOrdinalCollationTests
{
    [Theory]
    [InlineData(EfProviderNames.SqlServer, "Latin1_General_100_BIN2")]
    [InlineData(EfProviderNames.PostgreSql, "C")]
    [InlineData(EfProviderNames.MySql, "utf8mb4_0900_bin")]
    // SQLite's BINARY is its only TEXT collation and its default, so declaring it says nothing.
    [InlineData(EfProviderNames.Sqlite, null)]
    public void One_binary_collation_is_declared_per_provider(string provider, string? expected) =>
        Assert.Equal(expected, EfOrdinalCollation.ForProvider(provider));

    /// <summary>
    /// A provider nobody decided for must not quietly get the server's linguistic default. That silence is
    /// how Activities Design's declaration went missing from two providers' schemas in the first place.
    /// </summary>
    [Fact]
    public void An_undecided_provider_is_refused_rather_than_left_on_the_server_default() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => EfOrdinalCollation.ForProvider("Oracle.EntityFrameworkCore"));

    [Fact]
    public void Keys_and_indexes_are_collated_and_other_string_columns_are_not()
    {
        var model = Build();

        Assert.Equal(EfOrdinalCollation.SqlServer, Collation(model, nameof(Row.LookupHash)));
        Assert.Equal(EfOrdinalCollation.SqlServer, Collation(model, nameof(Row.ScopeKey)));
        Assert.Null(Collation(model, nameof(Row.Payload)));
        Assert.Null(Collation(model, nameof(Row.Cursor)));
    }

    [Fact]
    public void A_named_column_is_collated_even_though_no_index_covers_it()
    {
        var model = Build(nameof(Row.Cursor));

        Assert.Equal(EfOrdinalCollation.SqlServer, Collation(model, nameof(Row.Cursor)));
        Assert.Null(Collation(model, nameof(Row.Payload)));
    }

    /// <summary>A shared list can name a column a given model does not have; that is not an error.</summary>
    [Fact]
    public void A_name_no_entity_declares_is_ignored()
    {
        var model = Build("ColumnFromAnotherModule");

        Assert.Null(Collation(model, nameof(Row.Payload)));
    }

    [Fact]
    public void Sqlite_leaves_every_column_on_its_default()
    {
        var model = Build(nameof(Row.Cursor), EfProviderNames.Sqlite);

        Assert.All(
            model.FindEntityType(typeof(Row))!.GetProperties(),
            property => Assert.Null(property.FindAnnotation(RelationalAnnotationNames.Collation)?.Value));
    }

    /// <summary>Nothing here may set a collation for the database: modules can share one.</summary>
    [Fact]
    public void Nothing_is_declared_for_the_database()
    {
        Assert.Null(Build().GetCollation());
        Assert.Null(Build().FindEntityType(typeof(Row))!.FindAnnotation(RelationalAnnotationNames.Collation)?.Value);
    }

    /// <summary>
    /// The in-memory half covers the same columns as the SQL half, so a module cannot declare one and get the
    /// other silently (elsa-workflows/elsa-foundation#1855). What that comparer is worth on a real SQL Server
    /// model is measured in <c>OrdinalKeyComparerTests</c>; here it is the coverage that is pinned.
    /// </summary>
    [Theory]
    [InlineData(EfProviderNames.SqlServer)]
    [InlineData(EfProviderNames.PostgreSql)]
    [InlineData(EfProviderNames.MySql)]
    // SQLite declares no collation, and still declares the comparer: the identity map is not the server's.
    [InlineData(EfProviderNames.Sqlite)]
    public void The_comparer_covers_the_same_columns_on_every_provider(string provider)
    {
        var model = Build(nameof(Row.Cursor), provider);

        Assert.False(Comparer(model, nameof(Row.LookupHash))!.Equals("A", "a"));
        Assert.False(Comparer(model, nameof(Row.ScopeKey))!.Equals("A", "a"));
        Assert.False(Comparer(model, nameof(Row.Cursor))!.Equals("A", "a"));
        // Declaring nothing leaves the property on whatever the provider's type mapping brings.
        Assert.Null(Comparer(model, nameof(Row.Payload)));
    }

    /// <summary>
    /// Ordinal equality is only half a comparer. Two strings that differ by case must also land on different
    /// hashes, or the identity map buckets them together and compares them anyway.
    /// </summary>
    [Fact]
    public void The_comparer_hashes_ordinally_as_well_as_compares()
    {
        var comparer = Comparer(Build(), nameof(Row.LookupHash))!;

        Assert.False(comparer.Equals("A", "a"));
        Assert.NotEqual(comparer.GetHashCode("A"), comparer.GetHashCode("a"));
        Assert.True(comparer.Equals("A", "A"));
    }

    private static ValueComparer? Comparer(IMutableModel model, string property) =>
        model.FindEntityType(typeof(Row))!.FindProperty(property)!.GetValueComparer();

    /// <summary>
    /// Read through the annotation rather than <c>GetCollation()</c>: this model is never finalized, and
    /// the extension resolves a type mapping the convention set has not built yet.
    /// </summary>
    private static string? Collation(IMutableModel model, string property) =>
        model.FindEntityType(typeof(Row))!.FindProperty(property)!.FindAnnotation(RelationalAnnotationNames.Collation)?.Value as string;

    private static IMutableModel Build(string? alsoCompared = null, string provider = EfProviderNames.SqlServer)
    {
        // A bare ModelBuilder runs no conventions, so every column this fixture asserts on is declared.
        var builder = new ModelBuilder();
        builder.Entity<Row>(entity =>
        {
            entity.Property(row => row.LookupHash);
            entity.Property(row => row.ScopeKey);
            entity.Property(row => row.Cursor);
            entity.Property(row => row.Payload);
            entity.HasKey(row => row.LookupHash);
            entity.HasIndex(row => row.ScopeKey);
        });
        EfOrdinalCollation.Apply(builder, provider, alsoCompared is null ? [] : [alsoCompared]);
        return builder.Model;
    }

    private sealed class Row
    {
        public string LookupHash { get; set; } = "";
        public string ScopeKey { get; set; } = "";
        public string Cursor { get; set; } = "";
        public string Payload { get; set; } = "";
    }
}
