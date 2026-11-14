using System.Text.Json;
using Elsa.Workflows.Runtime.Api.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Api.Tests;

public sealed partial class StandaloneTriggerManagementTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Feature_selects_authoring_or_protected_committer_from_the_actual_physical_participant(bool physical)
    {
        var fixture = new Fixture(); IServiceCollection services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<IStandaloneTriggerStore>(fixture.Store);
        var participant = new PhysicalParticipant(fixture.Store);
        if (physical) services.AddSingleton<ITriggerSourceResourceCommitStore>(participant);
        var feature = new StandaloneTriggersApiFeature(); feature.ConfigureServices(services); feature.PostConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        var committer = scope.ServiceProvider.GetRequiredService<ITriggerManagementCommitter>();
        Assert.Equal(physical, committer.SupportsActivatedResources); Assert.False(committer.CanActivateType("event", 1));
        var row = fixture.Store.Resource.Trigger;
        await committer.CommitAsync(fixture.Access.Scope, new("feature-paused", "feature-paused", "operator", 1, row, []));
        Assert.Equal(physical ? 1 : 0, participant.Calls); Assert.Equal(1, fixture.Store.Writes);
        Assert.Empty(scope.ServiceProvider.GetServices<StandaloneTriggerSourceCapability>());
    }

    [Fact]
    public void Feature_preserves_an_explicit_committer_replacement()
    {
        var fixture = new Fixture(); IServiceCollection services = new ServiceCollection(); services.AddLogging();
        services.AddSingleton<ITriggerManagementCommitter>(fixture.Committer);
        services.AddSingleton<ITriggerSourceResourceCommitStore>(new PhysicalParticipant(fixture.Store));
        var feature = new StandaloneTriggersApiFeature(); feature.ConfigureServices(services); feature.PostConfigureServices(services);
        using var provider = services.BuildServiceProvider(); Assert.Same(fixture.Committer, provider.GetRequiredService<ITriggerManagementCommitter>());
    }

    [Theory]
    [InlineData("store")] [InlineData("committer")] [InlineData("types")] [InlineData("targets")] [InlineData("mapper")]
    public void Feature_refuses_duplicate_single_owner_replacements(string contract)
    {
        var fixture = new Fixture(); IServiceCollection services = new ServiceCollection(); var feature = new StandaloneTriggersApiFeature();
        feature.ConfigureServices(services);
        var type = contract switch { "store" => typeof(IStandaloneTriggerStore), "committer" => typeof(ITriggerManagementCommitter),
            "types" => typeof(IStandaloneTriggerTypeCatalog), "targets" => typeof(IStandaloneTriggerTargetDiscovery), _ => typeof(ITriggerInputMapper) };
        services.Add(ServiceDescriptor.Singleton(type, _ => throw new InvalidOperationException("Must not resolve duplicate owners.")));
        if (services.Count(item => item.ServiceType == type) == 1)
            services.Add(ServiceDescriptor.Singleton(type, _ => throw new InvalidOperationException("Must not resolve duplicate owners.")));
        Assert.Throws<InvalidOperationException>(() => feature.PostConfigureServices(services));
    }

    [Theory]
    [InlineData("\"1\"")] [InlineData("null")] [InlineData("{}")]
    [InlineData("[]")] [InlineData("1.5")] [InlineData("9223372036854775808")]
    public async Task Malformed_revision_kinds_are_validation_failures_before_any_write(string revision)
    {
        var fixture = new Fixture();
        var request = Element("{\"expectedRevision\":" + revision + ",\"command\":{\"kind\":\"PauseTrigger\"}}");
        var failure = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Management.PreviewAsync("trigger-a", request, default).AsTask());
        Assert.Equal("validation", failure.Code); Assert.Equal(0, fixture.Store.Writes);
    }

    private sealed class PhysicalParticipant(IStandaloneTriggerStore resources) : ITriggerSourceResourceCommitStore
    {
        internal int Calls;
        public bool SupportsActivatedResources => true;
        public ValueTask<TriggerMutationReceipt> CommitAsync(TriggerScope scope, TriggerStoreWrite write,
            IReadOnlyList<RuntimeIngressReservationDescriptor> reservations, CancellationToken cancellationToken = default)
        { Calls++; return resources.WriteAsync(scope, write, cancellationToken); }
    }
}
