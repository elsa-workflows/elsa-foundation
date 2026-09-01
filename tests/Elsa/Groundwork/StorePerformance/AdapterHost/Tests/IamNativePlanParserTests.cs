using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class IamNativePlanParserTests
{
    [Fact]
    public void Sqlite_plan_derives_the_physical_index_and_rejects_a_scan()
    {
        var parsed = IamNativePlanParser.Parse(
            "sqlite",
            "2\t0\tSEARCH identity_users USING COVERING INDEX __groundwork_ix_14_identity_users_29_identity_user_by_normalized_name (x=?)");

        Assert.Equal("sqlite-explain-query-plan", parsed.Format);
        Assert.Equal("__groundwork_ix_14_identity_users_29_identity_user_by_normalized_name", parsed.PhysicalIndexName);
        Assert.Equal("index-search", parsed.PlanClassification);

        var error = Assert.Throws<PerformanceContractException>(() =>
            IamNativePlanParser.Parse("sqlite", "2\t0\tSCAN identity_users"));
        Assert.Contains("SEARCH", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Secret_plan_derives_the_route_index_while_ignoring_the_version_include_lookup()
    {
        var parsed = IamNativePlanParser.ParseSecret(
            "sqlite",
            "2\t0\tSEARCH s USING INDEX IX_Secrets_TenantId_Name (TenantId=?)\n" +
            "42\t0\tSEARCH s0 USING INDEX sqlite_autoindex_SecretVersionEntity_1 (SecretId=?) LEFT-JOIN");

        Assert.Equal("IX_Secrets_TenantId_Name", parsed.PhysicalIndexName);
        Assert.Equal("index-search", parsed.PlanClassification);
    }

    [Fact]
    public void Secret_plan_rejects_every_physical_scan_including_an_alias()
    {
        Assert.Throws<PerformanceContractException>(() =>
            IamNativePlanParser.ParseSecret(
                "sqlite",
                "2\t0\tSEARCH s USING INDEX IX_Secrets_TenantId_Name (TenantId=?)\n" +
                "39\t0\tSCAN s1"));
    }

    [Fact]
    public void Secret_plan_does_not_treat_an_indexed_search_through_an_alias_as_a_scan()
    {
        var parsed = IamNativePlanParser.ParseSecret(
            "sqlite",
            "2\t0\tSEARCH s USING COVERING INDEX IX_Secrets_TenantId_Status_Name (TenantId=? AND Status=?)");

        Assert.Equal("IX_Secrets_TenantId_Status_Name", parsed.PhysicalIndexName);
    }

    [Fact]
    public void Secret_plan_distinguishes_groundwork_result_cte_iteration_from_a_physical_alias_scan()
    {
        var parsed = IamNativePlanParser.ParseSecret(
            "sqlite",
            "2\t0\tSEARCH s USING INDEX IX_Secrets_TenantId_Status_Name (TenantId=? AND Status=?)\n" +
            "102\t0\tSCAN __groundwork_total\n" +
            "107\t0\tSCAN __groundwork_page LEFT-JOIN");

        Assert.Equal("IX_Secrets_TenantId_Status_Name", parsed.PhysicalIndexName);
    }

    [Fact]
    public void Secret_sql_predicates_are_proved_from_the_where_clause_not_field_name_substrings()
    {
        var proof = SecretRoutePredicateInspector.InspectSql(
            "SELECT s.tenantId, s.status FROM physical_table AS s WHERE s.tenantId = @tenant AND s.status = @status ORDER BY s.normalizedName LIMIT @take",
            "tenantId",
            "status");

        Assert.True(proof.HasStorageScopePredicate);
        Assert.True(proof.HasRoutePredicate);
        Assert.True(SecretRoutePredicateInspector.HasSqlParameterizedEquality(
            "SELECT * FROM physical_table WHERE (\"tenantId\" COLLATE \"C\" = @p0)",
            "tenantId"));
        Assert.Throws<PerformanceContractException>(() =>
            SecretRoutePredicateInspector.InspectSql(
                "SELECT s.tenantId, s.status FROM physical_table AS s WHERE s.normalizedName = @name ORDER BY s.tenantId, s.status LIMIT @take",
                "tenantId",
                "status"));
    }

    [Theory]
    [InlineData("SELECT * FROM t WHERE notTenantId = @tenant AND status = @status")]
    [InlineData("SELECT * FROM t WHERE tenantIdSuffix = @tenant AND status = @status")]
    [InlineData("SELECT * FROM t WHERE prefix_tenantId = @tenant AND status = @status")]
    [InlineData("SELECT * FROM t WHERE other = @value /* tenantId = @tenant */ AND status = @status")]
    [InlineData("SELECT * FROM t WHERE note = 'tenantId = @tenant' AND status = @status")]
    [InlineData("SELECT * FROM t WHERE NOT tenantId = @tenant AND status = @status")]
    [InlineData("SELECT * FROM t WHERE tenantId = @tenant + 1 AND status = @status")]
    [InlineData("SELECT * FROM t WHERE (tenantId = @tenant OR 1 = 1) AND status = @status")]
    [InlineData("SELECT * FROM t WHERE tenantId = @tenant AND (status = @status OR 1 = 1)")]
    [InlineData("SELECT * FROM t WHERE CASE WHEN (tenantId = @tenant) THEN 1 ELSE 1 END = 1 AND status = @status")]
    public void Secret_sql_predicate_parser_rejects_identifier_collisions_comments_literals_and_non_equality_operands(
        string sql)
    {
        Assert.Throws<PerformanceContractException>(() =>
            SecretRoutePredicateInspector.InspectSql(sql, "tenantId", "status"));
    }

    [Fact]
    public void Secret_sql_predicate_parser_accepts_exact_qualified_and_quoted_identifiers_only()
    {
        var proof = SecretRoutePredicateInspector.InspectSql(
            "SELECT * FROM t WHERE [s].[tenantId] = @tenant AND \"s\".\"status\" COLLATE \"C\" = $2 ORDER BY [s].[tenantId] LIMIT @take",
            "tenantId",
            "status");

        Assert.True(proof.HasStorageScopePredicate);
        Assert.True(proof.HasRoutePredicate);
    }

    [Fact]
    public void Secret_sql_predicate_parser_accepts_a_required_equality_present_in_every_or_branch()
    {
        var proof = SecretRoutePredicateInspector.InspectSql(
            "SELECT * FROM t WHERE (tenantId = @tenantA OR tenantId = @tenantB) AND status = @status",
            "tenantId",
            "status");

        Assert.True(proof.HasStorageScopePredicate);
        Assert.True(proof.HasRoutePredicate);
    }

    [Fact]
    public void Secret_mongo_predicates_are_proved_from_the_actual_aggregate_pipeline_without_collection_name_assumptions()
    {
        const string rawPlan = """
            {
              "queryPlanner": {
                "winningPlan": {
                  "stage": "IXSCAN",
                  "indexName": "elsa_secrets_filtered_list"
                }
              },
              "command": {
                "aggregate": "opaque_scope_7f85d8",
                "pipeline": [
                  { "$match": { "$and": [ { "tenantId": "tenant-alpha" }, { "status": { "$eq": "active" } } ] } },
                  { "$sort": { "normalizedName": 1 } },
                  { "$limit": 16 }
                ],
                "cursor": {}
              }
            }
            """;

        var proof = SecretRoutePredicateInspector.InspectMongoExplain(rawPlan);

        Assert.Equal("opaque_scope_7f85d8", proof.MongoAggregateCollection);
        Assert.True(proof.HasStorageScopePredicate);
        Assert.True(proof.HasRoutePredicate);
    }

    [Fact]
    public void Secret_mongo_capture_fails_closed_without_an_actual_aggregate_command_or_match()
    {
        var missingCommand = Assert.Throws<PerformanceContractException>(() =>
            SecretRoutePredicateInspector.InspectMongoExplain(
                "{\"queryPlanner\":{\"winningPlan\":{\"stage\":\"IXSCAN\",\"indexName\":\"elsa_secrets_filtered_list\"}}}"));
        Assert.Contains("actual aggregate command", missingCommand.Message, StringComparison.Ordinal);

        var substringOnly = Assert.Throws<PerformanceContractException>(() =>
            SecretRoutePredicateInspector.InspectMongoExplain(
                "{\"command\":{\"aggregate\":\"tenantId-status\",\"pipeline\":[{\"$project\":{\"tenantId\":1,\"status\":1}},{\"$limit\":16}]}}"));
        Assert.Contains("$match equality predicates", substringOnly.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("{\"$and\":[{\"$or\":[{\"tenantId\":\"tenant-alpha\"},{\"noise\":true}]},{\"status\":\"active\"}]}")]
    [InlineData("{\"other\":{\"tenantId\":\"tenant-alpha\"},\"status\":\"active\"}")]
    [InlineData("{\"tenantId\":{\"$not\":{\"$eq\":\"tenant-alpha\"}},\"status\":\"active\"}")]
    public void Secret_mongo_predicate_parser_rejects_optional_nested_and_negated_equalities(string match)
    {
        var rawPlan = $$"""
            {
              "command": {
                "aggregate": "opaque_scope_7f85d8",
                "pipeline": [
                  { "$match": {{match}} },
                  { "$limit": 16 }
                ]
              }
            }
            """;

        Assert.Throws<PerformanceContractException>(() =>
            SecretRoutePredicateInspector.InspectMongoExplain(rawPlan));
    }

    [Fact]
    public void PostgreSql_plan_derives_index_only_scan_and_rejects_sequential_scan()
    {
        var parsed = IamNativePlanParser.Parse(
            "postgresql",
            "[{\"Plan\":{\"Node Type\":\"Limit\",\"Plans\":[{\"Node Type\":\"Index Only Scan\",\"Index Name\":\"__groundwork_ix_14_identity_users_29_identity_user_by_normalized_name\"}]}}]");

        Assert.Equal("__groundwork_ix_14_identity_users_29_identity_user_by_normalized_name", parsed.PhysicalIndexName);

        var error = Assert.Throws<PerformanceContractException>(() =>
            IamNativePlanParser.Parse("postgresql", "[{\"Plan\":{\"Node Type\":\"Seq Scan\"}}]"));
        Assert.Contains("sequential", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SqlServer_plan_derives_index_seek_and_normalizes_connection_metadata()
    {
        const string raw = "<ShowPlanXML xmlns=\"http://schemas.microsoft.com/sqlserver/2004/07/showplan\"><BatchSequence><RelOp PhysicalOp=\"Index Seek\"><IndexScan><Object Database=\"[identity]\" Index=\"[__groundwork_ix_14_identity_users_29_identity_user_by_normalized_name]\" /></IndexScan></RelOp></BatchSequence></ShowPlanXML>";
        var normalized = IamNativePlanParser.NormalizeForArtifact("sqlserver", raw);
        var parsed = IamNativePlanParser.Parse("sqlserver", normalized);

        Assert.DoesNotContain("http://", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Database=", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("__groundwork_ix_14_identity_users_29_identity_user_by_normalized_name", parsed.PhysicalIndexName);

        var error = Assert.Throws<PerformanceContractException>(() =>
            IamNativePlanParser.Parse("sqlserver", "<ShowPlanXML><RelOp PhysicalOp=\"Table Scan\" /></ShowPlanXML>"));
        Assert.Contains("scan", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Mongo_plan_uses_only_the_winning_ixscan_and_rejects_collscan()
    {
        var parsed = IamNativePlanParser.Parse(
            "mongodb",
            "{\"queryPlanner\":{\"winningPlan\":{\"stage\":\"FETCH\",\"inputStage\":{\"stage\":\"IXSCAN\",\"indexName\":\"__groundwork_ix_14_identity_users_29_identity_user_by_normalized_name\"}},\"rejectedPlans\":[{\"stage\":\"COLLSCAN\"}]}}");

        Assert.Equal("__groundwork_ix_14_identity_users_29_identity_user_by_normalized_name", parsed.PhysicalIndexName);

        var error = Assert.Throws<PerformanceContractException>(() =>
            IamNativePlanParser.Parse("mongodb", "{\"queryPlanner\":{\"winningPlan\":{\"stage\":\"COLLSCAN\"}}}"));
        Assert.Contains("COLLSCAN", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
