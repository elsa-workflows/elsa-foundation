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
    public void Runtime_output_names_preserve_case_and_distinct_names_in_the_actual_generated_contract()
    {
        var view = new WorkflowInstanceDetailsView(null!, [], [], new Dictionary<string, WorkflowOutputView>(StringComparer.Ordinal)
        {
            ["URL"] = Output("first"), ["url"] = Output("second"), ["ReviewData"] = Output("third")
        }, "Balanced", "Workflow");
        var type = RuntimeApiEndpointTestFactory.FindType("Elsa.Workflows.Runtime.Api.WorkflowsRuntimeJsonContext")!;
        var context = (JsonSerializerContext)type.GetProperty("Default")!.GetValue(null)!;
        var contract = context.GetTypeInfo(typeof(WorkflowInstanceDetailsView))!;
        var json = JsonSerializer.SerializeToElement(view, contract);
        var outputs = json.GetProperty("outputs");
        Assert.Equal(["URL", "url", "ReviewData"], outputs.EnumerateObject().Select(item => item.Name));
        Assert.Equal("Payload", outputs.GetProperty("URL").GetProperty("captureMode").GetString());
        Assert.Equal("first", outputs.GetProperty("URL").GetProperty("value").GetProperty("Note").GetString());
        var restored = (WorkflowInstanceDetailsView)JsonSerializer.Deserialize(json, contract)!;
        Assert.Equal(view.Outputs.Keys, restored.Outputs.Keys);
    }

    private static WorkflowOutputView Output(string note) => WorkflowOutputView.From(new WorkflowOutputProjection("Result", JsonSerializer.SerializeToElement(new { Note = note }), false, null, null!, DateTimeOffset.UnixEpoch) { CaptureMode = RuntimePayloadCaptureMode.Payload });
}
