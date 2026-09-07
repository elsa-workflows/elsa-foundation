using System.Text.Json;
using System.Xml.Linq;
using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.AdapterHost.Tests;

public sealed class DiagnosticsSqlServerPrimaryKeyContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SqlServer_structured_log_primary_key_plan_survives_raw_and_artifact_normalized_admission(bool normalize)
    {
        var rawPlan = ReadStructuredLogPlan();
        var plan = normalize ? IamNativePlanParser.NormalizeForArtifact("sqlserver", rawPlan) : rawPlan;

        Validate(plan, CommandFromPlan(rawPlan));
    }

    [Theory]
    [InlineData("filter-scope")]
    [InlineData("datalength-function")]
    [InlineData("datalength-direct-parameter")]
    [InlineData("rid-bookmark")]
    public void SqlServer_normalized_primary_key_plan_rejects_targeted_filter_and_rid_mutations(string mutation)
    {
        var rawPlan = ReadStructuredLogPlan();
        var normalized = IamNativePlanParser.NormalizeForArtifact("sqlserver", rawPlan);
        var invalidPlan = MutateNormalizedPlan(normalized, mutation);

        Assert.Throws<PerformanceContractException>(() => Validate(invalidPlan, CommandFromPlan(rawPlan)));
    }

    private static void Validate(string nativePlan, string command)
    {
        var specification = DiagnosticsNativePlanContract.For(
            DiagnosticsNativePlanContract.GroundworkAdapter,
            "structured-log-recent");
        var artifact = new DiagnosticsNativePlanArtifact(
            1,
            "sqlserver",
            DiagnosticsNativePlanContract.GroundworkAdapter,
            specification.RouteIdentity,
            specification.TableName,
            specification.IndexName,
            "__groundwork_pk_elsa_structured_logs",
            command,
            nativePlan);
        var directory = Directory.CreateTempSubdirectory("diagnostics-sqlserver-primary-key-");
        try
        {
            var path = Path.Combine(directory.FullName, "route.json");
            File.WriteAllText(path, JsonSerializer.Serialize(artifact));
            var route = new NativeRouteEvidence(
                specification.RouteIdentity,
                "route.json",
                ArtifactStore.HashFile(path),
                DiagnosticsNativePlanContract.IndexSearchPlanClassification,
                "__groundwork_pk_elsa_structured_logs",
                specification.PhysicalCardinality,
                true,
                false,
                specification.FiniteLimit,
                specification.FiniteLimit);
            DiagnosticsNativePlanContract.ValidateEnvelope("sqlserver", artifact.Adapter, route, path);
        }
        finally
        {
            directory.Delete(true);
        }
    }

    private static string ReadStructuredLogPlan() =>
        File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "sqlserver-structured-log-primary-key.xml"));

    private static string CommandFromPlan(string rawPlan) =>
        XDocument.Parse(rawPlan)
            .Descendants()
            .Single(element => element.Name.LocalName == "StmtSimple")
            .Attribute("StatementText")?.Value
        ?? throw new InvalidOperationException("The SQL Server fixture has no retained StatementText command.");

    private static string MutateNormalizedPlan(string normalized, string mutation)
    {
        var document = XDocument.Parse(normalized);
        if (mutation == "rid-bookmark")
        {
            var rangeExpression = document.Descendants()
                .Single(element => element.Name.LocalName == "RelOp" &&
                                   element.Attribute("PhysicalOp")?.Value == "RID Lookup")
                .Descendants()
                .Single(element => element.Name.LocalName == "RangeExpressions")
                .Descendants()
                .Single(element => element.Name.LocalName == "ColumnReference");
            rangeExpression.SetAttributeValue("Column", "Bmk9999");
        }
        else
        {
            var predicate = document.Descendants()
                .Single(element => element.Name.LocalName == "RelOp" &&
                                   element.Attribute("PhysicalOp")?.Value == "Filter")
                .Elements()
                .Single(element => element.Name.LocalName == "Filter")
                .Elements()
                .Single(element => element.Name.LocalName == "Predicate");
            var compare = predicate.Descendants().Single(element => element.Name.LocalName == "Compare");
            switch (mutation)
            {
                case "filter-scope":
                    predicate.Descendants()
                        .Single(element => element.Name.LocalName == "ColumnReference" &&
                                           element.Attribute("Column")?.Value == "__groundwork_scope")
                        .SetAttributeValue("Column", "other_scope");
                    break;
                case "datalength-function":
                    predicate.Descendants()
                        .First(element => element.Name.LocalName == "Intrinsic")
                        .SetAttributeValue("FunctionName", "len");
                    break;
                case "datalength-direct-parameter":
                    var operands = compare.Elements()
                        .Where(element => element.Name.LocalName == "ScalarOperator")
                        .ToArray();
                    operands[1].ReplaceNodes(new XElement(
                        "Identifier",
                        new XElement("ColumnReference", new XAttribute("Column", "@p0"))));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null);
            }
        }

        return document.ToString(SaveOptions.DisableFormatting);
    }
}
