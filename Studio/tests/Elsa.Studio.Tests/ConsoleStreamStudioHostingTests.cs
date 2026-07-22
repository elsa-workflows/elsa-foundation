using Elsa.Studio.ConsoleStream;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests;

public sealed class ConsoleStreamStudioHostingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void HostsConsoleLogStreamingHostOnlyWhenFeatureEnabled(bool enabled)
    {
        var services = new ServiceCollection();

        services.AddConsoleStreamStudioHostIfEnabled(BuildConfiguration(enabled));

        Assert.Equal(enabled, HasConsoleLogStreamingHost(services));
    }

    [Fact]
    public void ShellFeatureDoesNotHostTheHub()
    {
        var services = new ServiceCollection();

        new ConsoleStreamStudioFeature().ConfigureServices(services);

        // The console-log-streaming host (capture, SignalR hub, HTTP endpoints) must not be hosted
        // in the shell container, or its connections would capture a service provider disposed on reload.
        // Module manifest contribution is now handled declaratively via [StudioModule] on the feature class.
        Assert.False(HasConsoleLogStreamingHost(services));
    }

    private static bool HasConsoleLogStreamingHost(IServiceCollection services) =>
        services.Any(descriptor =>
            IsConsoleLogStreamingType(descriptor.ServiceType) ||
            IsConsoleLogStreamingType(descriptor.ImplementationType));

    private static bool IsConsoleLogStreamingType(Type? type) =>
        type?.Namespace?.StartsWith("ConsoleLogStreaming", StringComparison.Ordinal) == true;

    private static IConfiguration BuildConfiguration(bool enabled)
    {
        var featureName = enabled ? ConsoleStreamHookInstaller.FeatureName : "StructuredLogs";

        return new ConfigurationBuilder()
            .AddInMemoryCollection([
                new KeyValuePair<string, string?>($"CShells:Shells:default:Features:{featureName}:Enabled", "true")
            ])
            .Build();
    }
}
