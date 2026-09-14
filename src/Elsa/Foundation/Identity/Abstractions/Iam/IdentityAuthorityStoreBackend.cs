using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Abstractions.Iam;

/// <summary>
/// Composition ownership marker for the tenant-local Identity authority replacement contracts.
/// The captured descriptors let later persistence features distinguish an owned backend from an
/// uncoordinated host registration without exposing persistence implementation types.
/// </summary>
public sealed class IdentityAuthorityStoreBackend
{
    private static readonly Type[] ContractTypes =
    [
        typeof(IUserStore),
        typeof(IRevisionAwareUserStore),
        typeof(IRoleStore),
        typeof(IRevisionAwareRoleStore),
        typeof(IPagedRoleStore),
        typeof(IClaimMappingStore),
        typeof(IRevisionAwareClaimMappingStore),
        typeof(IPagedClaimMappingStore),
        typeof(IExternalIdentityStore),
        typeof(IRevisionAwareExternalIdentityStore),
        typeof(IPagedExternalIdentityStore),
        typeof(ITenantMembershipStore),
        typeof(IRevisionAwareTenantMembershipStore)
    ];

    private readonly IReadOnlyDictionary<Type, ServiceDescriptor> _descriptors;

    public IdentityAuthorityStoreBackend(string name, IServiceCollection services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(services);

        Name = name;
        _descriptors = ContractTypes.ToDictionary(type => type, type => GetSingleDescriptor(services, type));
    }

    public string Name { get; }

    public static void EnsureCompatible(string? existing, string incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming);
        if (existing is not null && !string.Equals(existing, incoming, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Identity authority stores are already bound to '{existing}' and cannot also bind '{incoming}'. " +
                "Enable only one Identity authority persistence feature.");
        }
    }

    public static bool HasAnyRegisteredContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Any(descriptor => ContractTypes.Contains(descriptor.ServiceType));
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        foreach (var pair in _descriptors)
        {
            var descriptors = services.Where(descriptor => descriptor.ServiceType == pair.Key).ToArray();
            if (descriptors.Length != 1 || !ReferenceEquals(descriptors[0], pair.Value))
                throw OwnershipFailure(pair.Key, descriptors.Length);
        }
    }

    private ServiceDescriptor GetSingleDescriptor(IServiceCollection services, Type contractType)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == contractType).ToArray();
        if (descriptors.Length != 1)
            throw OwnershipFailure(contractType, descriptors.Length);
        return descriptors[0];
    }

    private InvalidOperationException OwnershipFailure(Type contractType, int descriptorCount) =>
        new(
            $"Identity authority backend '{Name}' no longer exclusively owns replacement contract " +
            $"'{contractType.FullName}'. Descriptors: {descriptorCount}. Remove conflicting host registrations or select only the intended backend.");
}
