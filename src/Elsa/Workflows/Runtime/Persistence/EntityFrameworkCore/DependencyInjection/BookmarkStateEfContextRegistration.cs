using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

internal static class BookmarkStateEfContextRegistration
{
    public static void EnsureContextIsAvailable<TContext>(
        IServiceCollection services,
        bool existingContextIsOwnedBySibling,
        string owner)
        where TContext : BookmarkStateDbContext
    {
        var hasContextRegistration = services.Any(descriptor =>
            descriptor.ServiceType == typeof(TContext) ||
            descriptor.ServiceType == typeof(DbContextOptions<TContext>) ||
            descriptor.ServiceType == typeof(BookmarkStateDbContext));
        if (hasContextRegistration && !existingContextIsOwnedBySibling)
            throw new InvalidOperationException(
                $"A {typeof(TContext).Name} registration already exists; {owner} EF persistence refuses to reuse or replace an unowned context.");
    }

    public static bool IsOwnedByArtifacts(IServiceCollection services) =>
        services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RuntimeArtifactsEntityFrameworkCoreOptions>()
            .Any() &&
        services.Any(descriptor =>
            descriptor.ServiceType == typeof(EfWorkflowExecutableStore) &&
            descriptor.ImplementationType == typeof(EfWorkflowExecutableStore));

    public static bool IsOwnedByBookmarks(IServiceCollection services) =>
        services.Select(descriptor => descriptor.ImplementationInstance)
            .OfType<RuntimeBookmarksEntityFrameworkCoreOptions>()
            .Any() &&
        BookmarkStateStoreBackend.Find(services)?.Name == BookmarkStateStoreBackend.EntityFramework;

    public static void EnsureCompatible(
        IServiceCollection services,
        string provider,
        string? connectionString,
        string? connectionName,
        string defaultConnectionString)
    {
        EnsureCompatible(
            services.Select(x => x.ImplementationInstance)
                .OfType<RuntimeBookmarksEntityFrameworkCoreOptions>()
                .SingleOrDefault(),
            provider,
            connectionString,
            connectionName,
            defaultConnectionString,
            "Runtime bookmarks");
        EnsureCompatible(
            services.Select(x => x.ImplementationInstance)
                .OfType<RuntimeArtifactsEntityFrameworkCoreOptions>()
                .SingleOrDefault(),
            provider,
            connectionString,
            connectionName,
            defaultConnectionString,
            "Runtime artifacts");
    }

    private static void EnsureCompatible<TOptions>(
        TOptions? existing,
        string provider,
        string? connectionString,
        string? connectionName,
        string defaultConnectionString,
        string owner)
        where TOptions : class
    {
        if (existing is null)
            return;

        var (existingProvider, existingConnectionString, existingConnectionName, existingDefaultConnectionString) = existing switch
        {
            RuntimeBookmarksEntityFrameworkCoreOptions options =>
                (options.Provider, options.ConnectionString, options.ConnectionName, BookmarkStateEfModule.DefaultSqliteConnectionString),
            RuntimeArtifactsEntityFrameworkCoreOptions options =>
                (options.Provider, options.ConnectionString, options.ConnectionName, RuntimeArtifactEfModule.DefaultSqliteConnectionString),
            _ => throw new InvalidOperationException("Unknown Runtime EF context options.")
        };

        var existingIdentity = EffectiveIdentity(
            existingProvider,
            existingConnectionString,
            existingConnectionName,
            existingDefaultConnectionString);
        var currentIdentity = EffectiveIdentity(provider, connectionString, connectionName, defaultConnectionString);
        if (!string.Equals(existingIdentity, currentIdentity, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Combined Runtime bookmarks and artifact EF persistence requires compatible provider options; {owner} is already registered differently.");
        }
    }

    private static string EffectiveIdentity(
        string provider,
        string? connectionString,
        string? connectionName,
        string defaultConnectionString)
    {
        var normalizedProvider = EfRelationalProviderBinding.Normalize(provider);
        if (!string.IsNullOrWhiteSpace(connectionString))
            return $"{normalizedProvider}:connection:{connectionString}";
        if (!string.IsNullOrWhiteSpace(connectionName))
            return $"{normalizedProvider}:name:{connectionName}";
        return $"{normalizedProvider}:default:{defaultConnectionString}";
    }
}
