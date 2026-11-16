using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

public sealed partial class ChildControlContractTests
{
    [Theory]
    [InlineData("empty-terminal")]
    [InlineData("terminal-notice")]
    [InlineData("running-notice")]
    [InlineData("nullable-classification")]
    [InlineData("populated-control")]
    public void The_registered_payload_serializer_round_trips_complete_control_with_its_actual_null_policy(string scenario)
    {
        var services = new ServiceCollection();
        new SerializationFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var serializer = provider.GetRequiredService<IPayloadSerializer>();
        Assert.Equal(JsonIgnoreCondition.WhenWritingNull, serializer.GetOptions().DefaultIgnoreCondition);
        var batch = OmissionBatch(scenario);

        var text = serializer.Serialize(batch);
        var restored = serializer.Deserialize<RuntimeChildControlBatch>(text);
        Assert.Equal(text, serializer.Serialize(restored));
        var element = serializer.SerializeToElement(batch);
        var fromElement = serializer.Deserialize<RuntimeChildControlBatch>(element);
        Assert.True(JsonElement.DeepEquals(element, serializer.SerializeToElement(fromElement)));
        Assert.Equal(batch.AuthoredFailureCode, restored.AuthoredFailureCode);
        Assert.Equal(batch.Notifications.Select(item => item.Code), restored.Notifications.Select(item => item.Code));
        Assert.Equal(batch.Notifications.Select(item => item.Payload?.GetRawText()),
            restored.Notifications.Select(item => item.Payload?.GetRawText()));
        Assert.True(element.TryGetProperty("authoredFailureCode", out _));
        Assert.All(element.GetProperty("notifications").EnumerateArray(), item => Assert.True(item.TryGetProperty("payload", out _)));
    }

    public static IEnumerable<object[]> OmissionPolicies =>
        from naming in new[] { "default", "web", "snake" }
        from ignore in new[] { JsonIgnoreCondition.WhenWritingNull, JsonIgnoreCondition.WhenWritingDefault }
        from scenario in new[] { "empty-terminal", "terminal-notice" }
        select new object[] { naming, ignore, scenario };

    [Theory]
    [MemberData(nameof(OmissionPolicies))]
    public void Required_null_control_members_survive_omission_and_naming_policies(
        string naming, JsonIgnoreCondition ignore, string scenario)
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = ignore,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = naming switch
            {
                "web" => JsonNamingPolicy.CamelCase,
                "snake" => JsonNamingPolicy.SnakeCaseLower,
                _ => null
            }
        };
        var batch = OmissionBatch(scenario);
        var wire = JsonSerializer.Serialize(batch, options);

        var restored = JsonSerializer.Deserialize<RuntimeChildControlBatch>(wire, options)!;

        Assert.Equal(wire, JsonSerializer.Serialize(restored, options));
        Assert.Equal(batch.AuthoredFailureCode, restored.AuthoredFailureCode);
        Assert.Equal(batch.Notifications.Select(item => item.Payload?.GetRawText()),
            restored.Notifications.Select(item => item.Payload?.GetRawText()));
    }

    private static RuntimeChildControlBatch OmissionBatch(string scenario) => scenario switch
    {
        "empty-terminal" => new RuntimeChildControlBatch(1, "consumer", 1, Identity.ChildWorkflowExecutionId,
            "root", "source", 0, true),
        "terminal-notice" => Batch(terminal: true, failure: "ORDER", notifications: [new("notice")]),
        "running-notice" => Batch(notifications: [new("notice")]),
        "nullable-classification" => Batch(terminal: true,
            notifications: [new("notice", JsonSerializer.SerializeToElement(new { code = "WARNING" }))]),
        _ => Batch(terminal: true, failure: "ORDER",
            notifications: [new("notice", JsonSerializer.SerializeToElement(new { code = "WARNING" }))])
    };
}
