using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Describes a replacement contract's composed registrations for a startup composition check.</summary>
internal static class ReplacementContractRegistrations
{
    /// <summary>
    /// Every non-keyed registration of <typeparamref name="TContract"/> in <paramref name="services"/>, in registration
    /// order: its implementation type, the type of a registered instance, or "a factory registration".
    /// </summary>
    public static IReadOnlyList<string> Describe<TContract>(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(TContract) && !descriptor.IsKeyedService)
            .Select(Describe)
            .ToArray();

    private static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName is { } implementationType
            ? $"'{implementationType}'"
            : descriptor.ImplementationInstance?.GetType().FullName is { } instanceType
                ? $"an instance of '{instanceType}'"
                : "a factory registration";
}
