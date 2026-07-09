using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Unit coverage for the <see cref="HttpEndpointTriggerStimulusProvider"/> (W16, on the W7 seam), mirroring the
/// Event trigger provider tests: it recognizes published <see cref="HttpEndpoint"/> nodes, derives the stimulus
/// from the authored <see cref="HttpEndpoint.Path"/> literal, returns null for other activity types, and fails
/// the publish when the path is not an authored literal.
/// </summary>
public sealed class HttpEndpointTriggerStimulusProviderTests
{
    private readonly HttpEndpointTriggerStimulusProvider _provider = new();

    [Fact]
    public void Describe_ReturnsHttpEndpointStimulus_ForEndpointNodeWithLiteralPath()
    {
        var node = EndpointNode(path: "orders/webhook");

        var descriptor = Assert.Single(_provider.Describe(node));

        Assert.Equal("HttpEndpoint", descriptor.StimulusType);
        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook"), descriptor.StimulusHash);
    }

    [Fact]
    public void Describe_ReturnsNull_ForNonHttpEndpointActivityType()
    {
        var node = EndpointNode(path: "orders/webhook", activityType: "Elsa.WriteLine");

        Assert.Empty(_provider.Describe(node));
    }

    [Fact]
    public void Describe_Throws_WhenPathLiteralMissing()
    {
        var node = EndpointNode(path: null);

        Assert.Throws<ArgumentException>(() => _provider.Describe(node));
    }

    [Fact]
    public void Hash_IsDeterministic_Prefixed_AndPathNormalized()
    {
        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook"), HttpEndpointStimulus.Hash("orders/webhook"));
        Assert.NotEqual(HttpEndpointStimulus.Hash("orders/webhook"), HttpEndpointStimulus.Hash("orders/other"));
        Assert.StartsWith("sha256:", HttpEndpointStimulus.Hash("orders/webhook"));

        // Case and surrounding slashes are normalized away, so equivalent routes hash identically.
        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook"), HttpEndpointStimulus.Hash("/Orders/Webhook/"));
    }

    private static ExecutableNode EndpointNode(string? path, string activityType = "Elsa.HttpEndpoint")
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        if (path is not null)
            bindings[nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), path);

        return new ExecutableNode(
            executableNodeId: "node-http-endpoint",
            authoredActivityId: "authored-node-http-endpoint",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: bindings,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding LiteralBinding(string name, string value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return new RuntimeInputBinding(name, RuntimeInputBindingSource.Literal, literalValue: document.RootElement.Clone());
    }
}
