using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Core.Iam;

/// <summary>
/// Composition ownership marker for the application and credential replacement contracts.
/// The captured descriptors let later persistence features distinguish an owned backend from
/// an uncoordinated host registration without exposing persistence implementation types.
/// </summary>
public sealed class IdentityApplicationCredentialStoreBackend
{
    private readonly ServiceDescriptor _applicationDescriptor;
    private readonly ServiceDescriptor _revisionAwareApplicationDescriptor;
    private readonly ServiceDescriptor _credentialDescriptor;
    private readonly ServiceDescriptor _revisionAwareCredentialDescriptor;

    public IdentityApplicationCredentialStoreBackend(string name, IServiceCollection services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(services);

        Name = name;
        _applicationDescriptor = GetSingleDescriptor<IApplicationStore>(services);
        _revisionAwareApplicationDescriptor = GetSingleDescriptor<IRevisionAwareApplicationStore>(services);
        _credentialDescriptor = GetSingleDescriptor<ICredentialStore>(services);
        _revisionAwareCredentialDescriptor = GetSingleDescriptor<IRevisionAwareCredentialStore>(services);
    }

    public string Name { get; }

    public static void EnsureCompatible(string? existing, string incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming);
        if (existing is not null && !string.Equals(existing, incoming, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Identity application and credential stores are already bound to '{existing}' and cannot also bind '{incoming}'. " +
                "Enable only one application/credential persistence feature.");
    }

    public static bool HasAnyRegisteredContract(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.Any(descriptor => descriptor.ServiceType == typeof(IApplicationStore) ||
                                          descriptor.ServiceType == typeof(IRevisionAwareApplicationStore) ||
                                          descriptor.ServiceType == typeof(ICredentialStore) ||
                                          descriptor.ServiceType == typeof(IRevisionAwareCredentialStore));
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        EnsureOwns<IApplicationStore>(services, _applicationDescriptor);
        EnsureOwns<IRevisionAwareApplicationStore>(services, _revisionAwareApplicationDescriptor);
        EnsureOwns<ICredentialStore>(services, _credentialDescriptor);
        EnsureOwns<IRevisionAwareCredentialStore>(services, _revisionAwareCredentialDescriptor);
    }

    private ServiceDescriptor GetSingleDescriptor<TContract>(IServiceCollection services)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(TContract)).ToArray();
        if (descriptors.Length != 1)
            throw OwnershipFailure(typeof(TContract), descriptors.Length);
        return descriptors[0];
    }

    private void EnsureOwns<TContract>(IServiceCollection services, ServiceDescriptor ownedDescriptor)
    {
        var descriptors = services.Where(descriptor => descriptor.ServiceType == typeof(TContract)).ToArray();
        if (descriptors.Length != 1 || !ReferenceEquals(descriptors[0], ownedDescriptor))
            throw OwnershipFailure(typeof(TContract), descriptors.Length);
    }

    private InvalidOperationException OwnershipFailure(Type contractType, int descriptorCount) =>
        new(
            $"Identity application/credential backend '{Name}' no longer exclusively owns replacement contract " +
            $"'{contractType.FullName}'. Descriptors: {descriptorCount}. Remove conflicting host registrations or select only the intended backend.");
}
