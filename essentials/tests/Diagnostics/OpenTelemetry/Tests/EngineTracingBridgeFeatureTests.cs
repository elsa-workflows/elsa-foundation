using System.Reflection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Workflows;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.OpenTelemetry.Tests;

public sealed class EngineTracingBridgeFeatureTests
{
    [Fact]
    public void Keeps_the_feature_name_and_dependencies_shells_select_it_by()
    {
        var feature = typeof(EngineTracingBridgeFeature).GetCustomAttribute<ShellFeatureAttribute>();

        Assert.NotNull(feature);
        Assert.Equal("DiagnosticsOpenTelemetryEngineBridge", feature.Name);
        Assert.Equal(["DiagnosticsOpenTelemetry", "WorkflowsRuntimeTracing"], feature.DependsOn.Cast<string>());
        Assert.True(typeof(EngineTracingBridgeFeature).IsPublic);
    }

    [Fact]
    public async Task Registers_one_bridge_that_its_startup_task_attaches_and_its_terminator_detaches()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IOpenTelemetryIngestor>(new NullIngestor());

        new EngineTracingBridgeFeature().ConfigureServices(services);

        Assert.Equal(ServiceLifetime.Singleton,
            Assert.Single(services, descriptor => descriptor.ServiceType == typeof(EngineTracingBridge)).Lifetime);
        var task = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStartupTask));
        Assert.Equal(ServiceLifetime.Scoped, task.Lifetime);

        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var startupTask = Assert.IsType<BridgeStartupTask>(scope.ServiceProvider.GetRequiredService<IStartupTask>());
        var terminator = Assert.Single(scope.ServiceProvider.GetServices<IShellTerminator>().OfType<BridgeTerminator>());

        await startupTask.ExecuteAsync(CancellationToken.None);
        await terminator.TerminateAsync(CancellationToken.None);
        // Stop re-arms Start, so the same singleton attaches again on the next activation.
        await startupTask.ExecuteAsync(CancellationToken.None);
        await terminator.TerminateAsync(CancellationToken.None);

        Assert.Same(provider.GetRequiredService<EngineTracingBridge>(), scope.ServiceProvider.GetRequiredService<EngineTracingBridge>());
    }

    private sealed class NullIngestor : IOpenTelemetryIngestor
    {
        public ValueTask IngestAsync(OpenTelemetryBatch batch, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
