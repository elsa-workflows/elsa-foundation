using CShells.Lifecycle;
using Elsa.Workflows.Design.Validations.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Design.Validations.Internal;

/// <summary>The service collection the validations feature was composed in, read again at shell activation.</summary>
internal sealed record CredentialLiteralValidatorComposition(IServiceCollection Services);

/// <summary>
/// Fails shell activation when the host composes more than one <see cref="ICredentialLiteralValidator"/>, naming every
/// registration, whatever order they were registered in.
/// </summary>
/// <remarks>
/// Framework constitution §2.6.2 requires a replacement-contract conflict to be prevented at registration time or
/// detected at startup. A registration sees only what was registered before it, so this check reads the composed
/// service collection once every feature has registered. It counts registrations rather than resolving them.
/// </remarks>
internal sealed class CredentialLiteralValidatorCompositionCheck(CredentialLiteralValidatorComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var registrations = composition.Services
            .Where(descriptor => descriptor.ServiceType == typeof(ICredentialLiteralValidator) && !descriptor.IsKeyedService)
            .ToArray();
        if (registrations.Length > 1)
            throw new InvalidOperationException(
                $"'{typeof(ICredentialLiteralValidator).FullName}' is a replacement contract with exactly one implementation per host, " +
                $"but {registrations.Length} are registered: {string.Join(", ", registrations.Select(Describe))}. Register only one.");

        return Task.CompletedTask;
    }

    /// <summary>Registers the check once per service collection, in the Prepare phase, before any startup work.</summary>
    internal static void Register(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(CredentialLiteralValidatorComposition)))
            return;

        services.AddSingleton(new CredentialLiteralValidatorComposition(services));
        services.AddShellInitializer<CredentialLiteralValidatorCompositionCheck>(LifecyclePhase.Prepare, 0);
    }

    private static string Describe(ServiceDescriptor descriptor) =>
        descriptor.ImplementationType?.FullName is { } implementationType
            ? $"'{implementationType}'"
            : descriptor.ImplementationInstance?.GetType().FullName is { } instanceType
                ? $"an instance of '{instanceType}'"
                : "a factory registration";
}
