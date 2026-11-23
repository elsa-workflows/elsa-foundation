using System.Globalization;
using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Activities.Http.StandaloneTriggers;

/// <summary>Pure source-owned reservation preparation. The common Runtime provider arbitrates and commits it.</summary>
internal sealed class HttpTriggerReservationPlanner : ITriggerReservationPlanner
{
    public string TypeKey => "http";
    public ValueTask<IReadOnlyList<RuntimeIngressReservationDescriptor>> PlanAsync(TriggerScope scope, TriggerAggregate? current,
        TriggerStoreWrite proposed, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var trigger = proposed.Trigger;
        if (trigger.TypeKey != TypeKey) throw new TriggerOperationException("validation", "The HTTP planner received a different trigger type.");
        if (trigger.DesiredState == TriggerDesiredState.Deleted || trigger.DesiredState != TriggerDesiredState.Enabled &&
            !trigger.HasActivated && current?.Trigger.HasActivated != true)
            return ValueTask.FromResult<IReadOnlyList<RuntimeIngressReservationDescriptor>>([]);
        var configuration = trigger.Configuration;
        var template = HttpEndpointStimulus.NormalizeTemplate(Required(configuration, "path"));
        var method = Required(configuration, "method").ToUpperInvariant();
        var response = configuration.GetProperty("http"); var mode = Required(response, "mode");
        var policy = response.TryGetProperty("admissionPolicy", out var admission) ? admission.GetString() : "RequireAll";
        if (mode is not ("WorkflowResponse" or "AsyncFanOut") || mode == "AsyncFanOut" && policy is not ("RequireAll" or "AcceptAvailable"))
            throw new TriggerOperationException("validation", "The HTTP response mode or admission policy is invalid.");
        if (mode == "WorkflowResponse" && trigger.DesiredState == TriggerDesiredState.Enabled && proposed.Subscriptions.Count(row => row.Enabled) != 1)
            throw new TriggerOperationException("validation", "Workflow-owned HTTP response requires exactly one enabled subscription.");
        var authorize = Required(configuration, "sourceAccess") switch
        { "Authenticated" => true, "Anonymous" => false, _ => throw new TriggerOperationException("validation", "Choose an explicit HTTP source access policy.") };
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [HttpEndpointRouting.TemplateMetadataKey] = template,
            [HttpEndpointRouting.MethodMetadataKey] = method.ToLowerInvariant(),
            [HttpEndpointRouting.AuthorizeMetadataKey] = authorize.ToString(CultureInfo.InvariantCulture),
            [HttpEndpointRouting.ResponseModeMetadataKey] = mode == "WorkflowResponse" ? "Sync" : "Async",
            ["http:standaloneMode"] = mode,
            ["http:triggerState"] = trigger.DesiredState.ToString(),
            ["http:admissionPolicy"] = mode == "AsyncFanOut" ? policy! : "RequireAll"
        };
        return ValueTask.FromResult<IReadOnlyList<RuntimeIngressReservationDescriptor>>(
            [new(HttpEndpointRouting.StimulusType, HttpEndpointStimulus.Hash(template, method), metadata)]);
    }
    private static string Required(JsonElement json, string name) => json.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new TriggerOperationException("validation", "The HTTP source configuration is incomplete.");
}
