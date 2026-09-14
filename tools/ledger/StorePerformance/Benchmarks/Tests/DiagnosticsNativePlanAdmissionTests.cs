using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class DiagnosticsNativePlanAdmissionTests
{
    private const string MongoOrdinalKeyFunctionBody =
        "function(value) { if (value === null || value === undefined) return null; var key = ''; for (var i = 0; i < value.length; i++) { var unit = value.charCodeAt(i).toString(16); key += ('0000' + unit).slice(-4); } return key; }";

    [Fact]
    public void Current_route_contract_admits_only_declared_order_covering_indexes()
    {
        var resource = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-last-seen");
        var trace = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "traces-by-last-seen");

        Assert.Equal(("elsa_otel_resources_v2", "elsa_otel_resources_last_seen"), (resource.TableName, resource.IndexName));
        Assert.NotNull(resource.NullableOrderingColumns);
        Assert.Empty(resource.NullableOrderingColumns!);
        Assert.False(resource.RequiresNullRank("lastSeen"));
        Assert.Equal(("elsa_otel_trace_summaries_v3", "elsa_otel_trace_summaries_start"), (trace.TableName, trace.IndexName));
        Assert.Equal("elsa_otel_resources_status_last_seen", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-status").IndexName);
        Assert.Equal("elsa_otel_resources_service_last_seen", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "resources-by-service").IndexName);
        Assert.Equal("elsa_otel_metric_points_timestamp", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "metrics-by-last-seen").IndexName);
        Assert.Equal("elsa_otel_logs_timestamp", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "logs-by-last-seen").IndexName);
        Assert.Equal("elsa_structured_logs_sequence_order", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "structured-log-recent").IndexName);
        Assert.Equal("elsa_structured_logs_sequence_order", DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, "structured-log-replay").IndexName);
        Assert.Equal(8, DiagnosticsDurableHistoryWorkload.NativeRouteLimits.Keys.Count(route =>
            !string.IsNullOrWhiteSpace(DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, route).IndexName)));
    }

    [Fact]
    public void Groundwork_indexes_bind_logical_names_to_provider_physical_names()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");

        Assert.Equal(specification.IndexName,
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("mongodb", specification));
        Assert.StartsWith("__groundwork_ix_", DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlite", specification));
        Assert.NotEqual(specification.IndexName,
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("postgresql", specification));
        Assert.NotEqual(specification.IndexName,
            DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlserver", specification));
    }

    [Fact]
    public void Fanout_unindexed_and_order_materializing_routes_are_explicitly_blocked()
    {
        var blocked = new[]
        {
            "trace-detail"
        };

        Assert.All(blocked, route =>
            Assert.Empty(DiagnosticsNativePlanContract.For(
                DiagnosticsNativePlanContract.GroundworkAdapter,
                route).IndexName));
    }

    [Fact]
    public void Trace_detail_has_independent_bounded_constituents_including_primary_key_fanout()
    {
        var constituents = DiagnosticsNativePlanContract.TraceDetailConstituents(
            DiagnosticsNativePlanContract.GroundworkAdapter);

        Assert.Equal(
            [
                "trace-detail/summary-by-trace-key",
                "trace-detail/spans-by-trace-key-start-id",
                "trace-detail/logs-by-trace-key-timestamp-id",
                "trace-detail/resources-by-id"
            ],
            constituents.Select(constituent => constituent.RouteIdentity));

        Assert.Equal(DiagnosticsTraceDetailOperationKind.PrimaryKeyRead, constituents[0].OperationKind);
        Assert.Equal("elsa_otel_trace_summaries_v3", constituents[0].TableName);
        Assert.Empty(constituents[0].IndexName);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[0].PhysicalCardinality);
        Assert.Equal(1, constituents[0].FiniteLimit);
        Assert.Equal(1, constituents[0].PublicRowBound);

        Assert.Equal(DiagnosticsTraceDetailOperationKind.BoundedOrderedQuery, constituents[1].OperationKind);
        Assert.Equal("elsa_otel_spans_trace_detail", constituents[1].IndexName);
        Assert.Equal(
            [
                new RuntimeNativeOrderTerm("startTime", RuntimeNativeOrderDirection.Ascending),
                new RuntimeNativeOrderTerm("spanId", RuntimeNativeOrderDirection.Ascending),
                new RuntimeNativeOrderTerm("sequence", RuntimeNativeOrderDirection.Ascending)
            ],
            constituents[1].Ordering);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[1].PublicRowBound);
        Assert.Equal((DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream + DiagnosticsDurableHistoryWorkload.QueryLimit - 1) / DiagnosticsDurableHistoryWorkload.QueryLimit, constituents[1].MaxInvocationCount);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[1].PhysicalCardinality);

        Assert.Equal(DiagnosticsTraceDetailOperationKind.BoundedOrderedQuery, constituents[2].OperationKind);
        Assert.Equal("elsa_otel_logs_trace_detail", constituents[2].IndexName);
        Assert.Equal(
            [
                new RuntimeNativeOrderTerm("timestamp", RuntimeNativeOrderDirection.Ascending),
                new RuntimeNativeOrderTerm("id", RuntimeNativeOrderDirection.Ascending),
                new RuntimeNativeOrderTerm("sequence", RuntimeNativeOrderDirection.Ascending)
            ],
            constituents[2].Ordering);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[2].PublicRowBound);
        Assert.Equal((DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream + DiagnosticsDurableHistoryWorkload.QueryLimit - 1) / DiagnosticsDurableHistoryWorkload.QueryLimit, constituents[2].MaxInvocationCount);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.RetainedRecordsPerStream, constituents[2].PhysicalCardinality);

        Assert.Equal(DiagnosticsTraceDetailOperationKind.PrimaryKeyRead, constituents[3].OperationKind);
        Assert.Empty(constituents[3].IndexName);
        Assert.Equal(1, constituents[3].FiniteLimit);
        Assert.Equal(DiagnosticsDurableHistoryWorkload.ResourceCount, constituents[3].PhysicalCardinality);
        Assert.Equal(Math.Min(5_000, DiagnosticsDurableHistoryWorkload.ResourceCount), constituents[3].MaxInvocationCount);
    }

    [Theory]
    [InlineData("resources-by-status", "status", true)]
    [InlineData("resources-by-service", "serviceNameKey", true)]
    [InlineData("metrics-by-last-seen", null, true)]
    [InlineData("logs-by-last-seen", null, true)]
    [InlineData("structured-log-recent", null, true)]
    [InlineData("structured-log-replay", null, false)]
    public void Groundwork_frozen_routes_bind_exact_order_and_predicate_shape(
        string route,
        string? predicate,
        bool descending)
    {
        var specification = DiagnosticsNativePlanContract.For(DiagnosticsNativePlanContract.GroundworkAdapter, route);

        Assert.NotEqual(string.Empty, specification.IndexName);
        Assert.Equal(predicate, specification.PredicateColumn);
        Assert.Equal(descending, specification.Descending);
        Assert.True(specification.StorageScopeRequired);
    }

    [Fact]
    public void Unfiltered_route_has_no_route_predicate_but_still_requires_scope_binding()
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "resources-by-last-seen");

        Assert.True(specification.StorageScopeRequired);
        Assert.Null(specification.PredicateColumn);
    }

    private static JsonObject MongoOrdinalKeyStage(int index, string column, string? body = null) =>
        new()
        {
            ["$set"] = new JsonObject
            {
                [$"_groundwork_ordinal_key_{index}"] = new JsonObject
                {
                    ["$function"] = new JsonObject
                    {
                        ["body"] = body ?? MongoOrdinalKeyFunctionBody,
                        ["args"] = new JsonArray(JsonValue.Create("$" + column)),
                        ["lang"] = "js"
                    }
                }
            }
        };

    private static string BoundedCatalogPlan(string provider)
    {
        if (provider == "postgresql")
        {
            var ordinalColumns = new[] { "idOrderKey", "id" };
            var scan = new Dictionary<string, object>
            {
                ["Node Type"] = "Seq Scan",
                ["Relation Name"] = "elsa_otel_resources_v2",
                ["Plans"] = Enumerable.Range(1, 2).Select(index =>
                {
                    var alias = index == 1 ? "chars" : $"chars_{index - 1}";
                    return new Dictionary<string, object>
                    {
                        ["Node Type"] = "Aggregate",
                        ["Parent Relationship"] = "SubPlan",
                        ["Subplan Name"] = $"SubPlan {index}",
                        ["Output"] = new[]
                        {
                            $"string_agg(CASE WHEN (ascii({alias}.ch) <= 65535) THEN lpad(to_hex(ascii({alias}.ch)), 4, '0'::text) ELSE " +
                            $"(lpad(to_hex((55296 + ((ascii({alias}.ch) - 65536) >> 10))), 4, '0'::text) || " +
                            $"lpad(to_hex((56320 + ((ascii({alias}.ch) - 65536) & 1023))), 4, '0'::text)) END, ''::text ORDER BY {alias}.ord)"
                        },
                        ["Plans"] = new[]
                        {
                            new Dictionary<string, object>
                            {
                                ["Node Type"] = "Function Scan",
                                ["Function Name"] = "unnest",
                                ["Alias"] = alias,
                                ["Output"] = new[] { $"{alias}.ch", $"{alias}.ord" },
                                ["Function Call"] =
                                    $"unnest(string_to_array((elsa_otel_resources_v2.{ordinalColumns[index - 1]})::text, NULL::text))"
                            }
                        }
                    };
                }).ToArray()
            };
            var sort = new Dictionary<string, object>
            {
                ["Node Type"] = "Sort",
                ["Sort Key"] = PostgreSqlExplainSortKeys(),
                ["Plans"] = new[] { scan }
            };
            var limit = new Dictionary<string, object>
            {
                ["Node Type"] = "Limit",
                ["Plans"] = new[] { sort }
            };
            return JsonSerializer.Serialize(new[]
            {
                new Dictionary<string, object> { ["Plan"] = limit }
            });
        }

        return provider switch
        {
            "sqlite" =>
                "2 0 SCAN elsa_otel_resources_v2\n3 0 USE TEMP B-TREE FOR ORDER BY",
            "sqlserver" =>
                """
                <ShowPlanXML>
                  <RelOp PhysicalOp="Top"><Top><RelOp PhysicalOp="Filter"><Filter>
                    <RelOp PhysicalOp="Sort"><Sort><OrderBy>
                      <OrderByColumn Ascending="0"><ColumnReference Column="[lastSeen]" /></OrderByColumn>
                      <OrderByColumn Ascending="1"><ColumnReference Column="[Expr1005]" /></OrderByColumn>
                      <OrderByColumn Ascending="1"><ColumnReference Column="[Expr1006]" /></OrderByColumn>
                      <OrderByColumn Ascending="1"><ColumnReference Column="[Expr1008]" /></OrderByColumn>
                      <OrderByColumn Ascending="1"><ColumnReference Column="[Expr1009]" /></OrderByColumn>
                    </OrderBy><RelOp PhysicalOp="Compute Scalar"><ComputeScalar><DefinedValues>
                      <DefinedValue><ColumnReference Column="[Expr1005]" /><ScalarOperator ScalarString="[idOrderKey] COLLATE Latin1_General_100_BIN2" /></DefinedValue>
                      <DefinedValue><ColumnReference Column="[Expr1006]" /><ScalarOperator ScalarString="DATALENGTH([idOrderKey] COLLATE Latin1_General_100_BIN2)" /></DefinedValue>
                      <DefinedValue><ColumnReference Column="[Expr1008]" /><ScalarOperator ScalarString="[id] COLLATE Latin1_General_100_BIN2" /></DefinedValue>
                      <DefinedValue><ColumnReference Column="[Expr1009]" /><ScalarOperator ScalarString="DATALENGTH([id] COLLATE Latin1_General_100_BIN2)" /></DefinedValue>
                    </DefinedValues>
                      <RelOp PhysicalOp="Table Scan"><TableScan><Object Table="[elsa_otel_resources_v2]" /></TableScan></RelOp>
                    </ComputeScalar></RelOp></Sort></RelOp>
                  </Filter></RelOp></Top></RelOp>
                </ShowPlanXML>
                """,
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
    }

    private static string[] PostgreSqlRenderedOrderTerms(DiagnosticsNativeRouteSpec specification) =>
        specification.EffectiveOrdering.SelectMany(term =>
        {
            var ordinal = term.Column is "id" or "idOrderKey" or "traceKey" or "spanId";
            var persistedOrdinal = term.Column is
                "__groundwork_ordinal_id" or "__groundwork_ordinal_spanId" or "__groundwork_ordinal_traceKey";
            var expression = ordinal || persistedOrdinal
                ? $"(\"{term.Column}\" COLLATE \"C\")"
                : $"\"{term.Column}\"";
            var direction = term.Direction == RuntimeNativeOrderDirection.Descending ? "DESC" : "ASC";
            var nullPlacement = term.Direction == RuntimeNativeOrderDirection.Descending
                ? "NULLS LAST"
                : "NULLS FIRST";
            return new[]
            {
                (ordinal ? PostgreSqlOrdinalKey(expression) : expression) + " " + direction + " " + nullPlacement
            };
        }).ToArray();

    private static string[] PostgreSqlExplainSortKeys() =>
    [
        "elsa_otel_resources_v2.\"lastSeen\" DESC",
        "(COALESCE((SubPlan 1), ''::text))",
        "(COALESCE((SubPlan 2), ''::text))"
    ];

    private static string PostgreSqlOrdinalKey(string expression) =>
        "COALESCE((SELECT string_agg(CASE WHEN ascii(chars.ch) <= 65535 THEN lpad(to_hex(ascii(chars.ch)), 4, '0') ELSE " +
        "lpad(to_hex(55296 + ((ascii(chars.ch) - 65536) >> 10)), 4, '0') || " +
        "lpad(to_hex(56320 + ((ascii(chars.ch) - 65536) & 1023)), 4, '0') END, '' ORDER BY chars.ord) " +
        "FROM unnest(string_to_array(" + expression + ", NULL)) WITH ORDINALITY AS chars(ch, ord)), '')";

    private static string ProviderRenderedEquality(
        string provider,
        DiagnosticsNativeRouteSpec specification,
        string column,
        string parameter)
    {
        var isString = string.Equals(column, "__groundwork_scope", StringComparison.Ordinal) ||
                       string.Equals(column, specification.PredicateColumn, StringComparison.Ordinal) &&
                       string.Equals(specification.RouteIdentity, "resources-by-service", StringComparison.Ordinal);
        var expression = provider switch
        {
            "postgresql" when isString => $"(\"{column}\" COLLATE \"C\")",
            "postgresql" => $"\"{column}\"",
            "sqlserver" when isString => $"[{column}] COLLATE Latin1_General_100_BIN2",
            "sqlserver" => $"[{column}]",
            _ => throw new ArgumentOutOfRangeException(nameof(provider))
        };
        return provider == "sqlserver" && isString
            ? $"({expression} IS NOT NULL AND DATALENGTH({expression}) = DATALENGTH({parameter}) AND {expression} = {parameter})"
            : $"({expression} IS NOT NULL AND {expression} = {parameter})";
    }

    private static string SqlServerPlanWithoutTop()
    {
        var document = System.Xml.Linq.XDocument.Parse(BoundedCatalogPlan("sqlserver"));
        var top = document.Descendants().Single(element =>
            element.Name.LocalName == "RelOp" &&
            element.Attribute("PhysicalOp")?.Value == "Top");
        var child = top.Descendants().First(element => element.Name.LocalName == "RelOp");
        top.ReplaceWith(new System.Xml.Linq.XElement(child));
        return document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static string PostgreSqlPlanWithSwappedLimitAndSort()
    {
        var document = JsonNode.Parse(BoundedCatalogPlan("postgresql"))!.AsArray();
        var limit = document[0]!["Plan"]!.AsObject();
        var sort = limit["Plans"]![0]!.AsObject();
        limit["Node Type"] = "Sort";
        sort["Node Type"] = "Limit";
        return document.ToJsonString();
    }

    private static string SqlServerPlanWithDetachedSort()
    {
        var document = System.Xml.Linq.XDocument.Parse(BoundedCatalogPlan("sqlserver"));
        var top = document.Descendants().Single(element =>
            element.Name.LocalName == "RelOp" &&
            element.Attribute("PhysicalOp")?.Value == "Top");
        var sort = document.Descendants().Single(element =>
            element.Name.LocalName == "RelOp" &&
            element.Attribute("PhysicalOp")?.Value == "Sort");
        var detachedSort = new System.Xml.Linq.XElement(sort);
        top.RemoveNodes();
        document.Root!.Add(detachedSort);
        return document.ToString(System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static string RelationalCommand(string provider, DiagnosticsNativeRouteSpec specification)
    {
        var predicate = specification.PredicateColumn is null
            ? "__groundwork_scope = @scope"
            : $"__groundwork_scope = @scope AND {specification.PredicateColumn} = @value";
        var ordering = string.Join(
            ", ",
            specification.EffectiveOrdering.Select(term =>
                $"{term.Column} {(term.Direction == RuntimeNativeOrderDirection.Descending ? "DESC" : "ASC")}"));
        return provider == "sqlserver"
            ? $"SELECT TOP ({specification.FiniteLimit}) * FROM {specification.TableName} WHERE {predicate} ORDER BY {ordering}"
            : $"SELECT * FROM {specification.TableName} WHERE {predicate} ORDER BY {ordering} LIMIT {specification.FiniteLimit}";
    }

    private static string SqlServerNullableOrderingCommand(
        DiagnosticsNativeRouteSpec specification,
        bool includeNullRanks)
    {
        var ordering = specification.EffectiveOrdering.SelectMany(term =>
        {
            var value = $"[{term.Column}] {(term.Direction == RuntimeNativeOrderDirection.Descending ? "DESC" : "ASC")}";
            return includeNullRanks
                ? new[] { $"CASE WHEN [{term.Column}] IS NULL THEN 1 ELSE 0 END ASC", value }
                : new[] { value };
        });
        return $"SELECT TOP ({specification.FiniteLimit}) * FROM {specification.TableName} ORDER BY {string.Join(", ", ordering)}";
    }

    private static string SqlServerStructuredLogRecentCommand() =>
        "SELECT * FROM [elsa_structured_logs] WHERE " +
        "([__groundwork_scope] COLLATE Latin1_General_100_BIN2 IS NOT NULL AND " +
        "DATALENGTH([__groundwork_scope] COLLATE Latin1_General_100_BIN2) = DATALENGTH(@p0) AND " +
        "[__groundwork_scope] COLLATE Latin1_General_100_BIN2 = @p0) " +
        "ORDER BY [sequence] DESC OFFSET 0 ROWS FETCH NEXT @p1 ROWS ONLY";

    private static string SqlServerStructuredLogReplayCommandWithoutScopeLengthGuard() =>
        "SELECT * FROM [elsa_structured_logs] WHERE " +
        "([__groundwork_scope] IS NOT NULL AND [__groundwork_scope] = @p0) AND " +
        "([sequence] IS NOT NULL AND [sequence] > @p1 AND [sequence] <= @p2) " +
        "ORDER BY [sequence] ASC OFFSET 0 ROWS FETCH NEXT @p3 ROWS ONLY";

    private static string SqlServerCapturedStructuredLogReplayCommand() =>
        "SELECT * FROM [elsa_structured_logs] WHERE " +
        "(([__groundwork_scope] COLLATE Latin1_General_100_BIN2 IS NOT NULL AND " +
        "DATALENGTH([__groundwork_scope] COLLATE Latin1_General_100_BIN2) = DATALENGTH(@p0) AND " +
        "[__groundwork_scope] COLLATE Latin1_General_100_BIN2 = @p0) AND " +
        "([sequence] IS NOT NULL AND [sequence] > @p1 AND [sequence] <= @p2)) " +
        "ORDER BY [sequence] ASC OFFSET 0 ROWS FETCH NEXT @p3 ROWS ONLY";

    private static string SqlServerStructuredLogPrimaryKeyPlan() =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "sqlserver-structured-log-primary-key.xml"));

    private static string SqlServerStructuredLogReplayPlan()
    {
        var plan = SqlServerStructuredLogPrimaryKeyPlan()
            .Replace("ScanDirection=\"BACKWARD\"", "ScanDirection=\"FORWARD\"", StringComparison.Ordinal)
            .Replace("ScalarString=\"CONVERT_IMPLICIT(bigint,[@p1],0)\"", "ScalarString=\"CONVERT_IMPLICIT(bigint,[@p3],0)\"", StringComparison.Ordinal)
            .Replace("FETCH NEXT @p1", "FETCH NEXT @p3", StringComparison.Ordinal)
            .Replace("<ColumnReference Column=\"@p1\"/>", "<ColumnReference Column=\"@p3\"/>", StringComparison.Ordinal)
            .Replace("<ColumnReference Column=\"@p1\" />", "<ColumnReference Column=\"@p3\" />", StringComparison.Ordinal)
            .Replace("<ColumnReference Column=\"@p1\" ParameterDataType=", "<ColumnReference Column=\"@p3\" ParameterDataType=", StringComparison.Ordinal);
        const string prefixEnd = "                                  </Prefix>\n                                </SeekKeys>";
        const string replayRanges = """
                                  </Prefix>
                                  <StartRange ScanType="GT">
                                    <RangeColumns>
                                      <ColumnReference Database="[groundwork_diagnostics_fixture]" Schema="[dbo]" Table="[elsa_structured_logs]" Column="sequence" />
                                    </RangeColumns>
                                    <RangeExpressions>
                                      <ScalarOperator>
                                        <Identifier><ColumnReference Column="@p1" /></Identifier>
                                      </ScalarOperator>
                                    </RangeExpressions>
                                  </StartRange>
                                  <EndRange ScanType="LE">
                                    <RangeColumns>
                                      <ColumnReference Database="[groundwork_diagnostics_fixture]" Schema="[dbo]" Table="[elsa_structured_logs]" Column="sequence" />
                                    </RangeColumns>
                                    <RangeExpressions>
                                      <ScalarOperator>
                                        <Identifier><ColumnReference Column="@p2" /></Identifier>
                                      </ScalarOperator>
                                    </RangeExpressions>
                                  </EndRange>
                                </SeekKeys>
        """;
        return plan.Replace(prefixEnd, replayRanges, StringComparison.Ordinal);
    }

    private static string SqlServerStructuredLogReplayDuplicateStartRange() =>
        """
                              <StartRange ScanType="GT">
                                <RangeColumns>
                                  <ColumnReference Database="[groundwork_diagnostics_fixture]" Schema="[dbo]" Table="[elsa_structured_logs]" Column="sequence" />
                                </RangeColumns>
                                <RangeExpressions>
                                  <ScalarOperator>
                                    <Identifier><ColumnReference Column="@p1" /></Identifier>
                                  </ScalarOperator>
                                </RangeExpressions>
                              </StartRange>
                              <EndRange ScanType="LE">
        """;

    private static string SqlServerIndexSeekPlan(DiagnosticsNativeRouteSpec specification)
    {
        var physicalIndex = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName("sqlserver", specification);
        return $"<ShowPlanXML><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Table=\"[{specification.TableName}]\" Index=\"[{physicalIndex}]\" /></IndexScan></RelOp></ShowPlanXML>";
    }

    private static string ReadRealPostgreSqlBoundedCatalogPlan(string fixtureName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName));

    private static string ReadMongoExplainFixture(string fixtureName) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixtureName));

    private static string MongoCapturedResourceIndexPlan(DiagnosticsNativeRouteSpec specification)
    {
        var nativePlan = JsonNode.Parse(ReadMongoExplainFixture(
            "mongodb-resources-by-service-explain.json"))!.AsObject();
        var cursor = nativePlan["stages"]!.AsArray()
            .Single(stage => stage!["$cursor"] is not null)!["$cursor"]!.AsObject();
        var queryPlanner = cursor["queryPlanner"]!.AsObject();
        var winningPlan = queryPlanner["winningPlan"]!.AsObject();
        var inputStage = winningPlan["inputStage"]!.AsObject();
        var keyPattern = new JsonObject();
        if (specification.PredicateColumn is not null)
            keyPattern[specification.PredicateColumn] = 1;
        keyPattern["lastSeen"] = -1;
        keyPattern["idOrderKey"] = 1;
        keyPattern["id"] = 1;
        inputStage["keyPattern"] = keyPattern;
        var indexBounds = new JsonObject();
        if (specification.PredicateColumn is not null)
            indexBounds[specification.PredicateColumn] = new JsonArray(JsonValue.Create("[\"service-hash\", \"service-hash\"]"));
        indexBounds["lastSeen"] = new JsonArray(JsonValue.Create("[MaxKey, MinKey]"));
        indexBounds["idOrderKey"] = new JsonArray(JsonValue.Create("[MinKey, MaxKey]"));
        indexBounds["id"] = new JsonArray(JsonValue.Create("[MinKey, MaxKey]"));
        inputStage["indexBounds"] = indexBounds;
        inputStage["indexName"] = DiagnosticsNativePlanContract.ExpectedPhysicalIndexName(
            "mongodb",
            specification);

        // Only the service route is captured. Keep both native trees consistent when deriving
        // status and last-seen variants; these variants are contract tests, not live-provider evidence.
        inputStage["multiKeyPaths"] = new JsonObject(keyPattern.Select(property =>
            new KeyValuePair<string, JsonNode?>(property.Key, new JsonArray())));
        var executionIndex = cursor["executionStats"]!["executionStages"]!["inputStage"]!.AsObject();
        foreach (var property in inputStage)
            executionIndex[property.Key] = property.Value?.DeepClone();

        var match = nativePlan["command"]!["pipeline"]!.AsArray()[0]!["$match"]!.AsObject();
        if (specification.PredicateColumn is null)
            match.Clear();
        else if (specification.PredicateColumn != "serviceNameKey")
        {
            var value = match["serviceNameKey"]!.DeepClone();
            match.Remove("serviceNameKey");
            match[specification.PredicateColumn] = value;
        }

        queryPlanner["parsedQuery"] = specification.PredicateColumn is null
            ? new JsonObject()
            : new JsonObject
            {
                [specification.PredicateColumn] = new JsonObject
                {
                    ["$eq"] = match[specification.PredicateColumn]!.DeepClone()
                }
            };
        return nativePlan.ToJsonString();
    }

    private static string RealPostgreSqlBoundedCatalogPlanForRoute(
        DiagnosticsNativeRouteSpec specification)
    {
        var plan = JsonNode.Parse(ReadRealPostgreSqlBoundedCatalogPlan(
            "postgresql-bounded-resources-by-last-seen.json"))!.AsArray();
        var scan = plan[0]!["Plan"]!["Plans"]![0]!["Plans"]![0]!;
        var originalTable = scan["Relation Name"]!.GetValue<string>();
        Assert.Equal("elsa_otel_resources_v2", originalTable);
        scan["Relation Name"] = specification.TableName;
        return plan.ToJsonString();
    }

    private static string MutateRealPostgreSqlBoundedCatalogPlan(string mutation)
    {
        var plan = JsonNode.Parse(ReadRealPostgreSqlBoundedCatalogPlan(
                "postgresql-bounded-resources-by-last-seen.json"))!.AsArray();
        var sortKeys = plan[0]!["Plan"]!["Plans"]![0]!["Sort Key"]!.AsArray();
        var index = mutation.StartsWith("subplan-", StringComparison.Ordinal) ? 1 : 0;
        var original = sortKeys[index]!.GetValue<string>();
        var replacement = mutation switch
        {
            "simple-null-placement" => original.Replace("DESC NULLS LAST", "DESC NULLS FIRST", StringComparison.Ordinal),
            "simple-direction" => original.Replace("DESC NULLS LAST", "ASC NULLS FIRST", StringComparison.Ordinal),
            "subplan-null-placement" => original.Replace("NULLS FIRST", "NULLS LAST", StringComparison.Ordinal),
            "subplan-direction" => original.Replace("NULLS FIRST", "DESC NULLS LAST", StringComparison.Ordinal),
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };
        Assert.NotEqual(original, replacement);
        sortKeys[index] = replacement;
        return plan.ToJsonString();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory("diagnostics-trace-detail-");

        public string FullName => directory.FullName;

        public void Dispose() => directory.Delete(true);
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
