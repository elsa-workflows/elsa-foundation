using CShells.Features;
using Elsa.Http.Core.Contracts;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Http.Contracts;
using Elsa.Workflows.Runtime.Http.Options;
using Elsa.Workflows.Runtime.Http.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Reflection;
using Xunit;

namespace Elsa.Workflows.Runtime.Http.Tests;

public sealed class WorkflowsRuntimeStandaloneHttpFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersTheRouteTablePopulators()
    {
        var services = new ServiceCollection();

        new WorkflowsRuntimeStandaloneHttpFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IWorkflowTriggerIndexObserver));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IWorkflowTriggerIndexValidator));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IBookmarkLifecycleObserver));
        Assert.Contains(services, d => d.ServiceType == typeof(IHttpEndpointRoutesResolver));
        // The single serialization point every refresh routes through — registered once as a singleton (review fix).
        var synchronizer = Assert.Single(services, d => d.ServiceType == typeof(IHttpEndpointRouteTableSynchronizer));
        Assert.Equal(ServiceLifetime.Singleton, synchronizer.Lifetime);
    }

    // Kept beside the composition test: the configured convergence bound flows into the registered pump's options.
    [Fact]
    public void ConfigureServices_RegistersTheConvergencePump_WithTheConfiguredBound()
    {
        // #2190: the observers fire only on the node that made a change, so without this pump an endpoint published on
        // another node 404s here until a restart.
        var services = new ServiceCollection();

        new WorkflowsRuntimeStandaloneHttpFeature
        {
            RouteTableConvergenceIntervalSeconds = 0.5,
            RouteTableConvergenceMaxBackoffSeconds = 30
        }.ConfigureServices(services);

        var pump = Assert.Single(services, d => d.ServiceType == typeof(IRecurringTask));
        Assert.Equal(typeof(HttpEndpointRouteTableConvergencePumpTask), pump.ImplementationType);
        Assert.Equal(ServiceLifetime.Singleton, pump.Lifetime);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HttpEndpointRouteTableConvergenceOptions>>().Value;
        Assert.Equal(TimeSpan.FromMilliseconds(500), options.Interval);
        Assert.Equal(TimeSpan.FromSeconds(30), options.MaxBackoffInterval);
    }

    [Fact]
    public void ConfigureServices_DefaultsTheConvergenceBoundToFiveSeconds()
    {
        var services = new ServiceCollection().AddLogging();

        new WorkflowsRuntimeStandaloneHttpFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<HttpEndpointRouteTableConvergenceOptions>>().Value;
        Assert.Equal(TimeSpan.FromSeconds(5), options.Interval);
        Assert.Equal(TimeSpan.FromMinutes(1), options.MaxBackoffInterval);
        using var scope = provider.CreateScope();
        Assert.IsType<Services.HttpEndpointFaultHandler>(scope.ServiceProvider.GetRequiredService<IHttpEndpointFaultHandler>());
        Assert.IsType<Services.AuthenticationBasedHttpEndpointAuthorizationHandler>(
            scope.ServiceProvider.GetRequiredService<IHttpEndpointAuthorizationHandler>());
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(-1, 60)]
    [InlineData(double.NaN, 60)]
    [InlineData(5, 0)]
    [InlineData(5, double.PositiveInfinity)]
    public void ConfigureServices_RefusesANonPositiveConvergenceSetting(double intervalSeconds, double maxBackoffSeconds)
    {
        var feature = new WorkflowsRuntimeStandaloneHttpFeature
        {
            RouteTableConvergenceIntervalSeconds = intervalSeconds,
            RouteTableConvergenceMaxBackoffSeconds = maxBackoffSeconds
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => feature.ConfigureServices(new ServiceCollection()));
    }

    [Fact]
    public void DependsOn_IncludesTasks_SoTheStartupTaskAndTheConvergencePumpRun()
    {
        var attribute = typeof(WorkflowsRuntimeStandaloneHttpFeature).GetCustomAttribute<ShellFeatureAttribute>()!;

        Assert.Contains("Tasks", attribute.DependsOn.Cast<object>().Select(d => d.ToString()));
    }
}
