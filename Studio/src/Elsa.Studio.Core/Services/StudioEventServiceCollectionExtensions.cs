using Elsa.Studio.Core.Events;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.Core.Services;

public static class StudioEventServiceCollectionExtensions
{
    public static IServiceCollection AddStudioEventHandler<TEvent, THandler>(this IServiceCollection services)
        where TEvent : IStudioEvent
        where THandler : class, IStudioEventHandler<TEvent>
    {
        return services.AddTransient<IStudioEventHandler<TEvent>, THandler>();
    }
}

