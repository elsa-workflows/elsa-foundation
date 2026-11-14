using Company.Connector.Activities;
using Elsa.Integrations.Sdk.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Company.Connector.Tests;

public sealed class PingTests
{
    [Fact]
    public async Task Harness_constructs_ping()
    {
        await using var harness = ConnectorHarness.Create(configure: services => services.AddTransient<Ping>());
        var ping = ActivatorUtilities.CreateInstance<Ping>(harness.Services);
        Assert.NotNull(ping);
    }
}
