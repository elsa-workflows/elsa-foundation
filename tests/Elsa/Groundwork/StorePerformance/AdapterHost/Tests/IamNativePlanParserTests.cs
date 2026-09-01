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
