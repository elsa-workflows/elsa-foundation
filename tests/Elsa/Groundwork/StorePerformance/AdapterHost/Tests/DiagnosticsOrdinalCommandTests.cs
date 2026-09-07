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
                    "resources-by-last-seen", "resources-by-status", "resources-by-service",
                    "metrics-by-last-seen", "logs-by-last-seen", "traces-by-last-seen",
                    "trace-detail/spans-by-trace-key-start-id", "trace-detail/logs-by-trace-key-timestamp-id"
                })
        {
            yield return [provider, route, false];
            if (route.StartsWith("trace-detail/", StringComparison.Ordinal))
                yield return [provider, route, true];
        }
    }

    public static IEnumerable<object[]> OrdinalRoutes() =>
        Routes().Where(row => !((string)row[1]).StartsWith("resources-", StringComparison.Ordinal));

    [Theory]
    [MemberData(nameof(Routes))]
    public void Published_renderer_command_is_admitted(string provider, string route, bool continuation)
    {
        var specification = Specification(route);
        var command = Render(provider, specification, continuation);
        output.WriteLine(command);
        if (route.StartsWith("resources-", StringComparison.Ordinal))
            Assert.DoesNotContain("__groundwork_ordinal_", command, StringComparison.Ordinal);
        else
            Assert.Contains("__groundwork_ordinal_", command, StringComparison.Ordinal);
        if (route == "traces-by-last-seen")
        {
            Assert.Contains("__groundwork_ordinal_traceKey", command, StringComparison.Ordinal);
            Assert.DoesNotContain("string_agg", command, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("unnest", command, StringComparison.OrdinalIgnoreCase);
        }
        Validate(provider, specification, command);
    }

    [Theory]
    [MemberData(nameof(OrdinalRoutes))]
    public void Physical_order_key_cannot_be_replaced_by_a_logical_or_unknown_column(string provider, string route, bool continuation)
    {
        var specification = Specification(route);
        var command = Render(provider, specification, continuation);
        var logical = route.Contains("spans-", StringComparison.Ordinal)
            ? "spanId"
            : route == "traces-by-last-seen" ? "traceKey" : "id";
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

    [Fact]
    public void PostgreSql_real_bounded_catalog_plan_is_normalized_before_serialized_admission()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");
        var rawPlan = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "postgresql-bounded-resources-by-last-seen.json"));
        var normalizedPlan = IamNativePlanParser.NormalizeForArtifact("postgresql", rawPlan);
        var command = Render("postgresql", specification, continuation: false);
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("postgresql", specification);
        var artifact = new DiagnosticsNativePlanArtifact(
            1,
            "postgresql",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            physicalIndex,
            command,
            normalizedPlan);
        var directory = Directory.CreateTempSubdirectory("diagnostics-postgresql-normalized-plan-");
        try
        {
            var path = Path.Combine(directory.FullName, "route.json");
            File.WriteAllText(path, JsonSerializer.Serialize(artifact));
            var evidence = new NativeRouteEvidence(
                specification.RouteIdentity,
                "route.json",
                ArtifactStore.HashFile(path),
                DiagnosticsNativePlanContract.BoundedCatalogScanSortPlanClassification,
                physicalIndex,
                specification.PhysicalCardinality,
                true,
                false,
                specification.FiniteLimit,
                specification.FiniteLimit);

            DiagnosticsNativePlanContract.ValidateEnvelope(
                "postgresql",
                artifact.Adapter,
                evidence,
                path);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    [Theory]
    [InlineData("unknown-helper")]
    [InlineData("payload-removal")]
    [InlineData("helper-inclusion")]
    [InlineData("additional-stage")]
    public void Mongo_resource_helper_projection_must_remove_only_rendered_order_helpers(string mutation)
    {
        var specification = Specification("resources-by-last-seen");
        var command = JsonNode.Parse(Render("mongodb", specification, false))!.AsObject();
        var pipeline = command["pipeline"]!.AsArray();
        var projection = pipeline
            .Single(stage => stage!["$project"] is not null)!["$project"]!.AsObject();
        switch (mutation)
        {
            case "unknown-helper":
                projection["_groundwork_ordinal_unknown"] = 0;
                break;
            case "payload-removal":
                projection["id"] = 0;
                break;
            case "helper-inclusion":
                projection[projection.First().Key] = 1;
                break;
            case "additional-stage":
                pipeline.Insert(pipeline.Count - 1, new JsonObject { ["$skip"] = 1 });
                break;
        }

        Assert.Throws<PerformanceContractException>(() => Validate("mongodb", specification, command.ToJsonString()));
    }

    [Fact]
    public void Mongo_resource_helper_projection_cannot_repeat_one_helper_in_place_of_another()
    {
        var specification = Specification("resources-by-last-seen");
        var command = JsonNode.Parse(Render("mongodb", specification, false))!.AsObject();
        var projection = command["pipeline"]!.AsArray()
            .Single(stage => stage!["$project"] is not null)!["$project"]!.AsObject();
        Assert.True(projection.Count > 1);
        var repeatedField = JsonSerializer.Serialize(projection.First().Key) + ":0";
        var duplicateProjection = "{" + string.Join(",", Enumerable.Repeat(repeatedField, projection.Count)) + "}";
        var malformed = command.ToJsonString().Replace(projection.ToJsonString(), duplicateProjection, StringComparison.Ordinal);

        Assert.Throws<PerformanceContractException>(() => Validate("mongodb", specification, malformed));
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

    [Theory]
    [InlineData("column-order")]
    [InlineData("operator")]
    [InlineData("parameter-reuse")]
    [InlineData("extra-condition")]
    [InlineData("tuple-collation")]
    [InlineData("tuple-collation-missing")]
    [InlineData("scope-collation")]
    [InlineData("route-collation")]
    [InlineData("malformed-conjunction")]
    [InlineData("unbalanced-parentheses")]
    public void PostgreSql_tuple_cursor_comparison_rejects_non_equivalent_shapes(string mutation)
    {
        var specification = Specification("trace-detail/spans-by-trace-key-start-id");
        var command = Render("postgresql", specification, continuation: true);
        var orderIndex = command.IndexOf(" ORDER BY ", StringComparison.Ordinal);
        Assert.True(orderIndex > 0);
        var predicateCommand = command[..orderIndex];
        var invalid = mutation switch
        {
            "column-order" => command.Replace(
                "(\"startTime\", (\"__groundwork_ordinal_spanId\" COLLATE \"C\"), \"sequence\")",
                "(\"__groundwork_ordinal_spanId\" COLLATE \"C\", \"startTime\", \"sequence\")",
                StringComparison.Ordinal),
            "operator" => command.Replace(") > (", ") < (", StringComparison.Ordinal),
            "parameter-reuse" => command.Replace("@p4", "@p1", StringComparison.Ordinal),
            "extra-condition" => command.Replace(
                " ORDER BY ",
                " AND \"sequence\" = @p6 ORDER BY ",
                StringComparison.Ordinal),
            "tuple-collation" => predicateCommand.Replace(
                "(\"__groundwork_ordinal_spanId\" COLLATE \"C\")",
                "(\"__groundwork_ordinal_spanId\" COLLATE \"POSIX\")",
                StringComparison.Ordinal) + command[orderIndex..],
            "tuple-collation-missing" => predicateCommand.Replace(
                "(\"__groundwork_ordinal_spanId\" COLLATE \"C\")",
                "\"__groundwork_ordinal_spanId\"",
                StringComparison.Ordinal) + command[orderIndex..],
            "scope-collation" => command.Replace(
                "\"__groundwork_scope\" COLLATE \"C\"",
                "\"__groundwork_scope\" COLLATE \"POSIX\"",
                StringComparison.Ordinal),
            "route-collation" => command.Replace(
                "\"traceKey\" COLLATE \"C\"",
                "\"traceKey\" COLLATE \"POSIX\"",
                StringComparison.Ordinal),
            "malformed-conjunction" => command.Replace(
                " AND ",
                " AND AND ",
                StringComparison.Ordinal),
            "unbalanced-parentheses" => command.Replace(" WHERE ", " WHERE ) ", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };

        Assert.NotEqual(command, invalid);
        Assert.Throws<PerformanceContractException>(() => Validate("postgresql", specification, invalid));
    }

    [Fact]
    public void Trace_summary_unselected_renderer_keeps_the_raw_trace_key_order()
    {
        var specification = Specification("traces-by-last-seen");
        var selected = Render("postgresql", specification, continuation: false);
        var unselected = Render("postgresql", specification, continuation: false, selectIndex: false);

        Assert.Contains("__groundwork_ordinal_traceKey", selected, StringComparison.Ordinal);
        Assert.DoesNotContain("string_agg", selected, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("__groundwork_ordinal_traceKey", unselected, StringComparison.Ordinal);
        Assert.Contains("traceKey", unselected, StringComparison.Ordinal);
        Assert.Contains("string_agg", unselected, StringComparison.OrdinalIgnoreCase);
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
                    "index-search", index, specification.PhysicalCardinality, provider != "mongodb",
                    specification.PredicateColumn is not null,
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
