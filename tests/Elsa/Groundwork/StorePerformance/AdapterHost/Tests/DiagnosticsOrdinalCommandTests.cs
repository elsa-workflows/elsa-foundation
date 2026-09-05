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
    public static IEnumerable<object[]> Routes()
    {
        foreach (var provider in new[] { "sqlite", "postgresql", "sqlserver", "mongodb" })
        foreach (var route in new[]
                 {
                     "metrics-by-last-seen", "logs-by-last-seen",
                     "trace-detail/spans-by-trace-key-start-id", "trace-detail/logs-by-trace-key-timestamp-id"
                 })
        {
            yield return [provider, route, false];
            if (route.StartsWith("trace-detail/", StringComparison.Ordinal))
                yield return [provider, route, true];
        }
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void Published_renderer_command_is_admitted(string provider, string route, bool continuation)
    {
        var specification = Specification(route);
        var command = Render(provider, specification, continuation);
        output.WriteLine(command);
        Assert.Contains("__groundwork_ordinal_", command, StringComparison.Ordinal);
        Validate(provider, specification, command);
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void Physical_order_key_cannot_be_replaced_by_a_logical_or_unknown_column(string provider, string route, bool continuation)
    {
        var specification = Specification(route);
        var command = Render(provider, specification, continuation);
        var logical = route.Contains("spans-", StringComparison.Ordinal) ? "spanId" : "id";
        foreach (var replacement in new[] { logical, "__groundwork_ordinal_unknown" })
        {
            var invalid = command.Replace("__groundwork_ordinal_" + logical, replacement, StringComparison.Ordinal);
            Assert.NotEqual(command, invalid);
            Assert.Throws<PerformanceContractException>(() => Validate(provider, specification, invalid));
        }
    }

    [Fact]
    public void Mongo_cannot_overwrite_a_persisted_order_key_before_sorting()
    {
        var specification = Specification("metrics-by-last-seen");
        var command = JsonNode.Parse(Render("mongodb", specification, false))!.AsObject();
        command["pipeline"]!.AsArray().Insert(1, new JsonObject
        {
            ["$set"] = new JsonObject { ["__groundwork_ordinal_id"] = "constant" }
        });
        Assert.Throws<PerformanceContractException>(() => Validate("mongodb", specification, command.ToJsonString()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(127)]
    [InlineData(129)]
    public void Mongo_fetch_limit_is_exactly_the_public_page_plus_one_lookahead(int invalidLimit)
    {
        var specification = Specification("metrics-by-last-seen");
        var command = JsonNode.Parse(Render("mongodb", specification, false))!.AsObject();
        var limit = command["pipeline"]!.AsArray().Single(stage => stage!["$limit"] is not null)!;
        Assert.Equal(128, limit["$limit"]!.GetValue<int>());
        limit["$limit"] = invalidLimit;
        Assert.Throws<PerformanceContractException>(() => Validate("mongodb", specification, command.ToJsonString()));
    }

    [Fact]
    public void PostgreSql_persisted_cursor_comparisons_accept_parenthesized_columns()
    {
        var specification = Specification("trace-detail/spans-by-trace-key-start-id");
        const string command = """
            SELECT * FROM "elsa_otel_spans_v2"
            WHERE (("__groundwork_scope" COLLATE "C") = @scope AND ("traceKey" COLLATE "C") = @trace)
              AND ("startTime" > @time
                OR ("startTime" = @time AND ("__groundwork_ordinal_spanId" COLLATE "C") > @id)
                OR ("startTime" = @time AND ("__groundwork_ordinal_spanId" COLLATE "C") = @id AND "sequence" > @sequence))
            ORDER BY "startTime" ASC NULLS FIRST, ("__groundwork_ordinal_spanId" COLLATE "C") ASC NULLS FIRST,
                     "sequence" ASC NULLS FIRST LIMIT @limit
            """;
        Validate("postgresql", specification, command);
        Assert.Throws<PerformanceContractException>(() => Validate("postgresql", specification,
            command.Replace("COLLATE \"C\"", "COLLATE \"en-US\"", StringComparison.Ordinal)));
        Assert.Throws<PerformanceContractException>(() => Validate("postgresql", specification,
            command.Replace("AND \"sequence\" > @sequence", string.Empty, StringComparison.Ordinal)));
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

    private static string Render(string provider, DiagnosticsNativeRouteSpec specification, bool continuation)
    {
        var logical = V2OpenTelemetryStorageSchema.CreateUnits().Single(unit => unit.Name == specification.TableName);
        var physical = SearchKeyProjection.Expand(logical);
        var supplied = logical.CreateQueryRenderOptions(specification.IndexName);
        var options = supplied with
        {
            Indexes = SearchKeyQueryMappings.RetargetIndexes(physical, supplied.Indexes).ToImmutableArray(),
            SearchKeyColumns = SearchKeyQueryMappings.For(physical, specification.IndexName)
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
            ? new Predicate.Equal(Column(predicateColumn), QueryConstant.Of(Column(predicateColumn), new string('a', 64)))
            : Predicate.AlwaysTrue.Instance;
        if (provider != "mongodb")
        {
            var scope = new ColumnRef(table, "__groundwork_scope", QueryType.String, false, 128);
            var scopePredicate = new Predicate.Equal(scope, QueryConstant.Of(scope, "test-scope"));
            predicate = specification.PredicateColumn is null ? scopePredicate : new Predicate.And([scopePredicate, predicate]);
        }
        var request = new QueryRequest(table, predicate,
            specification.EffectiveOrdering.Select(term => new OrderTerm(Column(term.Column),
                term.Direction == RuntimeNativeOrderDirection.Ascending ? OrderDirection.Ascending : OrderDirection.Descending,
                NullOrder.Last)).ToImmutableArray(), Projection.All, Paging.Keyset(specification.FiniteLimit));
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

    private static void Validate(string provider, DiagnosticsNativeRouteSpec specification, string command)
    {
        var index = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(provider, specification);
        var table = specification.TableName;
        var plan = provider switch
        {
            "sqlite" => $"2 0 SEARCH {table} USING INDEX {index} (__groundwork_scope=?)",
            "postgresql" => $"[{{\"Plan\":{{\"Node Type\":\"Index Scan\",\"Relation Name\":\"{table}\",\"Index Name\":\"{index}\"}}}}]",
            "sqlserver" => $"<ShowPlanXML><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Table=\"[{table}]\" Index=\"[{index}]\" /></IndexScan></RelOp></ShowPlanXML>",
            "mongodb" => $"{{\"winningPlan\":{{\"stage\":\"IXSCAN\",\"indexName\":\"{index}\"}},\"command\":{command}}}",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        var artifact = new DiagnosticsNativePlanArtifact(1, provider, DiagnosticsNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity, table, specification.IndexName, index, command, plan);
        var directory = Directory.CreateTempSubdirectory("diagnostics-ordinal-command-");
        try
        {
            var path = Path.Combine(directory.FullName, "route.json");
            File.WriteAllText(path, JsonSerializer.Serialize(artifact));
            if (specification.RouteIdentity.StartsWith("trace-detail/", StringComparison.Ordinal))
            {
                var part = DiagnosticsNativePlanContract.TraceDetailConstituents(DiagnosticsNativePlanContract.GroundworkAdapter)
                    .Single(item => item.RouteIdentity == specification.RouteIdentity);
                var evidence = new DiagnosticsTraceDetailConstituentEvidence(part.RouteIdentity, "route.json",
                    ArtifactStore.HashFile(path), "index-search", index, command, part.PhysicalCardinality,
                    provider != "mongodb", true, part.FiniteLimit, part.PublicRowBound, part.PublicRowBound,
                    part.MaxInvocationCount, part.MaxInvocationCount);
                DiagnosticsNativePlanContract.ValidateTraceDetailConstituent(provider,
                    DiagnosticsNativePlanContract.GroundworkAdapter, evidence, path);
            }
            else
            {
                var evidence = new NativeRouteEvidence(specification.RouteIdentity, "route.json", ArtifactStore.HashFile(path),
                    "index-search", index, specification.PhysicalCardinality, provider != "mongodb", false,
                    specification.FiniteLimit, specification.FiniteLimit);
                DiagnosticsNativePlanContract.ValidateEnvelope(provider, DiagnosticsNativePlanContract.GroundworkAdapter, evidence, path);
            }
        }
        finally
        {
            directory.Delete(true);
        }
    }
}
