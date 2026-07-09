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
    public void Describe_StampsAuthoredOptions_OnEveryDescriptor()
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.SupportedMethods)] = LiteralCollectionBinding(nameof(HttpEndpoint.SupportedMethods), ["GET", "POST"]),
            [nameof(HttpEndpoint.Authorize)] = LiteralJsonBinding(nameof(HttpEndpoint.Authorize), "true"),
            [nameof(HttpEndpoint.Policy)] = LiteralBinding(nameof(HttpEndpoint.Policy), "orders-admin"),
            [nameof(HttpEndpoint.RequestTimeout)] = LiteralBinding(nameof(HttpEndpoint.RequestTimeout), "00:00:30"),
            [nameof(HttpEndpoint.RequestSizeLimit)] = LiteralJsonBinding(nameof(HttpEndpoint.RequestSizeLimit), "1048576")
        };

        var descriptors = _provider.Describe(NodeWith(bindings));

        Assert.Equal(2, descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            Assert.Equal("true", descriptor.Metadata[HttpEndpointRouting.AuthorizeMetadataKey]);
            Assert.Equal("orders-admin", descriptor.Metadata[HttpEndpointRouting.PolicyMetadataKey]);
            Assert.Equal("00:00:30", descriptor.Metadata[HttpEndpointRouting.RequestTimeoutMetadataKey]);
            Assert.Equal("1048576", descriptor.Metadata[HttpEndpointRouting.RequestSizeLimitMetadataKey]);
        }
    }

    [Fact]
    public void Describe_AbsentOptions_AreOmittedFromMetadata_AndHashUnchanged()
    {
        var node = EndpointNode(path: "orders/webhook");

        var descriptor = Assert.Single(_provider.Describe(node));

        Assert.DoesNotContain(HttpEndpointRouting.AuthorizeMetadataKey, descriptor.Metadata.Keys);
        Assert.DoesNotContain(HttpEndpointRouting.PolicyMetadataKey, descriptor.Metadata.Keys);
        Assert.DoesNotContain(HttpEndpointRouting.RequestTimeoutMetadataKey, descriptor.Metadata.Keys);
        Assert.DoesNotContain(HttpEndpointRouting.RequestSizeLimitMetadataKey, descriptor.Metadata.Keys);
        // Identity invariance: absent options leave the routing key as the bare (template, method) hash.
        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook", "GET"), descriptor.StimulusHash);
    }

    [Fact]
    public void Describe_AuthorizeFalseLiteral_OmitsAuthorizeKey()
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.Authorize)] = LiteralJsonBinding(nameof(HttpEndpoint.Authorize), "false")
        };

        var descriptor = Assert.Single(_provider.Describe(NodeWith(bindings)));

        Assert.DoesNotContain(HttpEndpointRouting.AuthorizeMetadataKey, descriptor.Metadata.Keys);
    }

    [Theory]
    [InlineData(nameof(HttpEndpoint.Authorize))]
    [InlineData(nameof(HttpEndpoint.Policy))]
    [InlineData(nameof(HttpEndpoint.RequestTimeout))]
    [InlineData(nameof(HttpEndpoint.RequestSizeLimit))]
    public void Describe_Throws_WhenOptionNonLiteral(string optionInput)
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [optionInput] = ExpressionBinding(optionInput)
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings)));
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:00:01")]
    public void Describe_Throws_WhenRequestTimeoutNonPositive(string timeout)
    {
        // Review C2: a non-positive timeout would arm CancelAfter with an invalid value at request time —
        // the authoring error fails the publish instead.
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.RequestTimeout)] = LiteralBinding(nameof(HttpEndpoint.RequestTimeout), timeout)
        };

        var exception = Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings)));
        Assert.Contains("non-positive", exception.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Describe_Throws_WhenRequestSizeLimitNonPositive(string sizeLimit)
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.RequestSizeLimit)] = LiteralJsonBinding(nameof(HttpEndpoint.RequestSizeLimit), sizeLimit)
        };

        var exception = Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings)));
        Assert.Contains("non-positive", exception.Message);
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

    [Fact]
    public void Describe_Throws_WhenSupportedMethodsContainsNonStringElements()
    {
        // [5, true] — a literal JSON array whose elements are not strings must fail the publish rather than coerce
        // each element to garbage via ToString().
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.SupportedMethods)] = RawLiteralBinding(nameof(HttpEndpoint.SupportedMethods), "[5, true]")
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings)));
    }

    [Fact]
    public void Describe_Throws_WhenSupportedMethodsMixesStringAndNonString()
    {
        // ["GET", {}] — a single non-string element is enough to fail the publish.
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.SupportedMethods)] = RawLiteralBinding(nameof(HttpEndpoint.SupportedMethods), """["GET", {}]""")
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings)));
    }

    [Fact]
    public void Describe_Throws_WhenPathLiteralIsNotAString()
    {
        // A non-string Path literal (a number here) is routing-significant and must throw, not ToString()-coerce.
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = RawLiteralBinding(nameof(HttpEndpoint.Path), "42")
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

    private static RuntimeInputBinding LiteralJsonBinding(string name, string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return new RuntimeInputBinding(name, RuntimeInputBindingSource.Literal, literalValue: document.RootElement.Clone());
    }

    private static RuntimeInputBinding LiteralCollectionBinding(string name, IReadOnlyCollection<string> values)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));
        return new RuntimeInputBinding(name, RuntimeInputBindingSource.Literal, literalValue: document.RootElement.Clone());
    }

    private static RuntimeInputBinding RawLiteralBinding(string name, string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return new RuntimeInputBinding(name, RuntimeInputBindingSource.Literal, literalValue: document.RootElement.Clone());
    }

    private static RuntimeInputBinding ExpressionBinding(string name) =>
        new(name, RuntimeInputBindingSource.Expression, expression: new RuntimeExpressionBinding("JavaScript", "input.foo"));
}
