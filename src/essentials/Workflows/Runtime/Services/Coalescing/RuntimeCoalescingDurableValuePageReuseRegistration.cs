using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Services.Coalescing;

/// <summary>
/// Captures the trusted service-registration evidence for enabling durable-value page reuse.
/// Uncertain or subsequently changed registrations fail closed without changing host startup behavior.
/// </summary>
public sealed class RuntimeCoalescingDurableValuePageReuseRegistration
{
    private readonly IServiceCollection _services;
    private readonly RuntimeOperationalStateStoreBackend? _backend;
    private readonly ServiceDescriptor? _backendDescriptor;
    private readonly ServiceDescriptor? _durableStoreDescriptor;
    private readonly ServiceDescriptor? _accessorDescriptor;
    private readonly ServiceDescriptor? _codecDescriptor;
    private readonly Dictionary<Type, ServiceDescriptor[]> _beforeGroups;
    private Dictionary<Type, ServiceDescriptor[]>? _expectedGroups;
    private bool _captured;

    private RuntimeCoalescingDurableValuePageReuseRegistration(
        IServiceCollection services,
        RuntimeOperationalStateStoreBackend? backend,
        ServiceDescriptor? backendDescriptor,
        ServiceDescriptor? durableStoreDescriptor,
        ServiceDescriptor? accessorDescriptor,
        ServiceDescriptor? codecDescriptor,
        Dictionary<Type, ServiceDescriptor[]> beforeGroups,
        bool eligibleBeforeDecoration)
    {
        _services = services;
        _backend = backend;
        _backendDescriptor = backendDescriptor;
        _durableStoreDescriptor = durableStoreDescriptor;
        _accessorDescriptor = accessorDescriptor;
        _codecDescriptor = codecDescriptor;
        _beforeGroups = beforeGroups;
        EligibleBeforeDecoration = eligibleBeforeDecoration;
    }

    /// <summary>Whether the owned EF backend and stable shared dependencies were eligible before decoration.</summary>
    public bool EligibleBeforeDecoration { get; }

    /// <summary>
    /// Whether the captured registration is still eligible in the finalized service collection.
    /// This is checked when the durable-value wrapper is activated, so later overrides bypass reuse.
    /// </summary>
    public bool IsEligible => EligibleBeforeDecoration && _captured && CurrentGroupsMatch();

    /// <summary>Captures pre-decoration backend ownership and the effective stable dependencies.</summary>
    public static RuntimeCoalescingDurableValuePageReuseRegistration CaptureBeforeDecoration(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        RuntimeOperationalStateStoreBackend? backend = null;
        ServiceDescriptor? backendDescriptor = null;
        ServiceDescriptor? durableDescriptor = null;
        ServiceDescriptor? accessorDescriptor = null;
        ServiceDescriptor? codecDescriptor = null;
        var beforeGroups = new Dictionary<Type, ServiceDescriptor[]>();
        var eligible = false;

        try
        {
            var backendDescriptors = services
                .Where(descriptor => descriptor.ImplementationInstance is RuntimeOperationalStateStoreBackend)
                .ToArray();
            if (backendDescriptors.Length != 1 || backendDescriptors[0].IsKeyedService)
                return new(services, null, null, null, null, null, beforeGroups, false);

            backendDescriptor = backendDescriptors[0];
            backend = (RuntimeOperationalStateStoreBackend)backendDescriptor.ImplementationInstance!;
            var backendServiceGroup = ServiceGroup(services, typeof(RuntimeOperationalStateStoreBackend));
            if (backendDescriptor.ServiceType != typeof(RuntimeOperationalStateStoreBackend) ||
                backendServiceGroup.Length != 1 || !ReferenceEquals(backendServiceGroup[0], backendDescriptor))
                return new(services, backend, backendDescriptor, null, null, null, beforeGroups, false);
            if (backend.Name != RuntimeOperationalStateStoreBackend.EntityFramework)
                return new(services, backend, backendDescriptor, null, null, null, beforeGroups, false);

            backend.EnsureOwnsRegisteredContracts(services);

            var durableDescriptors = ServiceGroup(services, typeof(IDurableValueStateStore));
            if (durableDescriptors.Length != 1 || durableDescriptors[0].IsKeyedService || !backend.Owns(durableDescriptors[0]))
                return new(services, backend, backendDescriptor, null, null, null, beforeGroups, false);
            durableDescriptor = durableDescriptors[0];

            var accessorDescriptors = ServiceGroup(services, typeof(IPersistenceAccessContextAccessor));
            if (accessorDescriptors.Length != 1 || accessorDescriptors[0].IsKeyedService ||
                accessorDescriptors[0].Lifetime is not (ServiceLifetime.Scoped or ServiceLifetime.Singleton))
                return new(services, backend, backendDescriptor, durableDescriptor, null, null, beforeGroups, false);
            accessorDescriptor = accessorDescriptors[0];

            var codecDescriptors = ServiceGroup(services, typeof(IRuntimeRecoveryContinuationCodec));
            if (codecDescriptors.Length != 1 || !IsStableBuiltInCodec(codecDescriptors[0]))
                return new(services, backend, backendDescriptor, durableDescriptor, accessorDescriptor, null, beforeGroups, false);
            codecDescriptor = codecDescriptors[0];

            var ownedDescriptors = services.Where(backend.Owns).ToArray();
            if (ownedDescriptors.Length == 0 ||
                !ownedDescriptors.Any(descriptor => descriptor.ImplementationType == descriptor.ServiceType) ||
                ownedDescriptors.Any(descriptor => ReferenceCount(services, descriptor) != 1))
                return new(services, backend, backendDescriptor, durableDescriptor, accessorDescriptor, codecDescriptor, beforeGroups, false);

            // Concrete store registrations use self-registration descriptors (ServiceType == ImplementationType).
            // Require their entire effective group to be backend-owned, so an unowned concrete override cannot hide
            // behind an otherwise-owned IDurableValueStateStore factory.
            foreach (var concrete in ownedDescriptors.Where(descriptor => descriptor.ImplementationType == descriptor.ServiceType))
            {
                var concreteGroup = ServiceGroup(services, concrete.ServiceType);
                if (concreteGroup.Length != 1 || concreteGroup[0].IsKeyedService || !backend.Owns(concreteGroup[0]))
                    return new(services, backend, backendDescriptor, durableDescriptor, accessorDescriptor, codecDescriptor, beforeGroups, false);
            }

            foreach (var serviceType in ownedDescriptors.Select(descriptor => descriptor.ServiceType).Distinct())
            {
                var group = ServiceGroup(services, serviceType);
                if (group.Any(descriptor => descriptor.IsKeyedService))
                    return new(services, backend, backendDescriptor, durableDescriptor, accessorDescriptor, codecDescriptor, beforeGroups, false);
                beforeGroups.Add(serviceType, group);
            }

            beforeGroups[typeof(IPersistenceAccessContextAccessor)] = accessorDescriptors;
            beforeGroups[typeof(IRuntimeRecoveryContinuationCodec)] = codecDescriptors;
            beforeGroups[typeof(RuntimeOperationalStateStoreBackend)] = ServiceGroup(services, typeof(RuntimeOperationalStateStoreBackend));
            eligible = true;
        }
        catch (InvalidOperationException)
        {
            // Duplicate or no-longer-owned metadata is an ineligible composition, not a new startup failure.
        }

        return new(services, backend, backendDescriptor, durableDescriptor, accessorDescriptor, codecDescriptor, beforeGroups, eligible);
    }

    /// <summary>
    /// Captures the expected post-decoration descriptor groups. Only descriptors owned by the selected EF backend
    /// may be replaced; their original inner descriptor and resulting wrapper are both pinned by reference.
    /// </summary>
    public void CaptureAfterDecoration(
        IEnumerable<ServiceDecoration> decorations,
        ServiceDescriptor markerDescriptor,
        ServiceDescriptor registrationDescriptor)
    {
        ArgumentNullException.ThrowIfNull(decorations);
        ArgumentNullException.ThrowIfNull(markerDescriptor);
        ArgumentNullException.ThrowIfNull(registrationDescriptor);
        _captured = false;
        _expectedGroups = null;

        if (!EligibleBeforeDecoration || _backend is null || _backendDescriptor is null ||
            _durableStoreDescriptor is null || _accessorDescriptor is null || _codecDescriptor is null)
            return;

        try
        {
            var expected = _beforeGroups.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
            var seenOwnedDurable = false;
            foreach (var decoration in decorations)
            {
                if (_backend.Owns(decoration.Original))
                {
                    var expectedInnerServiceType = typeof(CoalescingInner<>).MakeGenericType(decoration.Original.ServiceType);
                    if (decoration.Original.ServiceType != decoration.Wrapper.ServiceType ||
                        decoration.Inner.ServiceType != expectedInnerServiceType ||
                        decoration.Original.Lifetime != decoration.Inner.Lifetime ||
                        decoration.Original.Lifetime != decoration.Wrapper.Lifetime)
                        return;

                    var originalGroup = expected[decoration.Original.ServiceType];
                    if (originalGroup.Count != 1 || !ReferenceEquals(originalGroup[0], decoration.Original))
                        return;

                    originalGroup.Clear();
                    originalGroup.Add(decoration.Wrapper);
                    if (expected.ContainsKey(decoration.Inner.ServiceType))
                        return;
                    expected.Add(decoration.Inner.ServiceType, [decoration.Inner]);
                    if (ReferenceEquals(decoration.Original, _durableStoreDescriptor))
                    {
                        if (seenOwnedDurable || decoration.Original.ServiceType != typeof(IDurableValueStateStore))
                            return;
                        seenOwnedDurable = true;
                    }
                }
            }

            if (!seenOwnedDurable)
                return;

            expected[typeof(RuntimeOperationalStateStoreBackend)] = [_backendDescriptor];
            expected[markerDescriptor.ServiceType] = [markerDescriptor];
            expected[registrationDescriptor.ServiceType] = [registrationDescriptor];
            if (!ContainsExactlyOnce(_services, markerDescriptor) || !ContainsExactlyOnce(_services, registrationDescriptor) ||
                !ReferenceEquals(registrationDescriptor.ImplementationInstance, this) ||
                ServiceGroup(_services, markerDescriptor.ServiceType).Length != 1 ||
                ServiceGroup(_services, registrationDescriptor.ServiceType).Length != 1)
                return;

            _expectedGroups = expected.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
            _captured = CurrentGroupsMatch();
        }
        catch (InvalidOperationException)
        {
            _expectedGroups = null;
            _captured = false;
        }
    }

    private bool CurrentGroupsMatch()
    {
        if (_expectedGroups is null || _backend is null || _backendDescriptor is null ||
            _accessorDescriptor is null || _codecDescriptor is null)
            return false;

        if (!ContainsExactlyOnce(_services, _backendDescriptor) ||
            !ContainsExactlyOnce(_services, _accessorDescriptor) ||
            !ContainsExactlyOnce(_services, _codecDescriptor) ||
            !IsStableBuiltInCodec(_codecDescriptor))
            return false;

        var backendDescriptors = _services
            .Where(descriptor => descriptor.ImplementationInstance is RuntimeOperationalStateStoreBackend)
            .ToArray();
        if (backendDescriptors.Length != 1 || !ReferenceEquals(backendDescriptors[0], _backendDescriptor))
            return false;

        foreach (var (serviceType, expected) in _expectedGroups)
        {
            var current = ServiceGroup(_services, serviceType);
            if (current.Length != expected.Length)
                return false;
            for (var index = 0; index < current.Length; index++)
            {
                if (!ReferenceEquals(current[index], expected[index]) || current[index].IsKeyedService)
                    return false;
            }
        }

        return true;
    }

    private static bool IsStableBuiltInCodec(ServiceDescriptor descriptor) =>
        !descriptor.IsKeyedService && descriptor.Lifetime == ServiceLifetime.Singleton &&
        (descriptor.ImplementationType == typeof(HmacRuntimeRecoveryContinuationCodec) ||
         descriptor.ImplementationInstance is HmacRuntimeRecoveryContinuationCodec);

    private static ServiceDescriptor[] ServiceGroup(IServiceCollection services, Type serviceType) =>
        services.Where(descriptor => descriptor.ServiceType == serviceType).ToArray();

    private static int ReferenceCount(IServiceCollection services, ServiceDescriptor expected) =>
        services.Count(descriptor => ReferenceEquals(descriptor, expected));

    private static bool ContainsExactlyOnce(IServiceCollection services, ServiceDescriptor expected) =>
        ReferenceCount(services, expected) == 1;

    /// <summary>The exact descriptors created when a runtime service is decorated.</summary>
    public readonly record struct ServiceDecoration(
        ServiceDescriptor Original,
        ServiceDescriptor Inner,
        ServiceDescriptor Wrapper);
}
