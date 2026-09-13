using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

internal static class BookmarkStateEfContextRegistration
{
    public static void EnsureCompatible(
        IServiceCollection services,
        string provider,
        string? connectionString,
        string? connectionName)
    {
        EnsureCompatible(
            services.Select(x => x.ImplementationInstance)
                .OfType<RuntimeBookmarksEntityFrameworkCoreOptions>()
                .SingleOrDefault(),
            provider,
            connectionString,
            connectionName,
            "Runtime bookmarks");
        EnsureCompatible(
            services.Select(x => x.ImplementationInstance)
                .OfType<RuntimeArtifactsEntityFrameworkCoreOptions>()
                .SingleOrDefault(),
            provider,
            connectionString,
            connectionName,
            "Runtime artifacts");
    }

    private static void EnsureCompatible<TOptions>(
        TOptions? existing,
        string provider,
        string? connectionString,
        string? connectionName,
        string owner)
        where TOptions : class
    {
        if (existing is null)
            return;

        var (existingProvider, existingConnectionString, existingConnectionName) = existing switch
        {
            RuntimeBookmarksEntityFrameworkCoreOptions options =>
                (options.Provider, options.ConnectionString, options.ConnectionName),
            RuntimeArtifactsEntityFrameworkCoreOptions options =>
                (options.Provider, options.ConnectionString, options.ConnectionName),
            _ => throw new InvalidOperationException("Unknown Runtime EF context options.")
        };

        if (!string.Equals(EfRelationalProviderBinding.Normalize(existingProvider), provider, StringComparison.Ordinal) ||
            !string.Equals(existingConnectionString, connectionString, StringComparison.Ordinal) ||
            !string.Equals(existingConnectionName, connectionName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Combined Runtime bookmarks and artifact EF persistence requires compatible provider options; {owner} is already registered differently.");
        }
    }
}
