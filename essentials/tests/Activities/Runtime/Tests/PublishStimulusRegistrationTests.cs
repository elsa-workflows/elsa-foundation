using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class PublishStimulusRegistrationTests
{
    [Fact]
    public void Repeated_feature_registration_resolves_one_workflow_capability_without_an_external_ingress()
    {
        var services = new ServiceCollection(); var feature = new ActivitiesPrimitivesFeature();
        feature.ConfigureServices(services); feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var capability = Assert.Single(scope.ServiceProvider.GetServices<StandaloneTriggerSourceCapability>());
        Assert.Equal("Elsa.Event.Broadcast", capability.Descriptor.SourceFamily);
        Assert.Equal("event", capability.Descriptor.TypeKey);
        Assert.Equal(PublishStimulusConstants.PublishStimulusIntentKind, capability.WorkflowIntentKind);
        Assert.True(capability.SupportsResourceAuthorization);
        var handler = Assert.Single(scope.ServiceProvider.GetServices<RuntimePostCommitIntentHandlerContribution>());
        Assert.True(handler.RequiresDurableSource);
        Assert.Equal(StimulusDeliverySnapshot.CurrentVersion, handler.StimulusDeliveryProtocolVersion);
        Assert.Equal(RuntimeSubscriptionDelivery.CurrentVersion, handler.SubscriptionDeliveryProtocolVersion);
        Assert.Empty(scope.ServiceProvider.GetServices<StandaloneTriggerSourceDescriptor>());
        Assert.DoesNotContain(services, descriptor => descriptor.ServiceType.IsGenericType &&
            (descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IStandaloneTriggerIngress<>) ||
             descriptor.ServiceType.GetGenericTypeDefinition() == typeof(IStandaloneTriggerSourceContext<>)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void A_foreign_producer_cannot_take_the_canonical_Event_identity_in_either_registration_order(bool workflowFirst)
    {
        var services = new ServiceCollection(); var feature = new ActivitiesPrimitivesFeature();
        var foreign = new StandaloneTriggerSourceDescriptor("foreign", 1, "Elsa.Event.Broadcast", new ValueTypeDescriptor("foreign", schemaVersion: 1));
        if (workflowFirst)
        {
            feature.ConfigureServices(services);
            Assert.Throws<InvalidOperationException>(() => services.AddStandaloneTriggerIngress<ForeignSource>(foreign));
        }
        else
        {
            services.AddStandaloneTriggerIngress<ForeignSource>(foreign);
            Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(services));
        }
    }

    private sealed class ForeignSource;
}
