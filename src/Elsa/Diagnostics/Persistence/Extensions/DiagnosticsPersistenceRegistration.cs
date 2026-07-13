using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.Persistence.Extensions;

/// <summary>Provides one explicit replacement path for a diagnostics store contract.</summary>
public static class DiagnosticsPersistenceRegistration
{
    public static IServiceCollection ReplaceDiagnosticsStore<TContract, TImplementation>(
        this IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);
        services.RemoveAll<TContract>();
        services.RemoveAll<TImplementation>();
        services.AddSingleton<TImplementation>();
        services.AddSingleton<TContract>(provider => provider.GetRequiredService<TImplementation>());
        return services;
    }
}
