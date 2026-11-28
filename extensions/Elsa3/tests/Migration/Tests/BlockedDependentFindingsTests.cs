using System.Text.Json;
using Elsa3.Migration.Tests.Support;
using Elsa3.Migration.Tooling;

namespace Elsa3.Migration.Tests;

/// <summary>
/// #6242: a definition that uses a refused reusable workflow stays blocked, but one inventory run still lists its own
/// findings, directly and through a chain of reusables, each once.
/// </summary>
[Collection(nameof(CompositionCollection))]
public sealed class BlockedDependentFindingsTests
{
    private const string Unmapped = "ELS3-ACTIVITY-UNMAPPED-TYPE";
    private const string FunctionMissing = "ELS3-JS-HOST-FUNCTION-MISSING";
    private const string Blocked = "ELS3-RECONCILE-BLOCKED-BY-DEPENDENCY";
    private const string IntrinsicUnsupported = "ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED";
    private const string OutcomeUnmapped = "ELS3-ACTIVITY-GRAPH-OUTCOME-UNMAPPED";

    [Fact]
    public async Task A_workflow_using_a_refused_reusable_reports_its_own_findings()
    {
        using var host = new TestHostFolder();
        var export = Export(host,
            ("refused.json", RefusedReusable("acme-refused")),
            ("consumer.json", Definition("acme-consumer", reusable: false,
                [ReusableNode("UseRefused", "acme-refused"), UnknownNode("Unknown", "Acme.Unknown"), ScriptNode("Script", "return someFunction();")])));

        using var report = await InventoryAsync(host, export);

        var definitions = Definitions(report.RootElement);
        var consumer = definitions["acme-consumer"];
        Assert.Equal(["acme-refused-v1"], BlockedBy(consumer));
        var findings = Findings(consumer);
        Assert.Single(findings, finding => finding.Code == Blocked);
        Assert.Contains((Unmapped, "Acme.Unknown"), findings);
        Assert.Contains((FunctionMissing, "someFunction"), findings);
        Assert.Contains(IntrinsicUnsupported, Findings(definitions["acme-refused"]).Select(finding => finding.Code));
        AssertEachFindingOnce(report.RootElement, ("Acme.Unknown", "acme-consumer"), ("someFunction", "acme-consumer"));
    }

    [Fact]
    public async Task A_chain_through_a_reusable_with_findings_of_its_own_reports_every_link()
    {
        using var host = new TestHostFolder();
        var export = Export(host,
            ("refused.json", RefusedReusable("acme-refused")),
            ("middle.json", Definition("acme-middle", reusable: true,
                [ReusableNode("UseRefused", "acme-refused"), ScriptNode("Script", "return someFunction();")])),
            ("outer.json", Definition("acme-outer", reusable: false,
                [ReusableNode("UseMiddle", "acme-middle"), UnknownNode("Unknown", "Acme.Unknown")])));

        using var report = await InventoryAsync(host, export);

        var definitions = Definitions(report.RootElement);
        var middle = Findings(definitions["acme-middle"]);
        var outer = Findings(definitions["acme-outer"]);
        Assert.Contains((FunctionMissing, "someFunction"), middle);
        Assert.Contains((Unmapped, "Acme.Unknown"), outer);
        Assert.Equal(["acme-refused-v1"], BlockedBy(definitions["acme-middle"]));
        // The middle reusable now has a finding of its own, so it is a root behind the outer workflow too.
        Assert.Equal(["acme-middle-v1", "acme-refused-v1"], BlockedBy(definitions["acme-outer"]));
        AssertEachFindingOnce(report.RootElement, ("Acme.Unknown", "acme-outer"), ("someFunction", "acme-middle"));
    }

    [Fact]
    public async Task A_chain_through_a_clean_reusable_names_only_the_refused_root()
    {
        using var host = new TestHostFolder();
        var export = Export(host,
            ("refused.json", RefusedReusable("acme-refused")),
            ("middle.json", Definition("acme-middle", reusable: true, [ReusableNode("UseRefused", "acme-refused")])),
            ("outer.json", Definition("acme-outer", reusable: false,
                [ReusableNode("UseMiddle", "acme-middle"), ScriptNode("Script", "return someFunction();")])));

        using var report = await InventoryAsync(host, export);

        var definitions = Definitions(report.RootElement);
        Assert.Equal([Blocked], Findings(definitions["acme-middle"]).Where(NotRoutine).Select(finding => finding.Code));
        Assert.Equal(["acme-refused-v1"], BlockedBy(definitions["acme-outer"]));
        Assert.Contains((FunctionMissing, "someFunction"), Findings(definitions["acme-outer"]));
        AssertEachFindingOnce(report.RootElement, ("someFunction", "acme-outer"));
    }

    [Fact]
    public async Task Routing_on_an_outcome_only_the_refused_reusables_contract_could_settle_is_not_reported()
    {
        using var host = new TestHostFolder();
        var export = Export(host,
            ("refused.json", RefusedReusable("acme-refused")),
            ("consumer.json", Definition("acme-consumer", reusable: false,
                [ReusableNode("UseRefused", "acme-refused"), UnknownNode("Unknown", "Acme.Unknown")],
                [Connection("UseRefused", "Escalated", "Unknown")])));

        using var report = await InventoryAsync(host, export);

        var findings = Findings(Definitions(report.RootElement)["acme-consumer"]);
        Assert.Contains((Unmapped, "Acme.Unknown"), findings);
        Assert.DoesNotContain(findings, finding => finding.Code == OutcomeUnmapped);
    }

    private static void AssertEachFindingOnce(JsonElement root, params (string Component, string DefinitionId)[] expected)
    {
        var components = root.GetProperty("components").EnumerateArray()
            .ToDictionary(component => component.GetProperty("name").GetString()!, component => component);
        Assert.Equal(expected.Length, components.Count);
        foreach (var (component, definitionId) in expected)
        {
            Assert.Equal(1, components[component].GetProperty("usages").GetInt32());
            Assert.Equal([definitionId], components[component].GetProperty("definitions").EnumerateArray().Select(item => item.GetString()));
        }

        foreach (var definition in root.GetProperty("definitions").EnumerateArray())
        {
            var codes = definition.GetProperty("findings").EnumerateArray().Select(finding => finding.GetRawText()).ToArray();
            Assert.Equal(codes.Distinct().Count(), codes.Length);
        }
    }

    private static async Task<JsonDocument> InventoryAsync(TestHostFolder host, string export)
    {
        var (result, output) = await FixtureRun.RunAsync(host, MigrationCommand.Inventory, export);
        Assert.Null(result.Refusal);
        return FixtureRun.ReadReport(output);
    }

    private static string Export(TestHostFolder host, params (string File, string Json)[] files)
    {
        var export = Path.Combine(host.Root, "export");
        Directory.CreateDirectory(export);
        foreach (var (file, json) in files)
            File.WriteAllText(Path.Combine(export, file), json);
        return export;
    }

    private static Dictionary<string, JsonElement> Definitions(JsonElement root) =>
        root.GetProperty("definitions").EnumerateArray()
            .ToDictionary(definition => definition.GetProperty("definitionId").GetString()!, definition => definition);

    private static string[] BlockedBy(JsonElement definition) =>
        definition.GetProperty("blockedBy").EnumerateArray().Select(item => item.GetString()!).ToArray();

    private static List<(string Code, string? ComponentName)> Findings(JsonElement definition) =>
        definition.GetProperty("findings").EnumerateArray()
            .Select(finding => (finding.GetProperty("code").GetString()!,
                finding.TryGetProperty("componentName", out var name) && name.ValueKind == JsonValueKind.String ? name.GetString() : null))
            .ToList();

    private static bool NotRoutine((string Code, string? ComponentName) finding) => finding.Code != "ELS3-NULL-POLICIES";

    // Sets a variable, which a reusable workflow cannot yet (ELS3-ACTIVITY-REUSABLE-INTRINSIC-UNSUPPORTED).
    private static string RefusedReusable(string definitionId) => Definition(definitionId, reusable: true,
        [
            """
            {
              "id": "SetRate", "nodeId": "Workflow1:SetRate", "name": "SetRate", "type": "Elsa.SetVariable", "version": 1,
              "variable": { "id": "RateVar", "name": "Rate", "typeName": "String", "value": null },
              "value": { "typeName": "Object", "expression": { "type": "Literal", "value": "acme-rate" } }
            }
            """
        ],
        variables: """[{ "id": "RateVar", "name": "Rate", "typeName": "String", "isArray": false, "value": null, "storageDriverTypeName": null }]""");

    private static string Definition(string definitionId, bool reusable, string[] activities, string[]? connections = null, string variables = "[]") => $$"""
        {
          "id": "{{definitionId}}-v1",
          "definitionId": "{{definitionId}}",
          "name": "{{Name(definitionId)}}",
          "version": 1,
          "toolVersion": "3.6.3.0",
          "variables": {{variables}},
          "inputs": [],
          "outputs": [],
          "outcomes": [],
          "customProperties": {},
          "isLatest": true,
          "isPublished": true,
          "options": { "usableAsActivity": {{(reusable ? "true" : "false")}}, "autoUpdateConsumingWorkflows": false },
          "root": {
            "id": "Flowchart1", "nodeId": "Workflow1:Flowchart1", "name": "Flowchart1", "type": "Elsa.Flowchart", "version": 1,
            "activities": [{{string.Join(",", activities)}}],
            "connections": [{{string.Join(",", connections ?? [])}}],
            "variables": []
          }
        }
        """;

    private static string ReusableNode(string id, string targetDefinitionId) => $$"""
        {
          "id": "{{id}}", "nodeId": "Workflow1:{{id}}", "name": "{{id}}", "type": "{{Name(targetDefinitionId)}}", "version": 1,
          "workflowDefinitionId": "{{targetDefinitionId}}",
          "workflowDefinitionVersionId": "{{targetDefinitionId}}-v1",
          "latestAvailablePublishedVersion": 1,
          "latestAvailablePublishedVersionId": "{{targetDefinitionId}}-v1"
        }
        """;

    private static string UnknownNode(string id, string type) => $$"""
        { "id": "{{id}}", "nodeId": "Workflow1:{{id}}", "name": "{{id}}", "type": "{{type}}", "version": 1 }
        """;

    private static string ScriptNode(string id, string script) => $$"""
        {
          "id": "{{id}}", "nodeId": "Workflow1:{{id}}", "name": "{{id}}", "type": "Elsa.RunJavaScript", "version": 1,
          "script": { "typeName": "String", "expression": { "type": "Literal", "value": "{{script}}" } }
        }
        """;

    private static string Connection(string source, string port, string target) => $$"""
        { "source": { "activity": "{{source}}", "port": "{{port}}" }, "target": { "activity": "{{target}}", "port": "In" }, "vertices": [] }
        """;

    // Elsa 3 names a reusable's activity type after the definition's name.
    private static string Name(string definitionId) =>
        string.Concat(definitionId.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
}
