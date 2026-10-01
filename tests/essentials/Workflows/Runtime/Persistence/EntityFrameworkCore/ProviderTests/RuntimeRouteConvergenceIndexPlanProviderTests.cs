using System.Data.Common;
using System.Globalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(RuntimeBookmarksPostgreSqlFixture.CollectionName)]
public sealed class RuntimeRouteConvergenceIndexPlanPostgreSqlTests(RuntimeBookmarksPostgreSqlFixture fixture)
{
    [SkippableFact]
    public Task PostgreSql_convergence_projections_are_index_only_scans_of_their_covering_indexes() =>
        RuntimeRouteConvergenceIndexPlans.RunAsync(
            fixture,
            interceptor => new RuntimePostgreSqlDbContext(new DbContextOptionsBuilder<RuntimePostgreSqlDbContext>()
                .UseNpgsql(fixture.ConnectionString).AddInterceptors(interceptor).Options),
            RuntimeRouteConvergenceIndexPlans.PostgreSqlPlanAsync,
            (plan, index) => Assert.Contains($"Index Only Scan using \"{index}\"", plan));
}

[Collection(RuntimeBookmarksSqlServerFixture.CollectionName)]
public sealed class RuntimeRouteConvergenceIndexPlanSqlServerTests(RuntimeBookmarksSqlServerFixture fixture)
{
    [SkippableFact]
    public Task SqlServer_convergence_projections_seek_their_covering_indexes_without_a_lookup() =>
        RuntimeRouteConvergenceIndexPlans.RunAsync(
            fixture,
            interceptor => new RuntimeSqlServerDbContext(new DbContextOptionsBuilder<RuntimeSqlServerDbContext>()
                .UseSqlServer(fixture.ConnectionString).AddInterceptors(interceptor).Options),
            RuntimeRouteConvergenceIndexPlans.SqlServerPlanAsync,
            (plan, index) =>
            {
                Assert.Contains($"Index=\"[{index}]\"", plan);
                Assert.DoesNotContain("Lookup=\"true\"", plan);
            });
}

/// <summary>
/// The HTTP route-table convergence check (#2190) runs both projections on every node every interval, so each must be
/// answered from its covering index alone, never from the rows behind it. The SQL each store sends is captured and its
/// plan read back from the engine. Most rows belong to another stimulus type, so the index has a range to seek.
/// PostgreSQL is vacuumed so the planner may count on an index-only scan, and steered off sequential and bitmap scans,
/// which a table this small would otherwise win.
/// </summary>
internal static class RuntimeRouteConvergenceIndexPlans
{
    private const string RouteType = "HttpEndpoint";
    private const string OtherType = "Event";
    private static readonly DateTimeOffset CreatedAt = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    public static async Task RunAsync(
        RuntimeBookmarksProviderFixture fixture,
        Func<IInterceptor, RuntimeDbContext> createContext,
        Func<DbConnection, CapturedQuery, string, Task<string>> readPlan,
        Action<string, string> assertPlan)
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "The native provider is unavailable.");
        var capture = new DistinctQueryCapture();
        await using var context = createContext(capture);
        await context.Database.EnsureCreatedAsync();
        var accessor = new FixedAccessor($"route-plan-{Guid.NewGuid():N}");
        IBookmarkStimulusIndex bookmarks = new EfBookmarkStateStore(context, accessor);
        IWorkflowTriggerBindingStore bindings = new EfWorkflowTriggerBindingStore(context, accessor);
        await SeedAsync((IBookmarkStateStore)bookmarks, bindings);

        Assert.Equal(10, (await bookmarks.ListWaitingStimulusHashesByTypeAsync(RouteType, CreatedAt)).Count);
        var bookmarkQuery = capture.Take(BookmarkStateEfModule.TableName);
        Assert.Equal(10, (await bindings.ListActiveStimulusHashesAsync(RouteType)).Count);
        var bindingQuery = capture.Take(RuntimeTriggerBindingEfModule.TableName);

        await context.Database.OpenConnectionAsync();
        var connection = context.Database.GetDbConnection();
        assertPlan(await readPlan(connection, bookmarkQuery, BookmarkStateEfModule.TableName), BookmarkStateEfModule.RouteConvergenceIndexName);
        assertPlan(await readPlan(connection, bindingQuery, RuntimeTriggerBindingEfModule.TableName), RuntimeTriggerBindingEfModule.RouteConvergenceIndexName);
    }

    public static async Task<string> PostgreSqlPlanAsync(DbConnection connection, CapturedQuery query, string table)
    {
        await ExecuteAsync(connection, $"VACUUM ANALYZE \"{table}\"");
        await ExecuteAsync(connection, "SET enable_seqscan = off; SET enable_bitmapscan = off");
        try
        {
            return string.Join(Environment.NewLine, await ReadAllAsync(connection, "EXPLAIN " + query.Inline(literal => $"'{literal}'")));
        }
        finally
        {
            await ExecuteAsync(connection, "RESET enable_seqscan; RESET enable_bitmapscan");
        }
    }

    public static async Task<string> SqlServerPlanAsync(DbConnection connection, CapturedQuery query, string table)
    {
        await ExecuteAsync(connection, "SET SHOWPLAN_XML ON");
        try
        {
            return string.Concat(await ReadAllAsync(connection, query.Inline(literal => $"N'{literal}'")));
        }
        finally
        {
            await ExecuteAsync(connection, "SET SHOWPLAN_XML OFF");
        }
    }

    private static async Task SeedAsync(IBookmarkStateStore bookmarks, IWorkflowTriggerBindingStore bindings)
    {
        for (var index = 0; index < 100; index++)
        {
            var type = index < 10 ? RouteType : OtherType;
            var hash = $"sha256:{type}:{index:D3}";
            await bookmarks.SaveAsync(new BookmarkState(
                $"bookmark-{index:D3}", $"workflow-{index:D3}", "activity", "node", "resume", type, hash,
                null, new Dictionary<string, string>(), CreatedAt, null));
            await bindings.SaveAsync(new WorkflowTriggerBinding(
                WorkflowTriggerBinding.BuildId($"artifact-{index:D3}", "node", hash), $"artifact-{index:D3}", "definition", "1",
                "artifact-hash", "node", type, hash, null, new Dictionary<string, string>(), CreatedAt));
        }
    }

    private static async Task ExecuteAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<IReadOnlyList<string>> ReadAllAsync(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync())
            rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        return rows;
    }

    /// <summary>A projection's SQL and its parameter values, which are hashes and ticks.</summary>
    public sealed record CapturedQuery(string Sql, IReadOnlyList<(string Name, object? Value)> Parameters)
    {
        /// <summary>The SQL with each parameter replaced by its literal, longest name first so <c>@p1</c> never eats <c>@p10</c>.</summary>
        public string Inline(Func<string, string> quote) =>
            Parameters.OrderByDescending(parameter => parameter.Name.Length).Aggregate(Sql, (sql, parameter) => sql.Replace(
                parameter.Name,
                parameter.Value is string text ? quote(text.Replace("'", "''")) : Convert.ToString(parameter.Value, CultureInfo.InvariantCulture)));
    }

    private sealed class DistinctQueryCapture : DbCommandInterceptor
    {
        private readonly List<CapturedQuery> _captured = [];

        public CapturedQuery Take(string table)
        {
            var query = Assert.Single(_captured, candidate => candidate.Sql.Contains(table, StringComparison.Ordinal));
            _captured.Clear();
            return query;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DISTINCT", StringComparison.Ordinal))
                _captured.Add(new CapturedQuery(
                    command.CommandText,
                    command.Parameters.Cast<DbParameter>()
                        .Select(parameter => (Name: parameter.ParameterName.StartsWith('@') ? parameter.ParameterName : "@" + parameter.ParameterName, parameter.Value))
                        .ToArray()));
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FixedAccessor(string scope) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new PersistenceScope(scope));
    }
}
