using Elsa.Agent.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Core.DependencyInjection;

/// <summary>Explicit selection of the one journal backend; a second selection is a configuration error.</summary>
public static class AgentTurnStoreRegistration
{
    /// <summary>Registers a development default only when no journal is selected.</summary>
    public static IServiceCollection TryAddAgentTurnStore<TStore>(this IServiceCollection services, ServiceLifetime lifetime)
        where TStore : class, IAgentTurnStore
    {
        ArgumentNullException.ThrowIfNull(services);
        if (!Enum.IsDefined(lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));
        if (services.Any(x => x.ServiceType == typeof(IAgentTurnStore))) return services;
        var descriptor = ServiceDescriptor.Describe(typeof(IAgentTurnStore), typeof(TStore), lifetime);
        services.Add(descriptor);
        services.AddSingleton(new Selection(descriptor, false));
        return services;
    }

    /// <summary>Selects a scoped backend, replacing only the registered development default.</summary>
    public static IServiceCollection ReplaceAgentTurnStore(this IServiceCollection services, Func<IServiceProvider, IAgentTurnStore> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        var selections = services.Where(x => x.ServiceType == typeof(Selection)).ToArray();
        var selection = selections.SingleOrDefault()?.ImplementationInstance as Selection;
        if (selection is { IsExplicit: true } || services.Any(x => x.ServiceType == typeof(IAgentTurnStore) && !ReferenceEquals(x, selection?.Descriptor)))
            throw new InvalidOperationException("IAgentTurnStore already has a selected or unowned backend. Remove it explicitly before selecting another journal.");
        if (selection is not null)
        {
            if (!services.Remove(selection.Descriptor)) throw new InvalidOperationException("The selected agent journal descriptor was removed outside its registration boundary.");
            services.Remove(selections.Single());
        }
        var descriptor = ServiceDescriptor.Scoped(typeof(IAgentTurnStore), factory);
        services.Add(descriptor);
        services.AddSingleton(new Selection(descriptor, true));
        return services;
    }

    private sealed record Selection(ServiceDescriptor Descriptor, bool IsExplicit);
}
