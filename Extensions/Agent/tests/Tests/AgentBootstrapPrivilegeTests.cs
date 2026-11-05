using System.Text.Json;
using Elsa.Agent.Api;
using Elsa.Api.AspNetCore;
using Elsa.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

/// <summary>Actual mapped bootstrap and generated JSON report current host policy without accepting policy writes.</summary>
public sealed class AgentBootstrapPrivilegeTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Bootstrap_reports_each_host_class_independently_of_caller_query_values(bool administrative, bool irrecoverable, bool activation)
    {
        using var configuration = Configuration(administrative, irrecoverable, activation);
        using var root = Services(configuration);
        using var scope = root.CreateScope();

        var data = await BootstrapAsync(scope.ServiceProvider,
            $"?administrative={!administrative}&irrecoverable={!irrecoverable}&activation={!activation}");

        AssertPrivileges(data, administrative, irrecoverable, activation);
    }

    [Fact]
    public async Task Default_host_reports_all_three_off_without_an_effects_feature_or_configuration()
    {
        using var root = Services();
        using var scope = root.CreateScope();

        var data = await BootstrapAsync(scope.ServiceProvider, "?administrative=true&irrecoverable=true&activation=true");

        AssertPrivileges(data, false, false, false);
    }

    [Fact]
    public async Task An_existing_endpoint_and_scope_observe_real_configuration_reload_without_changing_an_earlier_response()
    {
        using var configuration = Configuration(false, false, false);
        using var root = Services(configuration);
        using var scope = root.CreateScope();
        var endpoint = BootstrapEndpoint(scope.ServiceProvider);
        var before = await SendAsync(endpoint, scope.ServiceProvider);

        configuration[Key(nameof(AgentPrivilegePolicyOptions.Administrative))] = "true";
        configuration[Key(nameof(AgentPrivilegePolicyOptions.Activation))] = "true";
        configuration.Reload();
        AssertPrivileges(await SendAsync(endpoint, scope.ServiceProvider), true, false, true);
        AssertPrivileges(before, false, false, false);

        configuration[Key(nameof(AgentPrivilegePolicyOptions.Administrative))] = "false";
        configuration[Key(nameof(AgentPrivilegePolicyOptions.Irrecoverable))] = "true";
        configuration.Reload();
        AssertPrivileges(await SendAsync(endpoint, scope.ServiceProvider), false, true, true);
        AssertPrivileges(before, false, false, false);
    }

    private static ServiceProvider Services(IConfiguration? configuration = null) => AgentTestServices.Create(services =>
    {
        services.AddRouting().AddElsaEndpoints();
        if (configuration is not null) services.AddSingleton(configuration);
    });

    private static Endpoint BootstrapEndpoint(IServiceProvider services)
    {
        var app = new TestEndpointRouteBuilder(services);
        AgentApi.MapAgentApi(app);
        var endpoints = app.DataSources.SelectMany(source => source.Endpoints).ToArray();
        Assert.DoesNotContain(endpoints.OfType<RouteEndpoint>(), endpoint =>
            endpoint.RoutePattern.RawText?.Contains("privileg", StringComparison.OrdinalIgnoreCase) == true);
        return Assert.Single(endpoints, endpoint => endpoint.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName == "ElsaAgentApiEndpointsBootstrap");
    }

    private static Task<JsonElement> BootstrapAsync(IServiceProvider services, string? query = null)
        => SendAsync(BootstrapEndpoint(services), services, query);

    private static async Task<JsonElement> SendAsync(Endpoint endpoint, IServiceProvider services, string? query = null)
    {
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.QueryString = new QueryString(query);
        await using var response = new MemoryStream();
        context.Response.Body = response;
        await endpoint.RequestDelegate!(context);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        using var document = await JsonDocument.ParseAsync(context.Response.Body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static void AssertPrivileges(JsonElement data, bool administrative, bool irrecoverable, bool activation)
    {
        var value = data.GetProperty("privileges");
        Assert.Equal(["activation", "administrative", "irrecoverable"], value.EnumerateObject().Select(x => x.Name).Order(StringComparer.Ordinal));
        Assert.Equal(administrative, value.GetProperty("administrative").GetBoolean());
        Assert.Equal(irrecoverable, value.GetProperty("irrecoverable").GetBoolean());
        Assert.Equal(activation, value.GetProperty("activation").GetBoolean());
    }

    private static string Key(string name) => AgentPrivilegePolicyOptions.SectionName + ":" + name;
    private static ConfigurationRoot Configuration(bool administrative, bool irrecoverable, bool activation)
        => (ConfigurationRoot)new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [Key(nameof(AgentPrivilegePolicyOptions.Administrative))] = administrative.ToString(),
            [Key(nameof(AgentPrivilegePolicyOptions.Irrecoverable))] = irrecoverable.ToString(),
            [Key(nameof(AgentPrivilegePolicyOptions.Activation))] = activation.ToString()
        }).Build();
}
