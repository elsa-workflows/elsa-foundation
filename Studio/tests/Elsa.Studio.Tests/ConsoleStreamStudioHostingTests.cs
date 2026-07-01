using Elsa.Studio.ConsoleStream;
using Elsa.Studio.ConsoleStream.Handlers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Tests;

public sealed class ConsoleStreamStudioHostingTests
{
    [Fact]
    public void RegistersConsoleLogStreamingHostWhenFeatureEnabled()
    {
        var services = new ServiceCollection();

        services.AddConsoleStreamStudioHostIfEnabled(BuildConfiguration(enabled: true));

        Assert.True(HasConsoleLogStreamingHost(services));
    }

    [Fact]
    public void DoesNotRegisterConsoleLogStreamingHostWhenFeatureDisabled()
    {
        var services = new ServiceCollection();

        services.AddConsoleStreamStudioHostIfEnabled(BuildConfiguration(enabled: false));

        Assert.False(HasConsoleLogStreamingHost(services));
    }

    [Fact]
    public void ShellFeatureContributesManifestButDoesNotHostTheHub()
    {
        var services = new ServiceCollection();

        new ConsoleStreamStudioFeature().ConfigureServices(services);

        // The UI module manifest contribution stays shell-scoped...
        Assert.Contains(services, descriptor => descriptor.ImplementationType == typeof(ContributeConsoleStreamStudioModule));
        // ...but the console-log-streaming host (capture, SignalR hub, HTTP endpoints) must not be hosted
        // in the shell container, or its connections would capture a service provider disposed on reload.
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
