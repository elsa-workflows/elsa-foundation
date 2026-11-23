using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Api.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed class WorkflowOutputWireTests
{
    [Fact]
    public void The_named_output_converter_preserves_the_complete_nested_JSON_value_through_the_real_owner_contract()
    {
        const string raw = """{ "OrderID":9007199254740993, "amount":1.00, "nil":null, "text":"\u0061 <+>" }""";
        JsonElement data;
        using (var document = JsonDocument.Parse(raw)) data = document.RootElement.Clone();
        var view = new WorkflowInstanceDetailsView(null!, [], [], new Dictionary<string, WorkflowOutputView>(StringComparer.Ordinal)
        {
            ["OrderID"] = new(data, false, null, DateTimeOffset.UnixEpoch)
        }, "Balanced", "Workflow");
        var contract = DetailsContract();
        var wire = JsonSerializer.SerializeToElement(view, contract);
        var value = wire.GetProperty("outputs").GetProperty("OrderID").GetProperty("value");
        Assert.Equal(raw, value.GetRawText());
        var restored = (WorkflowInstanceDetailsView)JsonSerializer.Deserialize(wire, contract)!;
        var retained = restored.Outputs["OrderID"].Value!.Value;
        Assert.Equal(raw, retained.GetRawText());
        Assert.Equal(9007199254740993L, retained.GetProperty("OrderID").GetInt64());
        Assert.Equal(JsonValueKind.Null, retained.GetProperty("nil").ValueKind);
        Assert.False(retained.TryGetProperty("missing", out _));
    }

    [Fact]
    public void Runtime_output_names_preserve_case_and_distinct_names_in_the_actual_generated_contract()
    {
        var view = new WorkflowInstanceDetailsView(null!, [], [], new Dictionary<string, WorkflowOutputView>(StringComparer.Ordinal)
        {
            ["URL"] = Output("first"), ["url"] = Output("second"), ["ReviewData"] = Output("third")
        }, "Balanced", "Workflow");
        var type = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.WorkflowsRuntimeJsonContext")!;
        var context = (JsonSerializerContext)type.GetProperty("WireContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        var contract = context.GetTypeInfo(typeof(WorkflowInstanceDetailsView))!;
        var json = JsonSerializer.SerializeToElement(view, contract);
        var outputs = json.GetProperty("outputs");
        Assert.Equal(["URL", "url", "ReviewData"], outputs.EnumerateObject().Select(item => item.Name));
        Assert.Equal("Payload", outputs.GetProperty("URL").GetProperty("captureMode").GetString());
        Assert.Equal("first", outputs.GetProperty("URL").GetProperty("value").GetProperty("Note").GetString());
        var restored = (WorkflowInstanceDetailsView)JsonSerializer.Deserialize(json, contract)!;
        Assert.Equal(view.Outputs.Keys, restored.Outputs.Keys);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"outputs\"")]
    [InlineData("{\"Result\":null}")]
    [InlineData("{\"Result\":§,\"Result\":§}")]
    public void Malformed_runtime_outputs_are_rejected_rather_than_partially_read(string outputs)
    {
        var contract = DetailsContract();
        var view = new WorkflowInstanceDetailsView(null!, [], [], new Dictionary<string, WorkflowOutputView>(StringComparer.Ordinal) { ["Result"] = Output("only") }, "Balanced", "Workflow");
        var json = JsonSerializer.SerializeToElement(view, contract);
        var valid = json.GetProperty("outputs").GetProperty("Result").GetRawText();
        var malformed = json.GetRawText().Replace(json.GetProperty("outputs").GetRawText(), outputs.Replace("§", valid));

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(malformed, contract));
    }

    [Fact]
    public void Truncated_runtime_outputs_are_rejected()
    {
        var contract = DetailsContract();
        var view = new WorkflowInstanceDetailsView(null!, [], [], new Dictionary<string, WorkflowOutputView>(StringComparer.Ordinal) { ["Result"] = Output("only") }, "Balanced", "Workflow");
        var text = JsonSerializer.SerializeToElement(view, contract).GetRawText();
        var truncated = text[..(text.IndexOf("\"Result\"", StringComparison.Ordinal) + "\"Result\"".Length)];

        Assert.ThrowsAny<JsonException>(() => JsonSerializer.Deserialize(truncated, contract));
    }

    private static System.Text.Json.Serialization.Metadata.JsonTypeInfo DetailsContract()
    {
        var type = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.WorkflowsRuntimeJsonContext")!;
        var context = (JsonSerializerContext)type.GetProperty("WireContext", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!.GetValue(null)!;
        return context.GetTypeInfo(typeof(WorkflowInstanceDetailsView))!;
    }

    private static WorkflowOutputView Output(string note) => WorkflowOutputView.From(new WorkflowOutputProjection("Result", JsonSerializer.SerializeToElement(new { Note = note }), false, null, null!, DateTimeOffset.UnixEpoch) { CaptureMode = RuntimePayloadCaptureMode.Payload });
}
