using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// The in-memory half of the ordinal declaration (elsa-workflows/elsa-foundation#1855). A collation governs
/// what the server evaluates; EF Core's identity map compares key values in .NET, and on SQL Server it did so
/// case-insensitively whatever the column said — so the schema and the change tracker disagreed about what
/// "the same row" is.
/// <para>
/// Building a model needs no database, so these run in the fast lane. What they cannot show is the two halves
/// agreeing on a live server: that is <c>OrdinalCollationProviderTests</c>, on the container leg.
/// </para>
/// </summary>
public sealed class OrdinalKeyComparerTests
{
    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    [InlineData("MySql")]
    [InlineData("Sqlite")]
    public void Every_key_column_a_declaring_module_owns_compares_ordinally(string provider)
    {
        foreach (var type in ModuleContextCatalog.Contexts(provider).Where(ModuleContextCatalog.DeclaresOrdinal))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection(provider));
            var columns = KeyStringColumns(context);

            // A module that stopped declaring, or an entity that lost its key, would otherwise pass vacuously.
            Assert.NotEmpty(columns);
            Assert.All(columns, property => Assert.False(
                property.GetKeyValueComparer().Equals("A", "a"),
                $"{type.Name}.{property.DeclaringType.DisplayName()}.{property.Name} still compares case-blind."));
        }
    }

    /// <summary>
    /// The control, and the reason the assertion above is worth anything: SQL Server's provider still attaches
    /// its case-insensitive comparer to every string key it is not told about. The six contexts that declare
    /// neither half are #1860; when one of them starts declaring, it moves out of this test and into the one
    /// above, and this one has to be told.
    /// </summary>
    [Fact]
    public void Sql_servers_own_comparer_is_still_case_blind_where_no_module_declares_otherwise()
    {
        foreach (var type in ModuleContextCatalog.Contexts("SqlServer").Where(type => !ModuleContextCatalog.DeclaresOrdinal(type)))
        {
            using var context = ModuleContextCatalog.Create(type, ModuleContextCatalog.PlaceholderConnection("SqlServer"));

            Assert.All(KeyStringColumns(context), property => Assert.True(property.GetKeyValueComparer().Equals("A", "a")));
        }
    }

    /// <summary>
    /// The symptom from the report, on the real Secrets model: two secrets whose <c>NormalizedName</c> differs
    /// only in case are two rows by the primary key, and the change tracker has to agree before
    /// <c>SaveChanges</c> is ever reached. Tracking needs no database.
    /// </summary>
    [Fact]
    public void Two_secrets_differing_only_in_case_are_two_tracked_entities_on_sql_server()
    {
        using var context = Secrets();

        context.Add(Secret("A"));
        context.Add(Secret("a"));

        Assert.Equal(2, context.ChangeTracker.Entries<SecretRecord>().Count());
    }

    /// <summary>The other half of that: an identity map that accepted a genuine duplicate would be no map.</summary>
    [Fact]
    public void The_same_key_twice_is_still_refused()
    {
        using var context = Secrets();
        context.Add(Secret("A"));

        Assert.Throws<InvalidOperationException>(() => context.Add(Secret("A")));
    }

    private static DbContext Secrets() => ModuleContextCatalog.Create(
        ModuleContextCatalog.Contexts("SqlServer").Single(type => type.Name.StartsWith("Secrets", StringComparison.Ordinal)),
        ModuleContextCatalog.PlaceholderConnection("SqlServer"));

    private static SecretRecord Secret(string normalizedName) => new() { TenantId = "t", NormalizedName = normalizedName };

    private static IProperty[] KeyStringColumns(DbContext context) => [.. context.Model.GetEntityTypes()
        .SelectMany(entity => entity.GetKeys().SelectMany(key => key.Properties))
        .Where(property => property.ClrType == typeof(string))
        .Distinct()];
}
