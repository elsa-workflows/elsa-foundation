using System.Text.Json;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal sealed partial class TriggerManagement
{
    private static object? HttpSummary(StandaloneTrigger trigger)
    {
        if (!trigger.Configuration.TryGetProperty("http", out var http) || http.ValueKind != JsonValueKind.Object)
            return null;
        var mode = OptionalText(http, "mode");
        return mode is "WorkflowResponse" or "AsyncFanOut"
            ? new { mode, admissionPolicy = mode == "AsyncFanOut" ? OptionalText(http, "admissionPolicy") ?? "RequireAll" : null }
            : null;
    }
}
