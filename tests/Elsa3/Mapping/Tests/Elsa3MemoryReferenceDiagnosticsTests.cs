using System.Text.Json;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3MemoryReferenceImportFixture;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3MemoryReferenceDiagnosticsTests
{
    [Theory]
    [InlineData("dangling", "VF-IMP-001", "$.root.Value.memoryReference", "dangling", "/root", "key:value")]
    [InlineData("multiple-producer", "VF-IMP-002", "$.root.activities[2].Value.memoryReference", "shared", "/root/consumer", "key:value")]
    [InlineData("cross-subtree", "VF-IMP-003", "$.root.activities[1].activities[0].Value.memoryReference", "shared", "/root/right/consumer", "key:value")]
    [InlineData("ambiguous", "VF-IMP-002", "$.root.activities[1].Value.memoryReference", "shared", "/root/consumer", "key:value")]
    [InlineData("cyclic", "VF-IMP-004", "$.root.activities[0].Input.memoryReference", "b-result", "/root/a", "key:input")]
    [InlineData("custom", "VF-IMP-007", "$.root.Mystery.memoryReference", "custom", "/root", "Mystery")]
    [InlineData("mutation-script", "VF-IMP-005", "$.root.Value.expression.value", null, "/root", "key:value")]
    [InlineData("dynamic-script", "VF-IMP-006", "$.root.Value.expression.value", null, "/root", "key:value")]
    [InlineData("incompatible", "VF-IMP-008", "$.root.Value.typeName", null, "/root", "key:value")]
    public async Task Import_invalid_reference_graph_returns_path_specific_diagnostic(
        string kind,
        string expectedCode,
        string expectedJsonPath,
        string? expectedMemoryId,
        string expectedActivityPath,
        string expectedPropertyKey)
    {
        var testCase = CreateCase(kind);
        var fixture = new Elsa3MemoryReferenceImportFixture(testCase.Shapes);

        var result = await fixture.ImportAsync(testCase.Definition, $"{kind}.json");

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        var diagnostic = Assert.Single(result.Diagnostics, x => x.Code == expectedCode);
        Assert.Equal(expectedJsonPath, diagnostic.Path);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Guidance));
        Assert.Equal($"{kind}.json", diagnostic.Metadata["SourceName"]);
        Assert.Equal(kind, diagnostic.Metadata["DefinitionId"]);
        Assert.Equal(expectedActivityPath.Split('/').Last(), diagnostic.Metadata["ActivityNodeId"]);
        Assert.Equal(expectedActivityPath, diagnostic.Metadata["ActivityPath"]);
        Assert.Equal(expectedPropertyKey, diagnostic.Metadata["StablePropertyKey"]);

        if (expectedMemoryId is null)
            Assert.False(diagnostic.Metadata.ContainsKey("MemoryReferenceId"));
        else
            Assert.Equal(expectedMemoryId, diagnostic.Metadata["MemoryReferenceId"]);
    }

    [Fact]
    public void Reject_live_instance_state_uses_importer_specific_diagnostic()
    {
        var fixture = new Elsa3MemoryReferenceImportFixture();

        var result = fixture.RejectWorkflowInstanceState("instance.json");

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal("VF-IMP-009", diagnostic.Code);
        Assert.Equal("instance.json", diagnostic.Metadata["SourceName"]);
        Assert.Equal(Elsa3MigrationInputKind.WorkflowInstanceState.ToString(), diagnostic.Metadata["InputKind"]);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Guidance));
    }

    private static InvalidImportCase CreateCase(string kind) => kind switch
    {
        "dangling" => DanglingCase(),
        "multiple-producer" => MultipleProducerCase(),
        "cross-subtree" => CrossSubtreeCase(),
        "ambiguous" => AmbiguousCase(),
        "cyclic" => CyclicCase(),
        "custom" => CustomCase(),
        "mutation-script" => MutationScriptCase(),
        "dynamic-script" => DynamicScriptCase(),
        "incompatible" => IncompatibleCase(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown diagnostic fixture."),
    };

    private static InvalidImportCase DanglingCase() => Case(
        Workflow("dangling", Activity("root", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = MemoryReference("dangling"),
        })),
        Consumer());

    private static InvalidImportCase MultipleProducerCase()
    {
        var producer1 = Activity("producer-1", "Elsa.Producer", new Dictionary<string, JsonElement>
        {
            ["Result"] = MemoryReference("shared"),
        });
        var producer2 = Activity("producer-2", "Elsa.Producer", new Dictionary<string, JsonElement>
        {
            ["Result"] = MemoryReference("shared"),
        });
        var consumer = Activity("consumer", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = MemoryReference("shared"),
        });
        return Case(
            Workflow("multiple-producer", Activity("root", "Elsa.Sequence", children: [producer1, producer2, consumer])),
            Container(), Producer(), Consumer());
    }

    private static InvalidImportCase CrossSubtreeCase()
    {
        var producer = Activity("producer", "Elsa.Producer", new Dictionary<string, JsonElement>
        {
            ["Result"] = MemoryReference("shared"),
        });
        var consumer = Activity("consumer", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = MemoryReference("shared"),
        });
        var left = Activity("left", "Elsa.Sequence", children: [producer]);
        var right = Activity("right", "Elsa.Sequence", children: [consumer]);
        return Case(
            Workflow("cross-subtree", Activity("root", "Elsa.Sequence", children: [left, right])),
            Container(), Producer(), Consumer());
    }

    private static InvalidImportCase AmbiguousCase()
    {
        var producer = Activity("producer", "Elsa.Producer", new Dictionary<string, JsonElement>
        {
            ["Result"] = MemoryReference("shared"),
        });
        var consumer = Activity("consumer", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = MemoryReference("shared"),
        });
        var definition = Workflow(
            "ambiguous",
            Activity("root", "Elsa.Sequence", children: [producer, consumer]),
            variables:
            [
                new Elsa3Variable { Id = "shared", Name = "shared-variable", TypeName = "String" },
            ]);
        return Case(definition, Container(), Producer(), Consumer());
    }

    private static InvalidImportCase CyclicCase()
    {
        var a = Activity("a", "Elsa.Transform", new Dictionary<string, JsonElement>
        {
            ["Input"] = MemoryReference("b-result"),
            ["Output"] = MemoryReference("a-result"),
        });
        var b = Activity("b", "Elsa.Transform", new Dictionary<string, JsonElement>
        {
            ["Input"] = MemoryReference("a-result"),
            ["Output"] = MemoryReference("b-result"),
        });
        return Case(
            Workflow("cyclic", Activity("root", "Elsa.Sequence", children: [a, b])),
            Container(), ActivityShape.Create("Elsa.Transform", inputs: ["Input"], outputs: ["Output"]));
    }

    private static InvalidImportCase CustomCase() => Case(
        Workflow("custom", Activity("root", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Mystery"] = MemoryReference("custom"),
        })),
        Consumer());

    private static InvalidImportCase DynamicScriptCase() => Case(
        Workflow("dynamic-script", Activity("root", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = Argument("JavaScript", "getVariable(variableName)"),
        })),
        Consumer());

    private static InvalidImportCase MutationScriptCase() => Case(
        Workflow("mutation-script", Activity("root", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = Argument("JavaScript", "setVariable(\"total\", 42)"),
        })),
        Consumer());

    private static InvalidImportCase IncompatibleCase() => Case(
        Workflow("incompatible", Activity("root", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = Argument("Literal", "not-an-integer"),
        })),
        ActivityShape.Create("Elsa.Consumer", inputs: ["Value"], inputType: "Int32"));

    private static InvalidImportCase Case(Elsa3WorkflowDefinition definition, params ActivityShape[] shapes) =>
        new(definition, shapes);

    private static ActivityShape Consumer() => ActivityShape.Create("Elsa.Consumer", inputs: ["Value"]);
    private static ActivityShape Producer() => ActivityShape.Create("Elsa.Producer", outputs: ["Result"]);
    private static ActivityShape Container() => ActivityShape.Create("Elsa.Sequence");

    private sealed record InvalidImportCase(Elsa3WorkflowDefinition Definition, ActivityShape[] Shapes);
}
