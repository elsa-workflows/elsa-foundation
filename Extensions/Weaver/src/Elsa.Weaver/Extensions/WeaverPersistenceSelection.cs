using Elsa.Weaver.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Weaver.Extensions;

/// <summary>
/// Preview: explicit selection of the one session record store and the one audit store (sink, reader and ownership).
/// A selection replaces only the process-local default; a second selection, or a selection over a registration this
/// boundary does not own, is a configuration error.
/// </summary>
public static class WeaverPersistenceSelection
{
    private static readonly Type[] AuditFacets = [typeof(IWeaverAuditSink), typeof(IWeaverAuditReader), typeof(IWeaverAuditOwnership)];

    /// <summary>Selects the session record store, replacing only the registered process-local default.</summary>
    /// <exception cref="InvalidOperationException">A store was already selected, or a store is registered outside this boundary.</exception>
    public static IServiceCollection SelectWeaverSessionRecordStore(this IServiceCollection services, Func<IServiceProvider, IWeaverSessionRecordStore> factory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        Select<RecordStoreSelection>(services, [typeof(IWeaverSessionRecordStore)], [ServiceDescriptor.Singleton(factory)],
            descriptors => new(descriptors, true), "IWeaverSessionRecordStore");
        return services;
    }

    /// <summary>
    /// Selects one store as the audit sink, reader and ownership together, replacing only the registered process-local
    /// default facets.
    /// </summary>
    /// <exception cref="InvalidOperationException">An audit store was already selected, or a facet is registered outside this boundary.</exception>
    public static IServiceCollection SelectWeaverAuditStore<TStore>(this IServiceCollection services, Func<IServiceProvider, TStore> factory)
        where TStore : class, IWeaverAuditSink, IWeaverAuditReader, IWeaverAuditOwnership
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(factory);
        var store = ServiceDescriptor.Singleton(factory);
        Select<AuditSelection>(services, AuditFacets,
        [
            store,
            ServiceDescriptor.Singleton<IWeaverAuditSink>(provider => provider.GetRequiredService<TStore>()),
            ServiceDescriptor.Singleton<IWeaverAuditReader>(provider => provider.GetRequiredService<TStore>()),
            ServiceDescriptor.Singleton<IWeaverAuditOwnership>(provider => provider.GetRequiredService<TStore>())
        ], descriptors => new(descriptors, true), "the agent audit store");
        return services;
    }

    /// <summary>Registers the process-local record store only when no store is registered or selected.</summary>
    internal static void TryAddDefaultSessionRecordStore(IServiceCollection services, Func<IServiceProvider, IWeaverSessionRecordStore> factory)
    {
        if (services.Any(x => x.ServiceType == typeof(IWeaverSessionRecordStore))) return;
        var descriptor = ServiceDescriptor.Singleton(factory);
        services.Add(descriptor);
        services.AddSingleton(new RecordStoreSelection([descriptor], false));
    }

    /// <summary>Registers each process-local audit facet that is not registered yet; the facets it adds stay replaceable by a selection.</summary>
    internal static void TryAddDefaultAuditStore<TStore>(IServiceCollection services) where TStore : class, IWeaverAuditSink, IWeaverAuditReader, IWeaverAuditOwnership
    {
        if (services.Any(x => x.ServiceType == typeof(AuditSelection) && x.ImplementationInstance is AuditSelection { IsExplicit: true })) return;
        var added = new List<ServiceDescriptor>();
        foreach (var (facet, descriptor) in new (Type, ServiceDescriptor)[]
                 {
                     (typeof(IWeaverAuditSink), ServiceDescriptor.Singleton<IWeaverAuditSink>(provider => provider.GetRequiredService<TStore>())),
                     (typeof(IWeaverAuditReader), ServiceDescriptor.Singleton<IWeaverAuditReader>(provider => provider.GetRequiredService<TStore>())),
                     (typeof(IWeaverAuditOwnership), ServiceDescriptor.Singleton<IWeaverAuditOwnership>(provider => provider.GetRequiredService<TStore>()))
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

    private sealed record RecordStoreSelection(IReadOnlyList<ServiceDescriptor> Descriptors, bool IsExplicit) : Selection(Descriptors, IsExplicit);

    private sealed record AuditSelection(IReadOnlyList<ServiceDescriptor> Descriptors, bool IsExplicit) : Selection(Descriptors, IsExplicit);
}
