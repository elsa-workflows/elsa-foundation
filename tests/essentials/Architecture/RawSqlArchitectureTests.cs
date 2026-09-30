using System.Text.RegularExpressions;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// ADR 0075 says Elsa 4 writes almost no provider-specific SQL, and #2162 added the one place a store writes it: the
/// insert-unless-present statement for the two startup seeds, which SaveChanges cannot send without EF logging a failure
/// for the loser (ADR 0073, D3, "The Secrets recipe is the starting point, not a universal Runtime design"). Raw SQL
/// spreads by example, so this guard names every production file that sends any: EF's raw entry points
/// (<c>ExecuteSql*</c>, <c>FromSql*</c>, <c>SqlQuery*</c>), <c>migrationBuilder.Sql</c>, and a hand-built ADO
/// <c>CommandText</c>. A new site is a decision, made by adding its file below with the reason, not a side effect.
/// <para>
/// The scan covers <c>src/</c> in full, migrations included, because the ADR's claim does; it leaves each extension's own
/// <c>tests/</c> alone, as every production guard does (<see cref="ModuleRoots.ProductionSourceFiles"/>). Comment lines are
/// not code. The distributed Runtime adapter's own <c>FromSql</c> ban
/// (<c>RuntimeExecutionPlacementPersistenceArchitectureTests</c>) is the narrower form of this rule and is covered by it.
/// </para>
/// </summary>
public sealed partial class RawSqlArchitectureTests
{
    /// <summary>Production files allowed to send raw SQL, each with why.</summary>
    private static readonly Dictionary<string, string> Sites = new(StringComparer.Ordinal)
    {
        ["src/essentials/Persistence/EntityFramework/SchemaFinalization/EfInsertIfAbsent.cs"] =
            "the startup seeds' insert-unless-present statement, one shape per engine (#2162)",
        ["src/essentials/Persistence/EntityFramework/SchemaFinalization/EfSchemaFinalizationCheck.cs"] =
            "the read-only catalog lookup that finds a record table without its migrations history (spec 171, FR-069)"
    };

    [Fact]
    public void Raw_sql_is_sent_only_from_the_declared_sites()
    {
        var sites = FindSites(ProductionSources());

        var undeclared = sites.Except(Sites.Keys, StringComparer.Ordinal).ToArray();
        Assert.True(
            undeclared.Length == 0,
            "Raw SQL is sent from a file that is not a declared site. Use EF's model, or, for a race-prone write that only " +
            $"the engine can order, extend EfInsertIfAbsent, or declare the site in this test with its reason:{Environment.NewLine}" +
            string.Join(Environment.NewLine, undeclared));
    }

    /// <summary>The rule passes vacuously if the scan stops finding raw SQL, so every declared site must still send some.</summary>
    [Fact]
    public void Every_declared_site_still_sends_raw_sql()
    {
        var sites = FindSites(ProductionSources());

        Assert.Empty(Sites.Keys.Except(sites, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("await context.Database.ExecuteSqlRawAsync(sql, values);", true)]
    [InlineData("await context.Database.ExecuteSqlAsync($\"delete from t\");", true)]
    [InlineData("var rows = set.FromSqlInterpolated($\"select * from t\");", true)]
    [InlineData("var rows = context.Database.SqlQueryRaw<int>(sql);", true)]
    [InlineData("migrationBuilder.Sql(\"update t set a = 1\");", true)]
    [InlineData("command.CommandText = sql;", true)]
    [InlineData("// migrationBuilder.Sql is not used anywhere in src/.", false)]
    [InlineData("/// <see cref=\"DbContext.ExecuteSqlRaw\"/> is not used.", false)]
    [InlineData("ActivityAuthorityClause.Sql(ActivityAuthorityCheck.KindInDomain, sql);", false)]
    [InlineData("var name = nameof(ExecuteSqlHelper);", false)]
    public void The_detector_finds_raw_sql_calls_and_skips_comments_and_lookalikes(string line, bool sendsRawSql) =>
        Assert.Equal(sendsRawSql, SendsRawSql(line));

    private static IEnumerable<(string Path, string Text)> ProductionSources() =>
        ModuleRoots.ProductionSourceFiles(RepoRoot)
            .Select(file => (Path: Path.GetRelativePath(RepoRoot, file).Replace(Path.DirectorySeparatorChar, '/'), Text: File.ReadAllText(file)));

    private static string[] FindSites(IEnumerable<(string Path, string Text)> sources) =>
        [.. sources.Where(source => source.Text.Split('\n').Any(SendsRawSql)).Select(source => source.Path).Order(StringComparer.Ordinal)];

    private static bool SendsRawSql(string line) => !line.TrimStart().StartsWith("//", StringComparison.Ordinal) && RawSql().IsMatch(line);

    [GeneratedRegex(@"\b(?:ExecuteSql(?:Raw|Interpolated)?(?:Async)?|FromSql(?:Raw|Interpolated)?|SqlQuery(?:Raw)?)\s*[<(]|\bmigrationBuilder\s*\.\s*Sql\s*\(|\.CommandText\s*=")]
    private static partial Regex RawSql();
}
