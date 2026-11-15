using Elsa.Events;
using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal sealed class NativeTargetMetadataFixture : IDisposable
{
    private readonly ServiceProvider provider;
    private readonly IServiceScope scope;
    internal NativeTargetMetadataFixture()
    {
        var services = new ServiceCollection();
        new EventsFeature().ConfigureServices(services);
        services.AddSingleton<ITriggerTargetModelSource, NativeTriggerTargetModelSource>();
        services.AddScoped<IEventHandler<TriggerTargetModelsDiscovering>, CollectTriggerTargetModels>();
        provider = services.BuildServiceProvider(); scope = provider.CreateScope();
    }
    internal IInlineEventPublisher Publisher => scope.ServiceProvider.GetRequiredService<IInlineEventPublisher>();
    public void Dispose() { scope.Dispose(); provider.Dispose(); }
}
