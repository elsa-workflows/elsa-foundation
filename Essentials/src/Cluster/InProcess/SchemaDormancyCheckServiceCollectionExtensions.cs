using Elsa.Cluster.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Cluster.InProcess;

public static class SchemaDormancyCheckServiceCollectionExtensions
{
    /// <summary>
    /// Adds the default <see cref="SchemaDormancyCheck"/> unless a check is already composed (spec 182, FR-003), so asking
    /// the check never depends on who composed it. A check registered before this one is the one used; two already
    /// registered are a replacement-contract conflict (framework constitution §2.6.2) and fail here.
    /// </summary>
    public static IServiceCollection TryAddSchemaDormancyCheck(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var existing = services.Where(descriptor => descriptor.ServiceType == typeof(ISchemaDormancyCheck) && !descriptor.IsKeyedService).ToArray();
        if (existing.Length > 1)
            throw new InvalidOperationException(
                $"{nameof(ISchemaDormancyCheck)} is a replacement contract, and {existing.Length} implementations are composed. A container " +
                "has exactly one, and a second one is never resolved by last-write-wins. Remove all but one.");
        if (existing.Length == 1)
            return services;

        services.AddOptions();
        return services.AddSingleton<ISchemaDormancyCheck, SchemaDormancyCheck>();
    }
}
