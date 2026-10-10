using CShells.Lifecycle;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>The service collection the runtime was composed in, read again at shell activation.</summary>
public sealed record RuntimeSecretMaskComposition(IServiceCollection Services);

/// <summary>
/// Fails shell activation when the host composes more than one <see cref="IRuntimeSecretMask"/>, naming every
/// registration, whatever order they were registered in. The runtime registers a default with <c>TryAdd</c>, so a host
/// that replaces it with <c>services.Replace(...)</c>, or registers its own before the runtime does, composes one.
/// </summary>
/// <remarks>
/// Framework constitution §2.6.2 requires a replacement-contract conflict to be prevented at registration time or
/// detected at startup. The activator registers resolved values with the mask and the work handlers mask with it, each
/// resolving the one registration; a second registration would make one of them win by registration order. This check
/// reads the composed service collection once every feature has registered, and counts registrations rather than
/// resolving them, as <see cref="RuntimeSecretResolverCompositionValidator"/> does for the secret resolver.
/// </remarks>
public sealed class RuntimeSecretMaskCompositionValidator(RuntimeSecretMaskComposition composition) : IShellInitializer
{
    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        var registrations = ReplacementContractRegistrations.Describe<IRuntimeSecretMask>(composition.Services);
        if (registrations.Count > 1)
            throw new MultipleRuntimeSecretMasksException(registrations);

        return Task.CompletedTask;
    }

    /// <summary>Registers the check once per service collection, in the Prepare phase, before any startup work.</summary>
    internal static void Register(IServiceCollection services)
    {
        if (services.Any(descriptor => descriptor.ServiceType == typeof(RuntimeSecretMaskComposition)))
            return;

        services.AddSingleton(new RuntimeSecretMaskComposition(services));
        services.AddShellInitializer<RuntimeSecretMaskCompositionValidator>(LifecyclePhase.Prepare, 0);
    }
}
