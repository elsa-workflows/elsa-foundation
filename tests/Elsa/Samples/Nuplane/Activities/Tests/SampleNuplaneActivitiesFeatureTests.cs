using global::Elsa.Activities.Design.Reconciliation.Core;
using global::Elsa.Activities.Runtime.Core.Contracts;
using global::Elsa.Samples.Nuplane.Activities;
using global::Elsa.Samples.Nuplane.Activities.Activities;
using global::Elsa.Samples.Nuplane.Activities.Constructors;
using global::Elsa.Samples.Nuplane.Activities.Descriptors;
using global::Elsa.Samples.Nuplane.Activities.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Samples.Nuplane.Activities.Tests;

public sealed class SampleNuplaneActivitiesFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersActivitySourceAndConstructor()
    {
        var services = new ServiceCollection();

        new SampleNuplaneActivitiesFeature().ConfigureServices(services);

        Assert.Contains(services, x => x.ServiceType == typeof(IActivityReconciliationSource) && x.ImplementationType == typeof(SampleNuplaneActivityReconciliationSource));
        Assert.Contains(services, x => x.ServiceType == typeof(IActivityConstructor) && x.ImplementationType == typeof(SampleNuplaneActivityConstructor));
    }

    [Fact]
    public async Task ReconciliationSource_ContributesSampleActivity()
    {
        var source = new SampleNuplaneActivityReconciliationSource();

        var activity = Assert.Single(await source.Read(CancellationToken.None));

        Assert.Equal(typeof(SayHelloFromNuplane).FullName, activity.ActivityTypeKey);
        Assert.Equal(typeof(SampleNuplaneActivityDescriptor).FullName, activity.DescriptorType);
        Assert.Equal("Say Hello From Nuplane", activity.DisplayName);
        Assert.Contains(activity.Inputs, x => x.Name == nameof(SayHelloFromNuplane.Recipient));
    }

    [Fact]
    public async Task Constructor_CreatesSampleActivity()
    {
        var constructor = new SampleNuplaneActivityConstructor();

        var activity = await constructor.Construct(SampleNuplaneActivityDescriptor.Default, null, null, CancellationToken.None);

        Assert.IsType<SayHelloFromNuplane>(activity);
    }
}
