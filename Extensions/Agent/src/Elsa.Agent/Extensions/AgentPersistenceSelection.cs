using Elsa.Agent.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Extensions;

/// <summary>
/// Preview: explicit selection of the one session record store and the one audit store (sink, reader and ownership).
/// A selection replaces only the process-local default; a second selection, or a selection over a registration this
/// boundary does not own, is a configuration error.
/// </summary>
public static class AgentPersistenceSelection
{
    private static readonly Type[] AuditFacets = [typeof(IAgentAuditSink), typeof(IAgentAuditReader), typeof(IAgentAuditOwnership)];

    /// <summary>Selects the session record store, replacing only the registered process-local default.</summary>
    /// <exception cref="InvalidOperationException">A store was already selected, or a store is registered outside this boundary.</exception>
    public static IServiceCollection SelectAgentSessionRecordStore(this IServiceCollection services, Func<IServiceProvider, IAgentSessionRecordStore> factory)
        => SelectAgentSessionRecordStore(services, factory, null);

    /// <summary>Selects the record store and its supported effects variation; null declares legacy-only support.</summary>
    /// <remarks>The required type must derive from <see cref="FoundationAgentEffectsFeature"/>. A selected variation may specialize that type.</remarks>
    public static IServiceCollection SelectAgentSessionRecordStore(this IServiceCollection services,
        Func<IServiceProvider, IAgentSessionRecordStore> factory, Type? requiredEffectsFeatureType)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        if (requiredEffectsFeatureType is not null && !typeof(FoundationAgentEffectsFeature).IsAssignableFrom(requiredEffectsFeatureType))
            throw new ArgumentException("The store's supported effects variation must derive from FoundationAgentEffectsFeature.", nameof(requiredEffectsFeatureType));
        Select<RecordStoreSelection>(services, [typeof(IAgentSessionRecordStore)], [ServiceDescriptor.Singleton(factory)],
            descriptors => new(descriptors, true, requiredEffectsFeatureType), "IAgentSessionRecordStore");
        return services;
    }

    /// <summary>
    /// Selects one store as the audit sink, reader and ownership together, replacing only the registered process-local
    /// default facets.
    /// </summary>
    /// <exception cref="InvalidOperationException">An audit store was already selected, or a facet is registered outside this boundary.</exception>
    public static IServiceCollection SelectAgentAuditStore<TStore>(this IServiceCollection services, Func<IServiceProvider, TStore> factory)
        where TStore : class, IAgentAuditSink, IAgentAuditReader, IAgentAuditOwnership
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        var store = ServiceDescriptor.Singleton(factory);
        Select<AuditSelection>(services, AuditFacets,
        [
            store,
            ServiceDescriptor.Singleton<IAgentAuditSink>(provider => provider.GetRequiredService<TStore>()),
            ServiceDescriptor.Singleton<IAgentAuditReader>(provider => provider.GetRequiredService<TStore>()),
            ServiceDescriptor.Singleton<IAgentAuditOwnership>(provider => provider.GetRequiredService<TStore>())
        ], descriptors => new(descriptors, true), "the agent audit store");
        return services;
    }

    /// <summary>Registers the process-local record store only when no store is registered or selected.</summary>
    internal static void TryAddDefaultSessionRecordStore(IServiceCollection services, Func<IServiceProvider, IAgentSessionRecordStore> factory)
    {
        if (services.Any(x => x.ServiceType == typeof(IAgentSessionRecordStore))) return;
        var descriptor = ServiceDescriptor.Singleton(factory);
        services.Add(descriptor);
        services.AddSingleton(new RecordStoreSelection([descriptor], false, typeof(FoundationAgentEffectsFeature)));
    }

    /// <summary>Reads only owned selection metadata; never invokes a store factory or infers support from a store's type.</summary>
    internal static Type? RequiredEffectsFeatureType(IServiceCollection services)
    {
        var markers = services.Where(item => item.ServiceType == typeof(RecordStoreSelection)).ToArray();
        if (markers.Length != 1 || markers[0].IsKeyedService || markers[0].Lifetime != ServiceLifetime.Singleton
            || markers[0].ImplementationInstance is not RecordStoreSelection selection
            || selection.Descriptors.Count != 1)
            throw new InvalidOperationException("Agent effects require record-store metadata owned by SelectAgentSessionRecordStore; an unowned registration cannot declare effects support.");
        var stores = services.Where(item => item.ServiceType == typeof(IAgentSessionRecordStore)).ToArray();
        if (stores.Length != 1 || !ReferenceEquals(stores[0], selection.Descriptors[0]))
            throw new InvalidOperationException("The selected Agent record store was replaced outside SelectAgentSessionRecordStore; its effects support is no longer established.");
        return selection.RequiredEffectsFeatureType;
    }

    /// <summary>Registers each process-local audit facet that is not registered yet; the facets it adds stay replaceable by a selection.</summary>
    internal static void TryAddDefaultAuditStore<TStore>(IServiceCollection services) where TStore : class, IAgentAuditSink, IAgentAuditReader, IAgentAuditOwnership
    {
        if (services.Any(x => x.ServiceType == typeof(AuditSelection) && x.ImplementationInstance is AuditSelection { IsExplicit: true })) return;
        var added = new List<ServiceDescriptor>();
        foreach (var (facet, descriptor) in new (Type, ServiceDescriptor)[]
                 {
                     (typeof(IAgentAuditSink), ServiceDescriptor.Singleton<IAgentAuditSink>(provider => provider.GetRequiredService<TStore>())),
                     (typeof(IAgentAuditReader), ServiceDescriptor.Singleton<IAgentAuditReader>(provider => provider.GetRequiredService<TStore>())),
                     (typeof(IAgentAuditOwnership), ServiceDescriptor.Singleton<IAgentAuditOwnership>(provider => provider.GetRequiredService<TStore>()))
                 })
        {
            if (services.Any(x => x.ServiceType == facet)) continue;
            services.Add(descriptor);
            added.Add(descriptor);
        }
        if (added.Count > 0 && !services.Any(x => x.ServiceType == typeof(AuditSelection)))
            services.AddSingleton(new AuditSelection([.. added], false));
    }

    private static void Select<TSelection>(IServiceCollection services, Type[] contracts, ServiceDescriptor[] replacements,
        Func<ServiceDescriptor[], TSelection> selected, string name) where TSelection : Selection
    {
        var markers = services.Where(x => x.ServiceType == typeof(TSelection)).ToArray();
        var current = markers.Select(x => x.ImplementationInstance).OfType<TSelection>().ToArray();
        var owned = current.SelectMany(x => x.Descriptors).ToHashSet();
        if (current.Any(x => x.IsExplicit) || services.Any(x => contracts.Contains(x.ServiceType) && !owned.Contains(x)))
            throw new InvalidOperationException($"{name} already has a selected or unowned registration. Remove it explicitly before selecting another store.");
        foreach (var descriptor in owned)
            if (!services.Remove(descriptor))
                throw new InvalidOperationException($"The registered default of {name} was removed outside its registration boundary.");
        foreach (var marker in markers) services.Remove(marker);
        foreach (var descriptor in replacements) services.Add(descriptor);
        services.AddSingleton(selected(replacements));
    }

    private abstract record Selection(IReadOnlyList<ServiceDescriptor> Descriptors, bool IsExplicit);

    private sealed record RecordStoreSelection(IReadOnlyList<ServiceDescriptor> Descriptors, bool IsExplicit, Type? RequiredEffectsFeatureType) : Selection(Descriptors, IsExplicit);

    private sealed record AuditSelection(IReadOnlyList<ServiceDescriptor> Descriptors, bool IsExplicit) : Selection(Descriptors, IsExplicit);
}
