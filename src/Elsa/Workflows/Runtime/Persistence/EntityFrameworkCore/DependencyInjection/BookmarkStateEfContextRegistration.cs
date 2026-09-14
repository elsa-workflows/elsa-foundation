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
        Func<ServiceDescriptor, bool>? ownsExistingContext,
        string owner)
        where TContext : BookmarkStateDbContext
    {
        var contextRegistrations = services.Where(IsContextRegistration<TContext>).ToArray();
        if (contextRegistrations.Length > 0 &&
            (ownsExistingContext is null || contextRegistrations.Any(descriptor => !ownsExistingContext(descriptor))))
            throw new InvalidOperationException(
                $"A {typeof(TContext).Name} registration already exists; {owner} EF persistence refuses to reuse or replace an unowned context.");
    }

    public static void EnsureContextIsAvailable(
        IServiceCollection services,
        string provider,
        string owner,
        params Func<ServiceDescriptor, bool>?[] owners)
    {
        var contextRegistrations = ContextRegistrations(services, provider);
        if (contextRegistrations.Count > 0 &&
            (owners.Length == 0 || contextRegistrations.Any(descriptor => !owners.Any(predicate => predicate?.Invoke(descriptor) == true))))
            throw new InvalidOperationException(
                $"A {contextRegistrations.First().ServiceType.Name} registration already exists; {owner} EF persistence refuses to reuse or replace an unowned context.");
    }

    public static void EnsureContextIsAvailable(
        IServiceCollection services,
        string provider,
        Func<ServiceDescriptor, bool>? ownsExistingContext,
        string owner)
    {
        switch (EfRelationalProviderBinding.Normalize(provider))
        {
            case "sqlite": EnsureContextIsAvailable<BookmarkStateSqliteDbContext>(services, ownsExistingContext, owner); break;
            case "sqlserver": EnsureContextIsAvailable<BookmarkStateSqlServerDbContext>(services, ownsExistingContext, owner); break;
            case "postgresql": EnsureContextIsAvailable<BookmarkStatePostgreSqlDbContext>(services, ownsExistingContext, owner); break;
            case "mysql": EnsureContextIsAvailable<BookmarkStateMySqlDbContext>(services, ownsExistingContext, owner); break;
            default: throw new ArgumentException($"Unknown Runtime EF provider '{provider}'.", nameof(provider));
        }
    }

    public static IReadOnlyCollection<ServiceDescriptor> ContextRegistrations(IServiceCollection services, string provider) =>
        EfRelationalProviderBinding.Normalize(provider) switch
        {
            "sqlite" => services.Where(IsContextRegistration<BookmarkStateSqliteDbContext>).ToArray(),
            "sqlserver" => services.Where(IsContextRegistration<BookmarkStateSqlServerDbContext>).ToArray(),
            "postgresql" => services.Where(IsContextRegistration<BookmarkStatePostgreSqlDbContext>).ToArray(),
            "mysql" => services.Where(IsContextRegistration<BookmarkStateMySqlDbContext>).ToArray(),
            _ => throw new ArgumentException($"Unknown Runtime EF provider '{provider}'.", nameof(provider))
        };

    private static bool IsContextRegistration<TContext>(ServiceDescriptor descriptor)
        where TContext : BookmarkStateDbContext
    {
        if (descriptor.ServiceType == typeof(TContext) ||
            descriptor.ServiceType == typeof(DbContextOptions<TContext>) ||
            descriptor.ServiceType == typeof(BookmarkStateDbContext))
            return true;

        return IsContextType(descriptor.ImplementationType) ||
               IsContextType(descriptor.ImplementationInstance?.GetType()) ||
               IsContextType(descriptor.ImplementationFactory?.Method.ReturnType);
    }

    private static bool IsContextType(Type? type) =>
        type is not null && typeof(BookmarkStateDbContext).IsAssignableFrom(type);

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
