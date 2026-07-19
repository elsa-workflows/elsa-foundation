using System.Reflection;
using Elsa.Api.Capabilities;
using Elsa.Api.Capabilities.Contracts;
using Elsa.Api.Capabilities.Extensions;
using Elsa.Api.Capabilities.Models;
using Elsa.Api.FastEndpoints.Constants;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Api.Capabilities.Tests;

public sealed class CapabilityEndpointTests
{
    [Fact]
    public void Endpoint_is_canonical_authenticated_and_action_scoped()
    {
        var endpoint = CreateEndpoint();

        Assert.Contains("capabilities", endpoint.Definition.Routes);
        Assert.Contains(PermissionNames.ApiCapabilitiesRead, endpoint.Definition.AllowedPermissions!);
        Assert.Contains(PermissionNames.All, endpoint.Definition.AllowedPermissions!);
        Assert.Null(endpoint.Definition.AnonymousVerbs);
    }

    [Fact]
    public async Task Separate_shell_scopes_expose_only_their_explicit_relative_declarations()
    {
        await using var designShell = BuildShell(new ApiCapabilityDeclaration(
            "elsa.api.workflow-design",
            1,
            [new("workflow-definitions", "design/workflows/definitions")],
            "WorkflowsDesignApi"));
        await using var runtimeShell = BuildShell(new ApiCapabilityDeclaration(
            "elsa.api.runtime",
            1,
            [new("workflow-executables", "runtime/workflows/executables")],
            "WorkflowsRuntimeApi"));

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

    private static BaseEndpoint CreateEndpoint()
    {
        var endpointType = typeof(ApiCapabilitiesFeature).Assembly.GetTypes()
            .Single(type => type is { IsClass: true, IsAbstract: false } &&
                            typeof(BaseEndpoint).IsAssignableFrom(type));
        var dependencies = endpointType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .Single().GetParameters().Select(parameter => Resolve(parameter.ParameterType)).ToArray();
        var create = typeof(Factory).GetMethods()
            .Single(method => method.Name == nameof(Factory.Create) && method.IsGenericMethodDefinition &&
                              method.GetParameters() is [var first, var rest] &&
                              first.ParameterType == typeof(Action<DefaultHttpContext>) &&
                              rest.ParameterType == typeof(object[]))
            .MakeGenericMethod(endpointType);
        var endpoint = (BaseEndpoint)create.Invoke(null, [(Action<DefaultHttpContext>)(_ => { }), dependencies])!;
        endpoint.Configure();
        return endpoint;
    }

    private static object Resolve(Type type)
    {
        if (type == typeof(IApiCapabilityCatalog))
            return new StubCatalog();
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ILogger<>))
            return typeof(NullLogger<>).MakeGenericType(type.GenericTypeArguments[0]).GetProperty("Instance")!.GetValue(null)!;
        throw new InvalidOperationException($"Unexpected dependency {type}.");
    }

    private sealed class StubCatalog : IApiCapabilityCatalog
    {
        public Task<ApiCapabilitiesDocument> GetAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new ApiCapabilitiesDocument([]));
    }
}
