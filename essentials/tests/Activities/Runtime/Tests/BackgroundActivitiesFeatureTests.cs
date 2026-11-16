using Elsa.Activities.Runtime.Services;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class BackgroundActivitiesFeatureTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CoordinatorCapabilityCanBeEnabledWithoutStartingLocalWorkers(bool runWorkers)
    {
        var services = new ServiceCollection();
        new BackgroundActivitiesFeature { RunWorkers = runWorkers, MaxConcurrency = 7 }.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var settings = provider.GetRequiredService<IOptions<BackgroundActivityWorkerOptions>>().Value;
        Assert.True(settings.Enabled);
        Assert.Equal(7, settings.MaxConcurrency);
        Assert.Equal(runWorkers ? 1 : 0, services.Count(descriptor => descriptor.ServiceType == typeof(IBackgroundTask)));
    }

    [Theory]
    [InlineData(0, 30, 10)]
    [InlineData(257, 30, 10)]
    [InlineData(2, 10, 10)]
    [InlineData(2, 10, 20)]
    [InlineData(2, 10, 0)]
    public void InvalidWorkerLimitsFailComposition(int concurrency, double leaseSeconds, double renewalSeconds)
    {
        var feature = new BackgroundActivitiesFeature
        {
            MaxConcurrency = concurrency,
            LeaseDurationSeconds = leaseSeconds,
            RenewalIntervalSeconds = renewalSeconds
        };
        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));
    }
}
