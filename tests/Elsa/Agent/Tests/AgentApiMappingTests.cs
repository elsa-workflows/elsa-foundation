using Elsa.Agent.Api;
using Elsa.Api.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Tests;

public sealed class AgentApiMappingTests
{
    [Fact]
    public void Maps_exactly_eleven_named_operations()
    {
        using var services = new ServiceCollection().AddRouting().BuildServiceProvider();
        var app = new TestEndpointRouteBuilder(services);

        AgentApi.MapAgentApi(app);

        var names = app.DataSources
            .SelectMany(x => x.Endpoints)
            .Select(x => x.Metadata.GetMetadata<EndpointNameMetadata>()?.EndpointName)
            .Where(x => x is not null)
            .OrderBy(x => x)
            .ToArray();

        Assert.Equal(11, names.Length);
        Assert.All(names, name => Assert.StartsWith("ElsaAgentApiEndpoints", name, StringComparison.Ordinal));
    }

    private sealed class TestEndpointRouteBuilder(IServiceProvider serviceProvider) : IEndpointRouteBuilder
    {
        public IServiceProvider ServiceProvider { get; } = serviceProvider;
        public ICollection<EndpointDataSource> DataSources { get; } = [];
        public IApplicationBuilder CreateApplicationBuilder() => new ApplicationBuilder(ServiceProvider);
    }
}
