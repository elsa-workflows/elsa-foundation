using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Elsa3.Migration.Tooling.Report;
using Elsa3.Models;
using Json.Schema;
using static Elsa3.Migration.Tests.ReportBuilderTests;

namespace Elsa3.Migration.Tests;

public sealed class ReportRenderingTests
{
    [Fact]
    public void Embedded_schema_is_identical_to_the_contract()
    {
        var contract = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Contracts", "report.schema.json"));

        Assert.Equal(Normalize(contract), Normalize(ReportJson.ReadSchema()));
    }

    [Fact]
    public void Built_reports_validate_against_the_schema()
    {
        foreach (var report in SampleReports())
            AssertValid(ReportJson.Serialize(report));
    }

    [Fact]
    public void Optional_members_are_left_out_not_written_as_null()
    {
        var json = JsonNode.Parse(ReportJson.Serialize(SampleReports().First()))!;
        var finding = json["definitions"]![0]!["findings"]![0]!.AsObject();

        Assert.False(finding.ContainsKey("remedy"));
        Assert.False(finding.ContainsKey("componentKind"));
        Assert.False(finding.ContainsKey("nodeId"));
        Assert.False(finding.ContainsKey("publishStoppedHere"));
        Assert.True(finding.ContainsKey("path"));
        Assert.True(json["definitions"]![0]!.AsObject().ContainsKey("wrapperExportedTo"));
    }

    [Fact]
    public void Serialization_is_deterministic()
    {
        var first = ReportJson.Serialize(SampleReports().Last());
        var second = ReportJson.Serialize(SampleReports().Last());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Html_inlines_exactly_the_report_json()
    {
        var json = ReportJson.Serialize(SampleReports().Last());
        var html = Encoding.UTF8.GetString(ReportHtml.Render(json));

        var inlined = ExtractData(html);

        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(inlined)));
    }

    [Fact]
    public void Html_contains_no_external_url()
    {
        var html = Encoding.UTF8.GetString(ReportHtml.Render(ReportJson.Serialize(SampleReports().Last())));

        Assert.DoesNotMatch(new Regex("(https?:|//[a-z0-9.-]+\\.[a-z]{2,}|src\\s*=|href\\s*=|@import|url\\()", RegexOptions.IgnoreCase), html);
    }

    [Fact]
    public void A_script_closing_tag_in_a_name_cannot_break_the_page()
    {
        const string hostile = "</script><script>alert(1)</script>&amp;";
        var report = ReportBuilder.Build(Input([Definition("x") with { Name = hostile }]));
        var json = ReportJson.Serialize(report);
        var html = Encoding.UTF8.GetString(ReportHtml.Render(json));

        Assert.Equal(2, Regex.Matches(html, "</script>", RegexOptions.IgnoreCase).Count);
        Assert.DoesNotContain("&amp;amp;", html, StringComparison.Ordinal);
        var parsed = JsonNode.Parse(ExtractData(html))!;
        Assert.Equal(hostile, parsed["definitions"]![0]!["name"]!.GetValue<string>());
    }

    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(ReportJson.ReadSchema()));

    internal static void AssertValid(byte[] reportJson)
    {
        var schema = Schema.Value;
        using var document = JsonDocument.Parse(reportJson);
        var result = schema.Evaluate(document.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });

        Assert.True(result.IsValid, string.Join(Environment.NewLine, (result.Details ?? []).Where(detail => detail.Errors is not null)
            .SelectMany(detail => detail.Errors!.Select(error => $"{detail.InstanceLocation}: {error.Value}"))));
    }

    private static string ExtractData(string html)
    {
        var match = Regex.Match(html, "<script type=\"application/json\" id=\"report-data\">(.*?)</script>", RegexOptions.Singleline);
        Assert.True(match.Success);
        return match.Groups[1].Value;
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Trim();

    private static IEnumerable<MigrationReport> SampleReports()
    {
        yield return ReportBuilder.Build(Input([Definition("plain", findings: [Finding(Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten, FindingSeverity.Warning)])]));

        yield return ReportBuilder.Build(Input([
            Definition("root", findings:
            [
                Finding(Elsa3ActivityDiagnosticCodes.UnmappedType, FindingSeverity.Error, componentName: "Acme.SendInvoice") with { NodeId = "n1" },
                Finding("ELSA4-PUBLISH", FindingSeverity.Error, FindingStage.Publish, path: null) with { PublishStoppedHere = true },
            ]),
            Definition("leaf", blockedBy: ["root-v1"], published: false, exportedTo: null) with { IsReusable = true },
        ]) with
        {
            NotConverted = [new ReportVersionReference("root", 0, "root-v0")],
            MissingTargets = [new ReportMissingTarget("absent", ["root"])],
            RunFindings = [Finding(Elsa3.Migration.Tooling.Elsa3MigrationToolingCodes.ImportRefused, FindingSeverity.Error, FindingStage.Import, path: null)],
            InputFindings = [new CollectedInputFinding("sub/bad.json", Finding(Elsa3.Migration.Tooling.Elsa3MigrationToolingCodes.InputUnreadable, FindingSeverity.Error, FindingStage.Input, path: null))],
            AllVersions = true,
            Command = "assess"
        });
    }
}
