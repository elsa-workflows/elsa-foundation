using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Api.Capabilities.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Interchange.Tests;

public sealed class BpmnDirectPublishingFeatureTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Authored_publication_is_mapped_and_advertised_only_by_its_optional_feature(bool enabled)
    {
        var builder = WebApplication.CreateBuilder();
        var interchange = new ActivitiesBpmnInterchangeFeature();
        var publishing = new ActivitiesBpmnDirectPublishingFeature();
        interchange.ConfigureServices(builder.Services);
        if (enabled)
        {
            publishing.ConfigureServices(builder.Services);
            publishing.ConfigureServices(builder.Services);
        }
        var descriptors = builder.Services.Where(value => value.ServiceType == typeof(BpmnAuthoredDirectPublisher)).ToArray();
        if (enabled) Assert.Equal(ServiceLifetime.Scoped, Assert.Single(descriptors).Lifetime);
        else Assert.Empty(descriptors);
        await using var app = builder.Build();
        interchange.MapEndpoints(app, null);
        if (enabled) publishing.MapEndpoints(app, null);
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).ToArray();
        Assert.Equal(enabled ? 4 : 3, routes.Length);
        Assert.Equal(enabled, routes.Any(route => route!.EndsWith("publish-direct", StringComparison.Ordinal)));
        var capabilities = app.Services.GetServices<ApiCapabilityDeclaration>()
            .Where(value => value.CapabilityId == "elsa.api.bpmn-publishing").ToArray();
        if (enabled) Assert.Equal("bpmn-authored-publish", Assert.Single(Assert.Single(capabilities).Links).Rel);
        else Assert.Empty(capabilities);
    }
}
