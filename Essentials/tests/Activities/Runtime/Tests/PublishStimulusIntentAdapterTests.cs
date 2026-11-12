using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class PublishStimulusIntentAdapterTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 5, 0, 0, TimeSpan.Zero);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Owning_adapter_projects_immutable_input_key_correlation_and_local_publisher(bool local)
    {
        using var provider = Provider(); var adapter = Assert.Single(provider.GetServices<IStimulusDeliveryIntentAdapter>());
        var intent = Intent(local: local); var request = adapter.CreateRequest(intent);
        Assert.Equal(EventStimulus.StimulusType, request.StimulusType); Assert.Equal(EventStimulus.Hash("hello"), request.StimulusHash);
        Assert.Equal("original-key", request.IdempotencyKey); Assert.Equal("correlation", request.CorrelationId);
        Assert.Equal("retained-value", request.Input!.Value.GetProperty("value").GetString());
        Assert.Equal(local ? StimulusRoutingMode.ResumeOnly : StimulusRoutingMode.StartAndResume, request.Mode);
        Assert.Equal(local ? "publisher" : null, request.TargetWorkflowExecutionId); Assert.True(request.ResumeAllMatchingBookmarks);
        Assert.Null(request.MatchedTriggerBindings); Assert.Null(request.DispatchOptions); Assert.Null(request.PayloadType); Assert.Null(request.ProviderId);
    }

    [Fact]
    public void Owning_adapter_preserves_legacy_absent_local_flag_without_defaulting_other_routing_fields()
    {
        using var provider = Provider(); var adapter = Assert.Single(provider.GetServices<IStimulusDeliveryIntentAdapter>());
        var payload = JsonSerializer.SerializeToNode(Payload(false), Web)!; payload.AsObject().Remove("isLocalEvent");
        Assert.Equal(StimulusRoutingMode.StartAndResume, adapter.CreateRequest(Intent(payload: JsonSerializer.SerializeToElement(payload))).Mode);
    }

    [Theory]
    [InlineData("stimulusType")] [InlineData("stimulusHash")] [InlineData("eventName")] [InlineData("correlationId")] [InlineData("payload")]
    public void Owning_adapter_requires_every_staged_field_and_preserves_identity_in_domain_failure(string field)
    {
        using var provider = Provider(); var adapter = Assert.Single(provider.GetServices<IStimulusDeliveryIntentAdapter>());
        var payload = JsonSerializer.SerializeToNode(Payload(false), Web)!; payload.AsObject().Remove(field);
        var failure = Assert.Throws<StimulusDeliveryIntentException>(() => adapter.CreateRequest(Intent(payload: JsonSerializer.SerializeToElement(payload))));
        Assert.Equal("send", failure.IntentId); Assert.Equal(PublishStimulusConstants.PublishStimulusIntentKind, failure.IntentKind);
        Assert.IsType<JsonException>(failure.InnerException);
    }

    [Theory]
    [InlineData("kind")] [InlineData("type")] [InlineData("hash")] [InlineData("name")]
    [InlineData("unknown")] [InlineData("duplicate")] [InlineData("not-object")]
    public void Owning_adapter_refuses_malformed_or_conflicting_payload_without_raw_parser_exception(string defect)
    {
        using var provider = Provider(); var adapter = Assert.Single(provider.GetServices<IStimulusDeliveryIntentAdapter>());
        var node = JsonSerializer.SerializeToNode(Payload(false), Web)!;
        if (defect == "type") node["stimulusType"] = "wrong";
        if (defect == "hash") node["stimulusHash"] = "wrong";
        if (defect == "name") node["eventName"] = "";
        if (defect == "unknown") node["targetWorkflowExecutionId"] = "foreign";
        var json = node.ToJsonString();
        if (defect == "duplicate") json = json.Insert(json.LastIndexOf('}'), ",\"isLocalEvent\":true");
        var payload = JsonSerializer.Deserialize<JsonElement>(defect == "not-object" ? "[]" : json);
        var failure = Assert.Throws<StimulusDeliveryIntentException>(() => adapter.CreateRequest(Intent(payload: payload, kind: defect == "kind" ? "other-kind" : null)));
        Assert.Equal("send", failure.IntentId); Assert.NotNull(failure.InnerException);
    }

    private static ServiceProvider Provider()
    {
        var services = new ServiceCollection(); new ActivitiesPrimitivesFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }
    private static PublishStimulusIntentPayload Payload(bool local) => new(EventStimulus.StimulusType, EventStimulus.Hash("hello"),
        "hello", "correlation", JsonSerializer.SerializeToElement(new { value = "retained-value" }), local);
    private static RuntimePostCommitIntent Intent(bool local = false, JsonElement? payload = null, string? kind = null) =>
        new("send", "publisher", kind ?? PublishStimulusConstants.PublishStimulusIntentKind, Now, "publisher-activity", "original-key", payload ?? JsonSerializer.SerializeToElement(Payload(local), Web));
}
