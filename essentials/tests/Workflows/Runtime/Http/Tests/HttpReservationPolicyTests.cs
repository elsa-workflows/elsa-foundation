using Elsa.Http.Core;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class HttpReservationPolicyTests
{
    [Theory]
    [InlineData("orders/{id}", "POST", "orders/{orderId}", "post", true)]
    [InlineData("ORDERS/{id}", "POST", "orders/{orderId}", "POST", true)]
    [InlineData("orders/{id}", "POST", "orders/{orderId}", "GET", false)]
    [InlineData("orders/{id:int}", "POST", "orders/{name:int}", "POST", true)]
    [InlineData("orders/{id:int}", "POST", "orders/{name:guid}", "POST", false)]
    [InlineData("orders/{id}", null, "orders/{name}", "CUSTOM", true)]
    [InlineData("orders/{id}", "*", "orders/{name}", "HEAD", true)]
    [InlineData("orders/{id}", "POST", "other/{name}", "POST", false)]
    public void Registered_policy_matches_manifest_shapes_and_legacy_method_overlap(
        string firstRoute, string? firstMethod, string secondRoute, string? secondMethod, bool expected)
    {
        using var provider = Provider(); var policy = Assert.Single(provider.GetServices<IIngressReservationPolicy>());
        var first = Claim(firstRoute, firstMethod); var second = Claim(secondRoute, secondMethod);
        Assert.Equal(expected, policy.Conflicts(first, second));
        Assert.Equal(expected, policy.Conflicts(second, first));
        if (firstRoute != secondRoute) Assert.NotEqual(first.StimulusHash, second.StimulusHash);
    }

    [Theory]
    [InlineData("route")] [InlineData("syntax")] [InlineData("method")] [InlineData("type")]
    public void Malformed_claims_refuse_with_a_contextual_domain_failure(string defect)
    {
        using var provider = Provider(); var policy = Assert.Single(provider.GetServices<IIngressReservationPolicy>());
        var first = Claim("orders/{id}", "POST");
        var metadata = first.Metadata.ToDictionary(item => item.Key, item => item.Value);
        if (defect == "route") metadata.Remove(HttpEndpointRouting.TemplateMetadataKey);
        if (defect == "syntax") metadata[HttpEndpointRouting.TemplateMetadataKey] = "orders/{";
        if (defect == "method") metadata[HttpEndpointRouting.MethodMetadataKey] = "bad method";
        var malformed = first with { StimulusType = defect == "type" ? "foreign" : first.StimulusType, Metadata = metadata };
        var failure = Assert.Throws<TriggerOperationException>(() => policy.Conflicts(first, malformed));
        Assert.Equal("unavailable", failure.Code); Assert.NotNull(failure.InnerException);
    }

    [Fact]
    public void Repeated_feature_configuration_installs_one_semantic_policy()
    {
        var services = new ServiceCollection(); var feature = new WorkflowsRuntimeHttpFeature();
        feature.ConfigureServices(services); feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        Assert.Equal(HttpEndpointRouting.StimulusType, Assert.Single(provider.GetServices<IIngressReservationPolicy>()).StimulusType);
    }

    private static ServiceProvider Provider()
    { var services = new ServiceCollection(); new WorkflowsRuntimeHttpFeature().ConfigureServices(services); return services.BuildServiceProvider(); }
    private static RuntimeIngressReservationDescriptor Claim(string route, string? method)
    {
        var metadata = new Dictionary<string, string> { [HttpEndpointRouting.TemplateMetadataKey] = route };
        if (method is not null) metadata[HttpEndpointRouting.MethodMetadataKey] = method;
        return new(HttpEndpointRouting.StimulusType, "original-identity:" + route + ":" + method, metadata);
    }
}
