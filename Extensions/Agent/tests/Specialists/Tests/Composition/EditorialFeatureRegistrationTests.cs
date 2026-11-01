using Elsa.Agent.Specialists.Editorial;
using Elsa.Agent.Specialists.Editorial.Api;
using Elsa.Agent.Specialists.Editorial.Contracts;
using Elsa.Agent.Specialists.Editorial.Services;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Composition;

public sealed class EditorialFeatureRegistrationTests
{
    [Fact]
    public void Editorial_feature_registers_the_decision_service_guard_and_bounded_lifetime_once()
    {
        var services = new ServiceCollection();
        new SpecialistsEditorialFeature { DecisionLifetime = TimeSpan.FromMinutes(5) }.ConfigureServices(services);
        new SpecialistsEditorialFeature().ConfigureServices(services);

        var service = Assert.Single(services, x => x.ServiceType == typeof(IEditorialReviews));
        Assert.Equal(ServiceLifetime.Scoped, service.Lifetime);
        Assert.Equal(typeof(EditorialReviewService), service.ImplementationType);
        Assert.Single(services, x => x.ServiceType == typeof(IEventHandler<ActivityResumeValidating>) && x.ImplementationType == typeof(EditorialResumeGuard));
        Assert.Equal(TimeSpan.FromMinutes(5), Assert.IsType<EditorialReviewOptions>(Assert.Single(services, x => x.ServiceType == typeof(EditorialReviewOptions)).ImplementationInstance).DecisionLifetime);
        Assert.Single(services, x => x.ServiceType == typeof(EditorialDecisionCodec));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public void Editorial_feature_refuses_an_unbounded_or_empty_decision_lifetime(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpecialistsEditorialFeature { DecisionLifetime = TimeSpan.FromMinutes(minutes) }.ConfigureServices(new ServiceCollection()));

    [Fact]
    public void Editorial_api_contributes_both_permissions_and_derives_the_caller_from_the_principal()
    {
        var services = new ServiceCollection();
        new SpecialistsEditorialApiFeature().ConfigureServices(services);

        Assert.Single(services, x => x.ServiceType == typeof(EditorialRequestCaller) && x.Lifetime == ServiceLifetime.Scoped);
        var contributor = Assert.IsType<EditorialReviewPermissionContributor>(Activator.CreateInstance(
            Assert.Single(services, x => x.ServiceType == typeof(IPermissionContributor) && x.ImplementationType == typeof(EditorialReviewPermissionContributor)).ImplementationType!));
        Assert.Equal(["editorial-reviews:read", "editorial-reviews:decide"], contributor.Contribute().Select(x => x.Key));
    }
}
