using Elsa.Studio.Core.Contracts;
using Elsa.Studio.Core.Events;

namespace Elsa.Studio.Api.Services;

internal sealed class CollectStudioRuntimeConfiguration(IEnumerable<IStudioRuntimeConfigurationSource> sources)
    : IStudioEventHandler<OnStudioRuntimeConfigurationCollecting>
{
    public Task Handle(OnStudioRuntimeConfigurationCollecting @event, CancellationToken cancellationToken)
    {
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var entry in source.GetConfiguration(@event.StudioPathBase))
                @event.Configuration.Add(entry);
        }
        return Task.CompletedTask;
    }
}
