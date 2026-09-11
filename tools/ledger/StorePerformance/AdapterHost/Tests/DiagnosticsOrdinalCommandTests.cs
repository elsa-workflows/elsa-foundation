using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Diagnostics.OpenTelemetry.Persistence.Groundwork;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using MongoDB.Bson;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

/// <summary>Real package renderer output through Elsa's admission boundary, without a live server.
/// The plan fixtures isolate command compatibility; they are not native-plan or timing evidence.</summary>
public sealed class DiagnosticsOrdinalCommandTests(ITestOutputHelper output)
{
    /// <summary>
    /// The start index declares ordinal identities, so the persisted trace key orders the query whether
    /// or not it is nominated (valence-works/groundwork-v2#443); no computed ordinal key appears.
    /// </summary>
    [Fact]
    public void Trace_summary_renderer_orders_by_the_persisted_trace_key_with_or_without_a_selected_index()
    {
        var specification = Specification("traces-by-last-seen");
        foreach (var selectIndex in new[] { true, false })
        {
            var rendered = Render("postgresql", specification, continuation: false, selectIndex: selectIndex);
            Assert.Contains("__groundwork_ordinal_traceKey", rendered, StringComparison.Ordinal);
            Assert.DoesNotContain("string_agg", rendered, StringComparison.OrdinalIgnoreCase);
        }
    }

    private static DiagnosticsNativeRouteSpec Specification(string route)
    {
        if (!route.StartsWith("trace-detail/", StringComparison.Ordinal))
            return DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, route);
        var part = DiagnosticsNativePlanContract.TraceDetailConstituents(DiagnosticsNativePlanContract.GroundworkAdapter)
            .Single(item => item.RouteIdentity == route);
        return new DiagnosticsNativeRouteSpec(route, part.TableName, part.IndexName, part.Ordering[0].Column,
            part.PredicateColumn, part.PhysicalCardinality, part.FiniteLimit, part.StorageScopeRequired,
            false, part.Ordering, []);
    }

    private static string Render(
        string provider,
        DiagnosticsNativeRouteSpec specification,
        bool continuation,
        bool selectIndex = true)
    {
        var logical = V2OpenTelemetryStorageSchema.CreateUnits().Single(unit => unit.Name == specification.TableName);
        var physical = SearchKeyProjection.Expand(logical);
        var selectedIndex = selectIndex ? specification.IndexName : null;
        var supplied = logical.CreateQueryRenderOptions(selectedIndex);
        var options = supplied with
        {
            Indexes = SearchKeyQueryMappings.RetargetIndexes(physical, supplied.Indexes).ToImmutableArray(),
            SearchKeyColumns = SearchKeyQueryMappings.For(physical, selectedIndex)
        };
        var table = new TableId(logical.Name);
        ColumnRef Column(string name)
        {
            var column = logical.Columns.Single(item => item.Name == name);
            var type = column.Type switch
            {
                PortableType.String => QueryType.String,
                PortableType.DateTimeOffset => QueryType.DateTimeOffset,
                PortableType.Int64 => QueryType.Int64,
                _ => throw new InvalidOperationException("Unexpected signal ordering type.")
            };
            return new ColumnRef(table, name, type, column.IsNullable, column.MaxLength);
        }

        Predicate predicate = specification.PredicateColumn is { } predicateColumn
            ? new Predicate.Equal(
                Column(predicateColumn),
                QueryConstant.Of(
                    Column(predicateColumn),
                    Column(predicateColumn).Type == QueryType.Int64 ? 1L : new string('a', 64)))
            : Predicate.AlwaysTrue.Instance;
        if (provider != "mongodb")
        {
            var scope = new ColumnRef(table, "__groundwork_scope", QueryType.String, false, 128);
            var scopePredicate = new Predicate.Equal(scope, QueryConstant.Of(scope, "test-scope"));
            predicate = specification.PredicateColumn is null ? scopePredicate : new Predicate.And([scopePredicate, predicate]);
        }
        var request = new QueryRequest(table, predicate,
            specification.EffectiveOrdering.Select((term, index) => new OrderTerm(Column(term.Column),
                term.Direction == RuntimeNativeOrderDirection.Ascending ? OrderDirection.Ascending : OrderDirection.Descending,
                index == 0 && specification.Descending ? NullOrder.First : NullOrder.Last)).ToImmutableArray(),
            Projection.All, Paging.Keyset(specification.FiniteLimit));
        var execution = QueryRequestExecution.ForProviderPage(request, options);
        if (continuation)
        {
            var rewritten = QuerySearchKeyRewriter.Rewrite(execution, options.SearchKeyColumns);
            var values = options.GetEffectiveOrder(rewritten).Select(term => QueryConstant.Of(term.Column,
                term.Column.Type switch
                {
                    QueryType.DateTimeOffset => (object)new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                    QueryType.Int64 => 7L,
                    QueryType.String => PortableStringComparison.CreateOrdinal("cursor-id"),
                    _ => throw new InvalidOperationException("Unexpected cursor type.")
                }));
            var token = QueryContinuationToken.Encode(rewritten, options, values);
            var continued = new QueryRequest(request.Table, request.Where, request.Order, request.Projection,
                Paging.Continuation(token, specification.FiniteLimit));
            execution = QueryRequestExecution.ForProviderPage(continued, options);
        }
        return provider switch
        {
            "sqlite" => new SqliteQueryRenderer().Render(execution, options).CommandText,
            "postgresql" => new PostgreSqlQueryRenderer().Render(execution, options).CommandText,
            "sqlserver" => new SqlServerQueryRenderer().Render(execution, options).CommandText,
            "mongodb" => MongoCommand(execution, options, specification.TableName),
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private static string MongoCommand(QueryRequest request, QueryRenderOptions options, string table)
    {
        var collection = table + "__scope__" + new string('A', 64);
        var command = new MongoQueryRenderer().Render(request, options, collection);
        return new BsonDocument
        {
            ["aggregate"] = collection,
            ["pipeline"] = new BsonArray(command.Pipeline),
            ["cursor"] = new BsonDocument()
        }.ToJson();
    }
}
