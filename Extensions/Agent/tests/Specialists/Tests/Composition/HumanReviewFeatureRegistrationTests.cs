using Elsa.Agent.Specialists.Review;
using Elsa.Agent.Specialists.Review.Api;
using Elsa.Agent.Specialists.Review.Contracts;
using Elsa.Agent.Specialists.Review.Services;
using Elsa.Events.Core.Contracts;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Events;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Composition;

public sealed class HumanReviewFeatureRegistrationTests
{
    [Fact]
    public void Editorial_feature_registers_the_decision_service_guard_and_bounded_lifetime_once()
    {
        var services = new ServiceCollection();
        new SpecialistsReviewFeature { DecisionLifetime = TimeSpan.FromMinutes(5) }.ConfigureServices(services);
        new SpecialistsReviewFeature().ConfigureServices(services);

        var service = Assert.Single(services, x => x.ServiceType == typeof(IHumanReviews));
        Assert.Equal(ServiceLifetime.Scoped, service.Lifetime);
        Assert.Equal(typeof(HumanReviewService), service.ImplementationType);
        Assert.Single(services, x => x.ServiceType == typeof(IEventHandler<ActivityResumeValidating>) && x.ImplementationType == typeof(HumanReviewResumeGuard));
        Assert.Equal(TimeSpan.FromMinutes(5), Assert.IsType<HumanReviewOptions>(Assert.Single(services, x => x.ServiceType == typeof(HumanReviewOptions)).ImplementationInstance).DecisionLifetime);
        Assert.Single(services, x => x.ServiceType == typeof(ReviewDecisionCodec));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1441)]
    public void Editorial_feature_refuses_an_unbounded_or_empty_decision_lifetime(int minutes) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new SpecialistsReviewFeature { DecisionLifetime = TimeSpan.FromMinutes(minutes) }.ConfigureServices(new ServiceCollection()));

    [Fact]
    public void Editorial_api_contributes_both_permissions_and_derives_the_caller_from_the_principal()
    {
        var services = new ServiceCollection();
        new SpecialistsReviewApiFeature().ConfigureServices(services);

        Assert.Single(services, x => x.ServiceType == typeof(ReviewRequestCaller) && x.Lifetime == ServiceLifetime.Scoped);
        var contributor = Assert.IsType<HumanReviewPermissionContributor>(Activator.CreateInstance(
            Assert.Single(services, x => x.ServiceType == typeof(IPermissionContributor) && x.ImplementationType == typeof(HumanReviewPermissionContributor)).ImplementationType!));
        Assert.Equal(["agent-reviews:read", "agent-reviews:decide"], contributor.Contribute().Select(x => x.Key));
    }
}
