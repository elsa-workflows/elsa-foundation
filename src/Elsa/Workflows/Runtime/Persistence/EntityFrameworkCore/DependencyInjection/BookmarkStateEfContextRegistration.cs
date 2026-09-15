using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

internal static class BookmarkStateEfContextRegistration
{
    public static void EnsureRecoveryContinuationSigningKeyCompatible(
        IServiceCollection services,
        string? requestedKey,
        string owner)
    {
        var configuredKeys = services
            .Select(descriptor => descriptor.ImplementationInstance)
            .Select(instance => instance switch
            {
                RuntimeActivityExecutionEntityFrameworkCoreOptions options => options.RecoveryContinuationSigningKey,
                RuntimeWorkflowExecutionEntityFrameworkCoreOptions options => options.RecoveryContinuationSigningKey,
                RuntimeWorkflowAlterationEntityFrameworkCoreOptions options => options.RecoveryContinuationSigningKey,
                RuntimeWorkflowTestScopeEntityFrameworkCoreOptions options => options.RecoveryContinuationSigningKey,
                RuntimeOperationalStateEntityFrameworkCoreOptions options => options.RecoveryContinuationSigningKey,
                RuntimeDurableTimerEntityFrameworkCoreOptions options => options.RecoveryContinuationSigningKey,
                RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions options => options.RecoveryContinuationSigningKey,
                _ => null
            })
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Cast<string>()
            .Append(requestedKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (configuredKeys.Length > 1)
            throw new InvalidOperationException($"Combined Runtime EF persistence requires one compatible recovery-continuation signing key; {owner} would compose conflicting non-null keys.");
    }

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
        var operationalBackend = RuntimeOperationalStateStoreBackend.Find(services);
        var durableTimerBackend = DurableTimerStoreBackend.Find(services);
        var schedulerWorkBackend = SchedulerWorkQueueStoreBackend.Find(services);
        var schedulerPoisonBackend = WorkflowSchedulerPoisonStoreBackend.Find(services);
        if (contextRegistrations.Count > 0 &&
            (owners.Length == 0 || contextRegistrations.Any(descriptor => !owners.Any(predicate => predicate?.Invoke(descriptor) == true) && operationalBackend?.Owns(descriptor) != true && durableTimerBackend?.Owns(descriptor) != true && schedulerWorkBackend?.Owns(descriptor) != true && schedulerPoisonBackend?.Owns(descriptor) != true)))
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

    /// <summary>
    /// Rejects a participant whose connection differs from one already registered. Participants share one context,
    /// so a second connection would otherwise be silently ignored in favor of whichever participant registered first.
    /// </summary>
    public static void EnsureCompatible(
        IServiceCollection services,
        string provider,
        string? connectionString,
        string? connectionName)
    {
        var currentIdentity = EffectiveIdentity(provider, connectionString, connectionName);
        foreach (var (owner, existingProvider, existingConnectionString, existingConnectionName) in services
                     .Select(descriptor => descriptor.ImplementationInstance)
                     .Select(RegisteredConnection)
                     .OfType<(string, string, string?, string?)>())
        {
            if (!string.Equals(EffectiveIdentity(existingProvider, existingConnectionString, existingConnectionName), currentIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Combined Runtime bookmarks and artifact EF persistence requires compatible provider options; {owner} is already registered differently.");
        }
    }

    private static (string Owner, string Provider, string? ConnectionString, string? ConnectionName)? RegisteredConnection(object? instance) => instance switch
    {
        RuntimeBookmarksEntityFrameworkCoreOptions options => ("Runtime bookmarks", options.Provider, options.ConnectionString, options.ConnectionName),
        RuntimeArtifactsEntityFrameworkCoreOptions options => ("Runtime artifacts", options.Provider, options.ConnectionString, options.ConnectionName),
        RuntimeActivityExecutionEntityFrameworkCoreOptions options => ("Runtime activity executions", options.Provider, options.ConnectionString, options.ConnectionName),
        RuntimeWorkflowExecutionEntityFrameworkCoreOptions options => ("Runtime workflow executions", options.Provider, options.ConnectionString, options.ConnectionName),
        RuntimeWorkflowAlterationEntityFrameworkCoreOptions options => ("Runtime alterations", options.Provider, options.ConnectionString, options.ConnectionName),
        RuntimeWorkflowTestScopeEntityFrameworkCoreOptions options => ("Runtime test scopes", options.Provider, options.ConnectionString, options.ConnectionName),
        RuntimeOperationalStateEntityFrameworkCoreOptions options => ("Runtime operational state", options.Provider, options.ConnectionString, options.ConnectionName),
        RuntimeDurableTimerEntityFrameworkCoreOptions options => ("Runtime durable timers", options.Provider, options.ConnectionString, options.ConnectionName),
        RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions options => ("Runtime scheduler work", options.Provider, options.ConnectionString, options.ConnectionName),
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions options => ("Runtime scheduler poison", options.Provider, options.ConnectionString, options.ConnectionName),
        _ => null
    };

    private static string EffectiveIdentity(string provider, string? connectionString, string? connectionName)
    {
        var normalizedProvider = EfRelationalProviderBinding.Normalize(provider);
        if (!string.IsNullOrWhiteSpace(connectionString))
            return $"{normalizedProvider}:connection:{connectionString}";
        if (!string.IsNullOrWhiteSpace(connectionName))
            return $"{normalizedProvider}:name:{connectionName}";
        return $"{normalizedProvider}:default";
    }

    /// <summary>
    /// Registers the one shared Runtime context. Every participant registers it through here, so whichever registers
    /// first, the context resolves the same connection for the same options.
    /// </summary>
    public static IReadOnlyCollection<ServiceDescriptor> AddContext(
        IServiceCollection services,
        string provider,
        string? connectionString,
        string? connectionName) => EfRelationalProviderBinding.Normalize(provider) switch
    {
        "sqlite" => AddContext<BookmarkStateSqliteDbContext>(services, provider, connectionString, connectionName, EfRelationalProviderBinding.UseSqlite),
        "sqlserver" => AddContext<BookmarkStateSqlServerDbContext>(services, provider, connectionString, connectionName, EfRelationalProviderBinding.UseSqlServer),
        "postgresql" => AddContext<BookmarkStatePostgreSqlDbContext>(services, provider, connectionString, connectionName, EfRelationalProviderBinding.UseNpgsql),
        "mysql" => AddContext<BookmarkStateMySqlDbContext>(services, provider, connectionString, connectionName, EfRelationalProviderBinding.UseMySql),
        _ => throw new ArgumentException($"Unknown Runtime EF provider '{provider}'.", nameof(provider))
    };

    private static IReadOnlyCollection<ServiceDescriptor> AddContext<TContext>(
        IServiceCollection services,
        string provider,
        string? connectionString,
        string? connectionName,
        Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : BookmarkStateDbContext
    {
        var start = services.Count;
        services.AddDbContext<TContext>((serviceProvider, builder) => bind(
            builder,
            ResolveConnectionString(serviceProvider, provider, connectionString, connectionName),
            RuntimeEfModule.HistoryTableName,
            typeof(BookmarkStateDbContext).Assembly.GetName().Name));
        services.TryAddScoped<BookmarkStateDbContext>(serviceProvider => serviceProvider.GetRequiredService<TContext>());
        return services.Skip(start).ToArray();
    }

    /// <summary>
    /// An explicit connection string wins, then a named <c>ConnectionStrings</c> entry, then
    /// <see cref="RuntimeEfModule.DefaultConnectionName"/>, then the SQLite default file.
    /// </summary>
    public static string ResolveConnectionString(
        IServiceProvider services,
        string provider,
        string? connectionString,
        string? connectionName)
    {
        if (!string.IsNullOrWhiteSpace(connectionString))
            return connectionString;
        var configuration = services.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(connectionName))
        {
            var named = configuration?.GetConnectionString(connectionName);
            return string.IsNullOrWhiteSpace(named)
                ? throw new InvalidOperationException($"Runtime EF connection '{connectionName}' was not found or was empty in ConnectionStrings.")
                : named;
        }

        var fallback = configuration?.GetConnectionString(RuntimeEfModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (EfRelationalProviderBinding.Normalize(provider) == "sqlite")
            return RuntimeEfModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException(
            $"Runtime EF requires ConnectionString, ConnectionName or ConnectionStrings:{RuntimeEfModule.DefaultConnectionName} for a non-Sqlite provider.");
    }
}
