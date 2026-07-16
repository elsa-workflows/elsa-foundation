using global::Elsa.Activities.Design.Reconciliation.Core;
using global::Elsa.Primitives.Models;
using global::Elsa.Samples.Nuplane.Activities;
using global::Elsa.Samples.Nuplane.Activities.Activities;
using global::Elsa.Samples.Nuplane.Activities.Reconciliation;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Samples.Nuplane.Activities.Tests;

public sealed class SampleNuplaneActivitiesFeatureTests
{
    [Fact]
    public void ConfigureServices_RegistersActivitySourceAndOptions()
    {
        var services = new ServiceCollection();

        new SampleNuplaneActivitiesFeature().ConfigureServices(services);

        Assert.Contains(services, x => x.ServiceType == typeof(IActivityReconciliationSource) && x.ImplementationType == typeof(SampleNuplaneActivityReconciliationSource));
        var options = Assert.IsType<SampleNuplaneActivityOptions>(
            Assert.Single(services, x => x.ServiceType == typeof(SampleNuplaneActivityOptions)).ImplementationInstance);
        Assert.Equal("Hello {recipient} from a Nuplane-loaded activity.", options.MessageTemplate);
    }

    [Fact]
    public async Task ReconciliationSource_ContributesSampleActivity()
    {
        var source = new SampleNuplaneActivityReconciliationSource();

        var activity = Assert.Single(await source.Read(CancellationToken.None));

        Assert.Equal(typeof(SayHelloFromNuplane).FullName, activity.ActivityTypeKey);
        Assert.Equal(typeof(ClrActivityDescriptor).FullName, activity.DescriptorType);
        Assert.Equal("Say Hello From Nuplane", activity.DisplayName);
        Assert.Contains(activity.Inputs, x => x.Name == nameof(SayHelloFromNuplane.Recipient));
    }

    [Fact]
    public void Activity_ReceivesConfigurationThroughConstructorInjection()
    {
        var activity = new SayHelloFromNuplane(new SampleNuplaneActivityOptions("Hello {recipient}.", true));
        Assert.NotNull(activity);
    }
}
