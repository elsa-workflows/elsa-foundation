using Elsa.Persistence.Groundwork.Targets;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkProviderConnectionsTests
{
    [Fact]
    public void Default_target_falls_back_to_the_ordinary_connection()
    {
        using var connection = new SqliteProviderFactory().Create("Data Source=:memory:");
        var services = new ServiceCollection().AddSingleton(connection);
        using var provider = services.BuildServiceProvider();

        Assert.Same(connection, GroundworkProviderConnections.Resolve(provider, null, "Test"));
        Assert.Same(connection, GroundworkProviderConnections.Resolve(provider, "default", "Test"));
    }

    [Fact]
    public void Named_target_resolves_its_keyed_connection_before_the_default()
    {
        using var shared = new SqliteProviderFactory().Create("Data Source=:memory:");
        using var diagnostics = new SqliteProviderFactory().Create("Data Source=:memory:");
        var services = new ServiceCollection()
            .AddSingleton(shared)
            .AddKeyedSingleton("diagnostics", diagnostics);
        using var provider = services.BuildServiceProvider();

        Assert.Same(diagnostics, GroundworkProviderConnections.Resolve(provider, "diagnostics", "Test"));
        Assert.Same(shared, GroundworkProviderConnections.Resolve(provider, null, "Test"));
    }

    [Fact]
    public void Unregistered_named_target_fails_instead_of_silently_sharing_the_default()
    {
        using var shared = new SqliteProviderFactory().Create("Data Source=:memory:");
        using var provider = new ServiceCollection().AddSingleton(shared).BuildServiceProvider();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            GroundworkProviderConnections.Resolve(provider, "diagnostics", "Diagnostics persistence"));

        Assert.Contains("Diagnostics persistence target 'diagnostics'", exception.Message, StringComparison.Ordinal);
    }
}
