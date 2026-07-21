using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Http.Activities;
using Elsa.Http.Core;
using Elsa.Http.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
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
    public void ProviderId_IsExplicitAndStable()
    {
        Assert.Equal("Elsa.HttpEndpoint", ((IActivityTriggerStimulusProvider)_provider).ProviderId);
    }

    [Fact]
    public void Describe_UnauthoredCanStartWorkflow_IsRecognizedButYieldsNoDescriptors()
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/webhook")
        };

        var result = _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: false));

        Assert.Equal("Elsa.HttpEndpoint", ((IActivityTriggerStimulusProvider)_provider).ProviderId);
        Assert.True(result.IsRecognized);
        Assert.Empty(result.Descriptors);
    }

    [Fact]
    public void Describe_UnauthoredMethods_YieldsSingleGetDescriptor()
    {
        var node = EndpointNode(path: "orders/webhook");

        var descriptor = Assert.Single(_provider.Describe(node).Descriptors);

        Assert.Equal(HttpEndpointRouting.StimulusType, descriptor.StimulusType);
        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook", "GET"), descriptor.StimulusHash);
        Assert.Equal(TriggerCardinality.Exclusive, ((IActivityTriggerStimulusProvider)_provider).Cardinality);
        Assert.Equal("orders/webhook", descriptor.Metadata[HttpEndpointRouting.TemplateMetadataKey]);
        Assert.Equal("get", descriptor.Metadata[HttpEndpointRouting.MethodMetadataKey]);
    }

    [Fact]
    public void Describe_StableActivityIdentity_RecognizesOnlyHttpEndpoint()
    {
        var endpointNode = EndpointNode(path: "hello-world", activityType: HttpEndpoint.ActivityType);
        var otherClrNode = EndpointNode(path: "hello-world", activityType: typeof(WriteHttpResponse).FullName!);

        var result = _provider.Describe(endpointNode);

        Assert.Equal("Elsa.HttpEndpoint", HttpEndpoint.ActivityType);
        Assert.True(result.IsRecognized);
        Assert.Single(result.Descriptors);
        Assert.False(_provider.Describe(otherClrNode).IsRecognized);
    }

    [Fact]
    public void Describe_AuthoredMethods_YieldsOneDescriptorPerMethod()
    {
        var node = EndpointNode(path: "orders/{id}", methods: ["GET", "DELETE"]);

        var descriptors = _provider.Describe(node).Descriptors;

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
    public void Describe_DuplicateAndCaseVariantMethods_YieldOneBindingPerNormalizedMethod()
    {
        var node = EndpointNode(path: "orders/{id}", methods: ["GET", "get", "POST", "post"]);

        var descriptors = _provider.Describe(node).Descriptors;

        Assert.Equal(2, descriptors.Count);
        Assert.Equal(
            [HttpEndpointStimulus.Hash("orders/{id}", "GET"), HttpEndpointStimulus.Hash("orders/{id}", "POST")],
            descriptors.Select(x => x.StimulusHash));
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

        var descriptors = _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)).Descriptors;

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

        var descriptor = Assert.Single(_provider.Describe(node).Descriptors);

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

        var descriptor = Assert.Single(_provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)).Descriptors);

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

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
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

        var exception = Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
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

        var exception = Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
        Assert.Contains("non-positive", exception.Message);
    }

    [Fact]
    public void Describe_ResponseModeSyncLiteral_StampsSyncMode_OnEveryDescriptor()
    {
        // Spec 089 E-D1: a literal ResponseMode = "Sync" is read via ReadLiteralEnum and rides the binding metadata
        // on every method descriptor (so a resumed endpoint gets its mode for free — scenario 5.5).
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.SupportedMethods)] = LiteralCollectionBinding(nameof(HttpEndpoint.SupportedMethods), ["GET", "POST"]),
            [nameof(HttpEndpoint.ResponseMode)] = LiteralBinding(nameof(HttpEndpoint.ResponseMode), "Sync")
        };

        var descriptors = _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)).Descriptors;

        Assert.Equal(2, descriptors.Count);
        foreach (var descriptor in descriptors)
        {
            Assert.Equal("Sync", descriptor.Metadata[HttpEndpointRouting.ResponseModeMetadataKey]);
            // Non-identity: the mode never changes the routing key.
            Assert.Equal(
                HttpEndpointStimulus.Hash("orders/{id}", descriptor.Metadata[HttpEndpointRouting.MethodMetadataKey]),
                descriptor.StimulusHash);
        }
    }

    [Fact]
    public void Describe_ResponseModeAsyncLiteral_OmitsModeKey()
    {
        // Async is the default and contributes no key (round-trip identity with the unauthored default).
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/webhook"),
            [nameof(HttpEndpoint.ResponseMode)] = LiteralBinding(nameof(HttpEndpoint.ResponseMode), "Async")
        };

        var descriptor = Assert.Single(_provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)).Descriptors);

        Assert.DoesNotContain(HttpEndpointRouting.ResponseModeMetadataKey, descriptor.Metadata.Keys);
    }

    [Fact]
    public void Describe_UnauthoredResponseMode_OmitsModeKey()
    {
        var node = EndpointNode(path: "orders/webhook");

        var descriptor = Assert.Single(_provider.Describe(node).Descriptors);

        Assert.DoesNotContain(HttpEndpointRouting.ResponseModeMetadataKey, descriptor.Metadata.Keys);
    }

    [Fact]
    public void Describe_Throws_WhenResponseModeNonLiteral()
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.ResponseMode)] = ExpressionBinding(nameof(HttpEndpoint.ResponseMode))
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
    }

    [Theory]
    [InlineData("\"Sideways\"")] // undefined member name
    [InlineData("\"sync\"")] // wrong case — parse is case-sensitive
    [InlineData("99")] // undefined numeric value
    public void Describe_Throws_WhenResponseModeInvalid(string rawJson)
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.ResponseMode)] = RawLiteralBinding(nameof(HttpEndpoint.ResponseMode), rawJson)
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
    }

    [Fact]
    public void Describe_ReturnsNotRecognized_ForNonHttpEndpointActivityType()
    {
        var node = EndpointNode(path: "orders/webhook", activityType: "Elsa.WriteLine");

        Assert.False(_provider.Describe(node).IsRecognized);
    }

    [Fact]
    public void Describe_CanStartWorkflowFalse_IsRecognizedButYieldsNoDescriptors()
    {
        // Spec 089 D (T006): a mid-flow endpoint declares itself a non-start — the provider recognizes it (so the
        // extractor does NOT fail the publish) but produces zero descriptors (so no trigger bindings are indexed).
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.CanStartWorkflow)] = LiteralJsonBinding(nameof(HttpEndpoint.CanStartWorkflow), "false")
        };

        var result = _provider.Describe(NodeWith(bindings));

        Assert.Equal("Elsa.HttpEndpoint", ((IActivityTriggerStimulusProvider)_provider).ProviderId);
        Assert.True(result.IsRecognized);
        Assert.Empty(result.Descriptors);
    }

    [Fact]
    public void Describe_CanStartWorkflowTrue_IsStartCapable_AndHashUnaffectedByTheFlag()
    {
        // The flag is non-identity: an explicit CanStartWorkflow = true yields the normal (template, method) hash.
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/webhook"),
            [nameof(HttpEndpoint.CanStartWorkflow)] = LiteralJsonBinding(nameof(HttpEndpoint.CanStartWorkflow), "true")
        };

        var descriptor = Assert.Single(_provider.Describe(NodeWith(bindings)).Descriptors);

        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook", "GET"), descriptor.StimulusHash);
        // No routing metadata leaks the flag.
        Assert.DoesNotContain(descriptor.Metadata.Keys, key => key.Contains("canStart", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Describe_Throws_WhenCanStartWorkflowNonLiteral()
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.CanStartWorkflow)] = ExpressionBinding(nameof(HttpEndpoint.CanStartWorkflow))
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
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

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
    }

    [Fact]
    public void Describe_Throws_WhenSupportedMethodsNonLiteral()
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), "orders/{id}"),
            [nameof(HttpEndpoint.SupportedMethods)] = ExpressionBinding(nameof(HttpEndpoint.SupportedMethods))
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
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

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
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

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
    }

    [Fact]
    public void Describe_Throws_WhenPathLiteralIsNotAString()
    {
        // A non-string Path literal (a number here) is routing-significant and must throw, not ToString()-coerce.
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Path)] = RawLiteralBinding(nameof(HttpEndpoint.Path), "42")
        };

        Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));
    }

    [Fact]
    public void Describe_AbsentOptionalOptions_ApplyDeclaredDefaults_AndDoNotThrow()
    {
        // Issue #925: with only the required Path authored, every optional option falls back to its declared default
        // (methods → GET, response mode → Async, the rest omitted) instead of demanding an authored literal each.
        var node = EndpointNode(path: "orders/webhook");

        var descriptor = Assert.Single(_provider.Describe(node).Descriptors);

        Assert.Equal(HttpEndpointStimulus.Hash("orders/webhook", "GET"), descriptor.StimulusHash);
        Assert.Equal("get", descriptor.Metadata[HttpEndpointRouting.MethodMetadataKey]);
        Assert.DoesNotContain(HttpEndpointRouting.ResponseModeMetadataKey, descriptor.Metadata.Keys);
        Assert.DoesNotContain(HttpEndpointRouting.AuthorizeMetadataKey, descriptor.Metadata.Keys);
    }

    [Fact]
    public void Describe_AggregatesEveryOptionProblem_InOnePass()
    {
        // Issue #925: a missing required path plus several non-literal options are reported together in one preflight
        // pass, not surfaced one at a time across repeated publish attempts.
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(HttpEndpoint.Policy)] = ExpressionBinding(nameof(HttpEndpoint.Policy)),
            [nameof(HttpEndpoint.RequestTimeout)] = ExpressionBinding(nameof(HttpEndpoint.RequestTimeout)),
            [nameof(HttpEndpoint.RequestSizeLimit)] = ExpressionBinding(nameof(HttpEndpoint.RequestSizeLimit))
        };

        var exception = Assert.Throws<ArgumentException>(() => _provider.Describe(NodeWith(bindings, authorCanStartWorkflow: true)));

        Assert.Contains(nameof(HttpEndpoint.Path), exception.Message);
        Assert.Contains(nameof(HttpEndpoint.Policy), exception.Message);
        Assert.Contains(nameof(HttpEndpoint.RequestTimeout), exception.Message);
        Assert.Contains(nameof(HttpEndpoint.RequestSizeLimit), exception.Message);
    }

    private static ExecutableNode EndpointNode(
        string? path,
        IReadOnlyCollection<string>? methods = null,
        string activityType = HttpEndpoint.ActivityType)
    {
        var bindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.OrdinalIgnoreCase);
        if (path is not null)
            bindings[nameof(HttpEndpoint.Path)] = LiteralBinding(nameof(HttpEndpoint.Path), path);
        if (methods is not null)
            bindings[nameof(HttpEndpoint.SupportedMethods)] = LiteralCollectionBinding(nameof(HttpEndpoint.SupportedMethods), methods);

        return NodeWith(bindings, activityType, authorCanStartWorkflow: true);
    }

    private static ExecutableNode NodeWith(
        Dictionary<string, RuntimeInputBinding> bindings,
        string activityType = HttpEndpoint.ActivityType,
        bool authorCanStartWorkflow = false)
    {
        var effectiveBindings = new Dictionary<string, RuntimeInputBinding>(bindings, StringComparer.OrdinalIgnoreCase);
        if (authorCanStartWorkflow && !effectiveBindings.ContainsKey(nameof(HttpEndpoint.CanStartWorkflow)))
            effectiveBindings[nameof(HttpEndpoint.CanStartWorkflow)] = LiteralJsonBinding(nameof(HttpEndpoint.CanStartWorkflow), "true");

        using var document = JsonDocument.Parse("""{"type":"test"}""");
        return new ExecutableNode(
            executableNodeId: "node-http-endpoint",
            authoredActivityId: "authored-node-http-endpoint",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: effectiveBindings,
            metadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding LiteralBinding(string name, string value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return CanonicalLiteral(name, document.RootElement, "String");
    }

    private static RuntimeInputBinding LiteralJsonBinding(string name, string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return CanonicalLiteral(name, document.RootElement, "Elsa.Any");
    }

    private static RuntimeInputBinding LiteralCollectionBinding(string name, IReadOnlyCollection<string> values)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(values));
        return CanonicalLiteral(name, document.RootElement, "Elsa.Any");
    }

    private static RuntimeInputBinding RawLiteralBinding(string name, string rawJson)
    {
        using var document = JsonDocument.Parse(rawJson);
        return CanonicalLiteral(name, document.RootElement, "Elsa.Any");
    }

    private static RuntimeInputBinding ExpressionBinding(string name) =>
        new(
            name,
            new ValueTypeDescriptor("Elsa.Any"),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Expression,
            expression: new RuntimeExpressionBinding("JavaScript", "input.foo"));

    private static RuntimeInputBinding CanonicalLiteral(string name, JsonElement value, string typeAlias)
    {
        var type = new ValueTypeDescriptor(typeAlias);
        return new RuntimeInputBinding(
            name,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, value, ValueProtectionPolicy.InstanceInline));
    }
}
