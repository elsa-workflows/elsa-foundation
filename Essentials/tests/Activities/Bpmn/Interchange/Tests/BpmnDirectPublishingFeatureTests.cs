using Elsa.Activities.Bpmn.Interchange.Services;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Design;
using Elsa.Workflows.Design.Core.Models;
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
    public async Task Optional_assessment_has_zero_or_one_owner_after_repeated_feature_composition(bool enabled)
    {
        var services = new ServiceCollection();
        var design = new ActivitiesBpmnDesignFeature();
        design.ConfigureServices(services); design.ConfigureServices(services);
        if (enabled)
        {
            var direct = new ActivitiesBpmnDirectPublishingFeature();
            direct.ConfigureServices(services); direct.ConfigureServices(services);
        }
        await Startup(services).ExecuteAsync(default);
        Assert.Equal(enabled ? 1 : 0, services.Count(descriptor => descriptor.ServiceType == typeof(IBpmnDocumentDirectAdmission)));
    }

    [Fact]
    public async Task An_explicit_single_owner_precedes_the_optional_default()
    {
        var services = new ServiceCollection(); var replacement = new ReplacementAdmission();
        services.AddScoped<IBpmnDocumentDirectAdmission>(_ => replacement);
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        new ActivitiesBpmnDirectPublishingFeature().ConfigureServices(services);
        await Startup(services).ExecuteAsync(default);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        Assert.Same(replacement, scope.ServiceProvider.GetRequiredService<IBpmnDocumentDirectAdmission>());
    }

    [Fact]
    public async Task Competing_assessors_fail_startup_instead_of_short_circuiting_an_implementation()
    {
        var services = new ServiceCollection();
        new ActivitiesBpmnDesignFeature().ConfigureServices(services);
        new ActivitiesBpmnDirectPublishingFeature().ConfigureServices(services);
        services.AddScoped<IBpmnDocumentDirectAdmission, ReplacementAdmission>();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Startup(services).ExecuteAsync(default));
        Assert.Contains("at most one implementation (2 registrations)", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Copied_shell_startup_validates_its_own_final_registration_collection()
    {
        var parent = new ServiceCollection();
        var feature = new ActivitiesBpmnDesignFeature(); feature.ConfigureServices(parent);
        new ActivitiesBpmnDirectPublishingFeature().ConfigureServices(parent);
        IServiceCollection child = new ServiceCollection(); foreach (var descriptor in parent) child.Add(descriptor);
        feature.ConfigureServices(child);
        child.AddScoped<IBpmnDocumentDirectAdmission, ReplacementAdmission>();
        await Startup(parent).ExecuteAsync(default);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Startup(child).ExecuteAsync(default));
    }

    private static ValidateBpmnDirectAdmissionReplacementStartupTask Startup(IServiceCollection services) =>
        Assert.Single(services.Select(descriptor => descriptor.ImplementationInstance).OfType<ValidateBpmnDirectAdmissionReplacementStartupTask>());

    private sealed class ReplacementAdmission : IBpmnDocumentDirectAdmission
    { public bool CanPromote(WorkflowDefinitionState state) => throw new InvalidOperationException("replacement failure"); }

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
        Assert.Equal(enabled ? 1 : 0, builder.Services.Count(value => value.ServiceType == typeof(IBpmnDocumentDirectAdmission)));
        await using var app = builder.Build();
        interchange.MapEndpoints(app, null);
        if (enabled) publishing.MapEndpoints(app, null);
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Select(endpoint => endpoint.RoutePattern.RawText).ToArray();
        Assert.Equal(enabled ? 5 : 4, routes.Length);
        Assert.Single(routes, route => route!.EndsWith("interchange/bpmn/authoring-capabilities", StringComparison.Ordinal));
        Assert.Equal(enabled, routes.Any(route => route!.EndsWith("publish-direct", StringComparison.Ordinal)));
        var capabilities = app.Services.GetServices<ApiCapabilityDeclaration>()
            .Where(value => value.CapabilityId == "elsa.api.bpmn-publishing").ToArray();
        if (enabled) Assert.Equal(["bpmn-authored-claim-publish", "bpmn-authored-document-publish", "bpmn-authored-publish", "bpmn-authored-service-publish"], Assert.Single(capabilities).Links.Select(link => link.Rel));
        else Assert.Empty(capabilities);
    }
}
