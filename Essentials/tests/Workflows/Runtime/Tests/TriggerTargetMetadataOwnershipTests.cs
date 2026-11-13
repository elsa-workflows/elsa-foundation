using Elsa.Events.Core.Contracts;
using Elsa.Events;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class TriggerTargetMetadataOwnershipTests
{
    [Fact]
    public async Task Contribution_lifetime_does_not_erase_supported_profiles_during_initialization()
    {
        var source = new BorrowedProfiles();
        var services = new ServiceCollection();
        new EventsFeature().ConfigureServices(services);
        services.AddSingleton<ITriggerTargetModelSource>(source);
        services.AddScoped<IEventHandler<TriggerTargetModelsDiscovering>, CollectTriggerTargetModels>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var discovery = new TriggerTargetModelsDiscovering();
        await scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>().Publish(discovery, CancellationToken.None);
        Assert.Empty(source.Profiles); // The producer resumed and discarded its borrowed collection.
        var descriptor = Assert.Single(TriggerTargetResolver.ValidateModels(discovery.Models));
        Assert.Equal("direct-v1", Assert.Single(descriptor.Profiles));

    }

    private sealed class BorrowedProfiles : ITriggerTargetModelSource
    {
        internal List<string> Profiles { get; } = ["direct-v1"];
        public IEnumerable<TriggerTargetModelDescriptor> GetModels()
        {
            yield return new("test.model", "Test.Model", 1, Profiles, false);
            Profiles.Clear();
        }
    }
    private sealed class Access : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new("metadata-scope")); }
}
