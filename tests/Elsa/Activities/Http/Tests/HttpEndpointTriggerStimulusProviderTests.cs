using System.Text.Json;
using Elsa.Activities.Http.Activities;
using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Http.Tests;

/// <summary>
/// Unit coverage for the <see cref="HttpEndpointTriggerStimulusProvider"/> (W16, on the W7 seam): it recognizes
/// published <see cref="HttpEndpoint"/> nodes, derives one <c>(template, method)</c> descriptor per supported
/// method from the authored literals, defaults to <c>GET</c> when methods are unauthored (elsa-core parity),
/// returns empty for other activity types, and fails the publish when a routing-significant input is not an
/// authored literal.
/// </summary>
public sealed class HttpEndpointTriggerStimulusProviderTests
{
    private readonly HttpEndpointTriggerStimulusProvider _provider = new();

    [Fact]
    public void Describe_UnauthoredMethods_YieldsSingleGetDescriptor()
    {
        var node = EndpointNode(path: "orders/webhook");

        var descriptor = Assert.Single(_provider.Describe(node));

        Assert.Equal(HttpEndpointRouting.StimulusType, descriptor.StimulusType);
        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook", "GET"), descriptor.StimulusHash);
        Assert.Equal("orders/webhook", descriptor.Metadata[HttpEndpointRouting.TemplateMetadataKey]);
        Assert.Equal("get", descriptor.Metadata[HttpEndpointRouting.MethodMetadataKey]);
    }

    [Fact]
    public void Describe_AuthoredMethods_YieldsOneDescriptorPerMethod()
    {
        var node = EndpointNode(path: "orders/{id}", methods: ["GET", "DELETE"]);

        var descriptors = _provider.Describe(node);

        Assert.Equal(2, descriptors.Count);
        Assert.Collection(
            descriptors, // deterministic lowercased-ordinal order: delete < get
            first =>
            {
                Assert.Equal(HttpEndpointStimulus.Hash("orders/{id}", "DELETE"), first.StimulusHash);
                Assert.Equal("orders/{id}", first.Metadata[HttpEndpointRouting.TemplateMetadataKey]);
                Assert.Equal("delete", first.Metadata[HttpEndpointRouting.MethodMetadataKey]);
            },
            second =>
            {
                Assert.Equal(HttpEndpointStimulus.Hash("orders/{id}", "GET"), second.StimulusHash);
                Assert.Equal("get", second.Metadata[HttpEndpointRouting.MethodMetadataKey]);
            });
    }

    [Fact]
    public void Describe_ReturnsEmpty_ForNonHttpEndpointActivityType()
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
    public void Describe_Throws_WhenPathNonLiteral()
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = ExpressionBinding(nameof(HttpEndpoint.Path))
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings)));
    }

    [Fact]
    public void Describe_Throws_WhenSupportedMethodsNonLiteral()
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.SupportedMethods)] = ExpressionBinding(nameof(HttpEndpoint.SupportedMethods))
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings)));
    }

    private static ExecutableNode EndpointNode(string? path, IReadOnlyCollection<string>? methods = null, string activityType = "Elsa.HttpEndpoint")
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        if (path is not null)
            bindings[nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), path);
        if (methods is not null)
            bindings[nameof(HttpEndpoint.SupportedMethods)] = LiteralCollectionBinding(nameof(HttpEndpoint.SupportedMethods), methods);

        return NodeWith(bindings, activityType);
    }

    private static ExecutableNode NodeWith(Dictionary<string, RuntimeInputBinding> bindings, string activityType = "Elsa.HttpEndpoint")
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
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

    private static RuntimeInputBinding LiteralCollectionBinding(string name, IReadOnlyCollection<string> values)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));
        return new RuntimeInputBinding(name, RuntimeInputBindingSource.Literal, literalValue: document.RootElement.Clone());
    }

    private static RuntimeInputBinding ExpressionBinding(string name) =>
        new(name, RuntimeInputBindingSource.Expression, expression: new RuntimeExpressionBinding("JavaScript", "input.foo"));
}
