using Elsa.Activities.Design.Persistence.EntityFrameworkCore.AuthorityContract;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The provider-independent half of the <c>ContentAuthorityIsValid</c> contract: every provider must prove every
/// property the marker claims, and SQLite must agree with the shared case table. The other three providers run the
/// same table in the ProviderTests leg, against live databases.
/// </summary>
public sealed class ActivityAuthorityValidityTests
{
    public static TheoryData<string> Providers() => ["Sqlite", "SqlServer", "PostgreSql", "MySql"];

    private static IReadOnlyList<ActivityAuthorityClause> Clauses(string provider) => provider switch
    {
        "Sqlite" => ActivityAuthorityValiditySql.Sqlite(),
        "SqlServer" => ActivityAuthorityValiditySql.SqlServer(),
        "PostgreSql" => ActivityAuthorityValiditySql.PostgreSql(),
        "MySql" => ActivityAuthorityValiditySql.MySql(),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unknown provider.")
    };

    /// <summary>
    /// The whole point of the shared check list: a provider added later cannot quietly omit a property. Every member
    /// of <see cref="ActivityAuthorityCheck"/> is either emitted as SQL or recorded as proved by another clause.
    /// </summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_provider_proves_every_check(string provider)
    {
        var clauses = Clauses(provider);
        foreach (var check in ActivityAuthorityValiditySql.Checks)
        {
            var declared = clauses.Where(clause => clause.Check == check).ToArray();
            Assert.True(declared.Length > 0, $"{provider} declares nothing for {check}.");
            Assert.All(declared, clause => Assert.True(clause.Expression is not null || clause.ProvenBy is not null,
                $"{provider} declares {check} with neither SQL nor a covering clause."));
        }
    }

    /// <summary>A check delegated to another clause is only covered if that clause is itself emitted as SQL here.</summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_delegated_check_points_at_a_clause_that_this_provider_emits(string provider)
    {
        var clauses = Clauses(provider);
        var emitted = clauses.Where(clause => clause.Expression is not null).Select(clause => clause.Check).ToHashSet();
        foreach (var clause in clauses.Where(clause => clause.ProvenBy is not null))
        {
            Assert.True(emitted.Contains(clause.ProvenBy!.Value),
                $"{provider} says {clause.Check} is proved by {clause.ProvenBy}, which it never emits.");
            Assert.False(string.IsNullOrWhiteSpace(clause.Reason), $"{provider} gives no reason for delegating {clause.Check}.");
        }
    }

    /// <summary>The guard is the first thing evaluated, so a row whose JSON will not parse fails before any clause reads into it.</summary>
    [Theory]
    [MemberData(nameof(Providers))]
    public void Every_provider_guards_on_well_formed_json_before_any_body_clause(string provider)
    {
        var emitted = Clauses(provider).Where(clause => clause.Expression is not null).ToArray();
        Assert.Equal(ActivityAuthorityCheck.JsonIsWellFormed, emitted[0].Check);
        Assert.DoesNotContain(emitted.Skip(1), clause => clause.Check == ActivityAuthorityCheck.JsonIsWellFormed);
    }

    [Fact]
    public void Compose_refuses_a_clause_list_that_does_not_open_with_the_guard()
    {
        ActivityAuthorityClause[] clauses =
        [
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindInDomain, "1 = 1"),
            ActivityAuthorityClause.Sql(ActivityAuthorityCheck.JsonIsWellFormed, "1 = 1")
        ];
        Assert.Throws<InvalidOperationException>(() => ActivityAuthorityValiditySql.Compose(clauses, "1", "0"));
    }

    [Fact]
    public async Task Sqlite_agrees_with_the_cross_provider_authority_contract()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();

        await ActivityAuthorityValidityContract.RunAsync(db, ActivitiesDesignSqliteDbContext.ExpectedProviderName, "sqlite");
    }
}
