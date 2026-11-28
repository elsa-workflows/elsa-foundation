using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Api.Services;

/// <summary>The classic feature supplies its exact default registrations to the hybrid API.</summary>
internal sealed class LegacyRuntimeApiReaderOwnership(params LegacyRuntimeApiReaderRegistration[] readers)
{
    internal bool Owns(ServiceDescriptor descriptor) =>
        readers.Any(reader => reader.Owns(descriptor));
}

internal sealed record LegacyRuntimeApiReaderRegistration(Type Implementation, ServiceDescriptor Default)
{
    internal bool Owns(ServiceDescriptor descriptor) =>
        Default.ServiceType == descriptor.ServiceType &&
        (descriptor.ImplementationType == Implementation || ReferenceEquals(descriptor, Default));

    internal static LegacyRuntimeApiReaderRegistration Add<TService, TImplementation>(
        IServiceCollection services, Func<IServiceProvider, TService> factory)
        where TService : class
        where TImplementation : class, TService
    {
        var descriptor = ServiceDescriptor.Scoped(factory);
        services.TryAdd(descriptor);
        return new(typeof(TImplementation), descriptor);
    }
}
