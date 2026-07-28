using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3MemoryReferenceImportFixture;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3MemoryReferenceImportTests
{
    [Theory]
    [InlineData("literal")]
    [InlineData("request")]
    [InlineData("variable")]
    [InlineData("result")]
    [InlineData("loop")]
    [InlineData("combined")]
    [InlineData("javascript")]
    [InlineData("liquid")]
    public async Task Import_valid_value_flow_lowers_to_canonical_authored_roles(string kind)
    {
        var testCase = CreateCase(kind);
        var fixture = new Elsa3MemoryReferenceImportFixture(testCase.Shapes);

        var result = await fixture.ImportAsync(testCase.Definition, $"{kind}.json");

        Assert.True(result.Succeeded, FormatDiagnostics(result.Diagnostics));
        Assert.NotNull(result.Value);
        testCase.AssertCanonical(result.Value);

        var stateJson = SerializeState(result.Value);
        foreach (var legacyMemoryId in testCase.LegacyMemoryIds)
            Assert.DoesNotContain(legacyMemoryId, stateJson, StringComparison.Ordinal);
    }

    private static ValidImportCase CreateCase(string kind) => kind switch
    {
        "literal" => LiteralCase(),
        "request" => WorkflowRequestCase(),
        "variable" => VariableCase(),
        "result" => ActivityResultCase(),
        "loop" => LoopCase(),
        "combined" => CombinedCase(),
        "javascript" => JavaScriptCase(),
        "liquid" => LiquidCase(),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown import fixture."),
    };

    private static ValidImportCase LiteralCase()
    {
        const string memoryId = "legacy:literal-input";
        var definition = Workflow("literal", Activity(
            "literal-node",
            "Elsa.Consumer",
            new Dictionary<string, JsonElement>
            {
                ["Value"] = Argument("Literal", "hello", memoryId),
            }));

        return Case(definition, [Consumer()], [memoryId], version =>
        {
            var value = ReadValue(FindInput(version, "literal-node", "Value"));
            Assert.Equal("Literal", value.ExpressionType);
            Assert.Equal("hello", value.Value.GetString());
        });
    }

    private static ValidImportCase WorkflowRequestCase()
    {
        const string memoryId = "legacy:request-input";
        var definition = Workflow(
            "request",
            Activity("request-node", "Elsa.Consumer", new Dictionary<string, JsonElement>
            {
                ["Value"] = Argument("JavaScript", "input.customerId", memoryId),
            }),
            inputs:
            [
                new Elsa3WorkflowArgumentDefinition { Name = "customerId", Type = "String" },
            ]);

        return Case(definition, [Consumer()], [memoryId], version =>
        {
            var expression = ReadPortableExpression(ReadValue(FindInput(version, "request-node", "Value")));
            Assert.Equal("JavaScript", expression.Language);
            Assert.Equal("args.customerId", expression.Source);
            var parameter = Assert.IsType<WorkflowRequestExpressionParameterBinding>(expression.Parameters["customerId"]);
            Assert.Equal("customerId", parameter.MemberKey);
        });
    }

    private static ValidImportCase VariableCase()
    {
        const string memoryId = "legacy:variable-input";
        var definition = Workflow(
            "variable",
            Activity("variable-node", "Elsa.Consumer", new Dictionary<string, JsonElement>
            {
                ["Value"] = Argument("JavaScript", "getVariable(\"total\")", memoryId),
            }),
            variables:
            [
                new Elsa3Variable { Id = "variable-total", Name = "total", TypeName = "Int32", Value = "0" },
            ]);

        return Case(definition, [Consumer()], [memoryId], version =>
        {
            var expression = ReadPortableExpression(ReadValue(FindInput(version, "variable-node", "Value")));
            Assert.Equal("args.total", expression.Source);
            var parameter = Assert.IsType<VariableExpressionParameterBinding>(expression.Parameters["total"]);
            Assert.Equal("workflow", parameter.DeclaringScopeNodeId);
            Assert.Equal("variable-total", parameter.VariableKey);
        });
    }

    private static ValidImportCase ActivityResultCase()
    {
        const string producerMemoryId = "legacy:receipt-result";
        const string consumerMemoryId = "legacy:receipt-consumer";
        var producer = Activity("producer", "Elsa.Producer", new Dictionary<string, JsonElement>
        {
            ["ReceiptId"] = MemoryReference(producerMemoryId),
        });
        var consumer = Activity("consumer", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = Argument("JavaScript", "getOutput(\"receiptId\")", consumerMemoryId),
        });
        var definition = Workflow("result", Activity("root", "Elsa.Sequence", children: [producer, consumer]));

        return Case(definition, [Container(), Producer(), Consumer()], [producerMemoryId, consumerMemoryId], version =>
        {
            var expression = ReadPortableExpression(ReadValue(FindInput(version, "consumer", "Value")));
            Assert.Equal("args.receiptId", expression.Source);
            var parameter = Assert.IsType<ActivityResultExpressionParameterBinding>(expression.Parameters["receiptId"]);
            Assert.Equal("producer", parameter.ProducerNodeId);
            Assert.Equal("key:receipt-id", parameter.ProjectionKey);
        });
    }

    private static ValidImportCase LoopCase()
    {
        const string memoryId = "legacy:loop-item";
        var loopBody = Activity("loop-body", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = MemoryReference(memoryId),
        });
        var loop = Activity("loop", "Elsa.ForEach", new Dictionary<string, JsonElement>
        {
            ["CurrentValue"] = MemoryReference(memoryId),
        }, [loopBody]);
        var definition = Workflow("loop", loop);

        return Case(definition, [ForEach(), Consumer()], [memoryId], version =>
        {
            var value = ReadValue(FindInput(version, "loop-body", "Value"));
            Assert.Equal("Variable", value.ExpressionType);
            Assert.Equal("loop", GetProperty(value.Value, "declaringScopeId").GetString());
            Assert.Equal("key:current-value", GetProperty(value.Value, "referenceKey").GetString());
        });
    }

    private static ValidImportCase JavaScriptCase()
    {
        var definition = Workflow("javascript", Activity("javascript-node", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = Argument("JavaScript", "40 + 2"),
        }));

        return Case(definition, [Consumer()], [], version =>
        {
            var expression = ReadPortableExpression(ReadValue(FindInput(version, "javascript-node", "Value")));
            Assert.Equal("JavaScript", expression.Language);
            Assert.Equal("40 + 2", expression.Source);
            Assert.Empty(expression.Parameters);
            Assert.Equal(ExpressionCapabilityProfiles.BindingPureV1, expression.CapabilityProfile);
        });
    }

    private static ValidImportCase CombinedCase()
    {
        const string memoryId = "legacy:combined";
        var transform = Activity("transform", "Elsa.Transform", new Dictionary<string, JsonElement>
        {
            ["Value"] = Argument("Literal", "seed", memoryId),
        });
        var consumer = Activity("consumer", "Elsa.Consumer", new Dictionary<string, JsonElement>
        {
            ["Value"] = MemoryReference(memoryId),
        });
        var definition = Workflow("combined", Activity("root", "Elsa.Sequence", children: [transform, consumer]));

        return Case(
            definition,
            [Container(), ActivityShape.Create("Elsa.Transform", inputs: ["Value"], outputs: ["Value"]), Consumer()],
            [memoryId],
            version =>
            {
                var input = ReadValue(FindInput(version, "transform", "Value"));
                Assert.Equal("Literal", input.ExpressionType);
                Assert.Equal("seed", input.Value.GetString());

                var result = ReadValue(FindInput(version, "consumer", "Value"));
                Assert.Equal("ActivityResult", result.ExpressionType);
                Assert.Equal("transform", GetProperty(result.Value, "producerNodeId").GetString());
                Assert.Equal("key:value", GetProperty(result.Value, "projectionKey").GetString());
            });
    }

    private static ValidImportCase LiquidCase()
    {
        const string memoryId = "legacy:liquid-input";
        var definition = Workflow(
            "liquid",
            Activity("liquid-node", "Elsa.Consumer", new Dictionary<string, JsonElement>
            {
                ["Value"] = Argument("Liquid", "{{ Variables.customer.name }}", memoryId),
            }),
            variables:
            [
                new Elsa3Variable { Id = "variable-customer", Name = "customer", TypeName = "Object" },
            ]);

        return Case(definition, [Consumer()], [memoryId], version =>
        {
            var expression = ReadPortableExpression(ReadValue(FindInput(version, "liquid-node", "Value")));
            Assert.Equal("Liquid", expression.Language);
            Assert.Equal("{{ customer.name }}", expression.Source);
            var parameter = Assert.IsType<VariableExpressionParameterBinding>(expression.Parameters["customer"]);
            Assert.Equal("variable-customer", parameter.VariableKey);
            Assert.Equal(ExpressionCapabilityProfiles.BindingPureV1, expression.CapabilityProfile);
        });
    }

    private static ValidImportCase Case(
        Elsa3WorkflowDefinition definition,
        ActivityShape[] shapes,
        string[] legacyMemoryIds,
        Action<Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionVersion> assertCanonical) =>
        new(definition, shapes, legacyMemoryIds, assertCanonical);

    private static ActivityShape Consumer() => ActivityShape.Create("Elsa.Consumer", inputs: ["Value"]);
    private static ActivityShape Producer() => ActivityShape.Create("Elsa.Producer", outputs: ["ReceiptId"]);
    private static ActivityShape Container() => ActivityShape.Create("Elsa.Sequence");
    private static ActivityShape ForEach() => ActivityShape.Create("Elsa.ForEach", outputs: ["CurrentValue"]);

    private static string FormatDiagnostics(IEnumerable<Elsa3MigrationDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(x => $"{x.Code}: {x.Message}"));

    private sealed record ValidImportCase(
        Elsa3WorkflowDefinition Definition,
        ActivityShape[] Shapes,
        string[] LegacyMemoryIds,
        Action<Elsa.Workflows.Design.Core.Contracts.IWorkflowDefinitionVersion> AssertCanonical);
}
