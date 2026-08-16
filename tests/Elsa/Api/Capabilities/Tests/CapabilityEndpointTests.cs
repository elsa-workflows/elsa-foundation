using Elsa.Api.AspNetCore;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.Capabilities.Authorization;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Api.Capabilities.Tests;

public sealed class CapabilityEndpointTests
{
    [Fact]
    public void Endpoint_is_canonical_authenticated_and_action_scoped()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var routes = new TestEndpointRouteBuilder(services);
        ApiCapabilitiesApi.MapApiCapabilitiesApi(routes);
        var endpoint = Assert.Single(routes.DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>());

        var owner = Assert.IsType<EndpointOwnershipMetadata>(endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>());
        Assert.Equal("Elsa.Api.Capabilities", owner.OwnerId);
        Assert.Equal(EndpointAuthoringModels.MinimalApi, endpoint.Metadata.GetMetadata<EndpointAuthoringMetadata>()?.Model);
        var security = Assert.IsType<EndpointSecurityDispositionMetadata>(
            endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>());
        Assert.Equal(EndpointSecurityDispositionKind.Permission, security.Kind);
        var policy = new PermissionPolicyCodec().Parse(security.Value!);
        Assert.Contains(PermissionKey.Normalize(ApiCapabilitiesPermissions.Read), policy.Descriptor!.Permissions);
        Assert.DoesNotContain(endpoint.Metadata, item => item is Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute);
    }

    [Fact]
    public async Task Separate_shell_scopes_expose_only_their_explicit_relative_declarations()
    {
        await using var designShell = BuildShell(new ApiCapabilityDeclaration(
            "elsa.api.workflow-design", 1,
            [new("workflow-definitions", "design/workflows/definitions")], "WorkflowsDesignApi"));
        await using var runtimeShell = BuildShell(new ApiCapabilityDeclaration(
            "elsa.api.runtime", 1,
            [new("workflow-executables", "runtime/workflows/executables")], "WorkflowsRuntimeApi"));

        var design = await designShell.GetRequiredService<IApiCapabilityCatalog>().GetAsync();
        var runtime = await runtimeShell.GetRequiredService<IApiCapabilityCatalog>().GetAsync();

        Assert.Equal("elsa.api.workflow-design", Assert.Single(design.Capabilities).Id);
        Assert.Equal("elsa.api.runtime", Assert.Single(runtime.Capabilities).Id);
        Assert.DoesNotContain("default/", design.Capabilities.SelectMany(x => x.Links).Select(x => x.Href));
        Assert.DoesNotContain("default/", runtime.Capabilities.SelectMany(x => x.Links).Select(x => x.Href));
    }

    private static ServiceProvider BuildShell(ApiCapabilityDeclaration declaration)
    {
        var services = new ServiceCollection();
        services.AddApiCapabilities();
        services.AddApiCapability(declaration);
        return services.BuildServiceProvider();
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
