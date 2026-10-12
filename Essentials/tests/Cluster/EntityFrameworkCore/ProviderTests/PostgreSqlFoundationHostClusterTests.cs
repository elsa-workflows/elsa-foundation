using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore.Tests;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit.Abstractions;

namespace Elsa.Cluster.EntityFrameworkCore.ProviderTests;

/// <summary>One built-host feed and one PostgreSQL container for the two-host boot, which packs and starts what the fast gate does.</summary>
[CollectionDefinition(Name)]
public sealed class PostgreSqlFoundationHostCollection : ICollectionFixture<FoundationHostFeed>, ICollectionFixture<PostgreSqlMembershipDatabase>
{
    public const string Name = "cluster-foundation-host-postgresql";
}

/// <summary>
/// #2151's acceptance on PostgreSQL, the engine the customer demo runs two hosts on: the scenario of
/// <see cref="FoundationHostClusterScenario"/> over a database of its own in the container. Two hosts that share it see
/// each other, the new version is finalized only once both can read it, and both then serve. Without Docker it skips, unless
/// <see cref="MembershipDatabase.RequireVariable"/> is set.
/// </summary>
/// <remarks>
/// It also writes down which engine assemblies each host has loaded and from where, because the host carries Npgsql and the
/// EF provider for its membership while Nuplane injects the engine package the <c>ef-provider</c> selection names into the
/// module's graph (ADR 0076, amended 2026-09-29).
/// </remarks>
[Collection(PostgreSqlFoundationHostCollection.Name)]
public sealed class PostgreSqlFoundationHostClusterTests(FoundationHostFeed feed, PostgreSqlMembershipDatabase database, ITestOutputHelper output)
    : FoundationHostClusterScenario(feed, "PostgreSql")
{
    private const string Fixture = "Elsa.Cluster.Fixtures.FeedModule", Engine = "Npgsql.EntityFrameworkCore.PostgreSQL";
    private string _connectionString = "";

    protected override string? SkipReason => database.Store.SkipReason;

    protected override string ConnectionString => _connectionString;

    private protected override FeedModuleDatabase Database => new((builder, connectionString) => builder.UseNpgsql(connectionString), ConnectionString);

    /// <summary>A database of this test's own, so nothing another suite in the container left is seen.</summary>
    public override async Task InitializeAsync()
    {
        if (SkipReason is not null)
            return;

        var name = $"elsa_hosts_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(database.Store.ConnectionString) { Pooling = false }.ConnectionString))
        {
            await admin.OpenAsync();
            await using var command = admin.CreateCommand();
            command.CommandText = $"CREATE DATABASE \"{name}\"";
            await command.ExecuteNonQueryAsync();
        }

        _connectionString = new NpgsqlConnectionStringBuilder(database.Store.ConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }

    protected override async Task<Dictionary<string, string[]>> ReadableByMemberAsync()
    {
        await using var context = new ClusterMembershipPostgreSqlDbContext(
            new DbContextOptionsBuilder<ClusterMembershipPostgreSqlDbContext>().UseNpgsql(ConnectionString).Options);
        try
        {
            return (await context.Members.AsNoTracking().Where(member => member.CurrentHostId != null && member.LeftAtUtcTicks == null).ToListAsync())
                .ToDictionary(member => member.HostId, member => ReadableVersions(member.ReportJson));
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            return []; // No host has composed the provider yet, so none has created its table.
        }
    }

    private protected override async Task AssertBindsTheHostsEfClosureAsync(FoundationHostProcess host)
    {
        var active = (await host.ActivePackagesAsync()).Keys.ToArray();
        output.WriteLine($"Acquired from the feeds: {string.Join(", ", active.Order(StringComparer.OrdinalIgnoreCase))}");
        var mapped = (await host.MappedAssembliesAsync())
            .Where(path => new[] { "Npgsql", "EntityFramework" }.Any(part => Path.GetFileName(path).Contains(part, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        foreach (var path in mapped)
            output.WriteLine($"Mapped: {path}");

        // One copy each of the driver, EF Core and the EF persistence package. The engine assembly is the exception: Nuplane
        // injects the engine package the ef-provider selection names into the module's graph, so a second copy of it is
        // mapped beside the host's, and the module binds the host's by name (ADR 0076, amended 2026-09-29). That the two
        // hosts finalize and serve with it is what shows the second copy does no harm.
        Assert.All(
            new[] { "Npgsql.dll", "Microsoft.EntityFrameworkCore.dll", "Microsoft.EntityFrameworkCore.Relational.dll", "Elsa.Persistence.EntityFramework.dll" },
            name => Assert.Single(mapped, path => Path.GetFileName(path) == name));

        Assert.Contains(Fixture, active, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(Engine, active, StringComparer.OrdinalIgnoreCase);
        string[] carried = ["Elsa.", "Microsoft.EntityFrameworkCore", "Npgsql"];
        Assert.Empty(active
            .Where(id => carried.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .Except([Fixture, Engine], StringComparer.OrdinalIgnoreCase));
    }
}
