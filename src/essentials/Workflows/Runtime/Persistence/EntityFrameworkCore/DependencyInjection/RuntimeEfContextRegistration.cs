using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

internal static class RuntimeEfContextRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(RuntimeDbContext));

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
        where TContext : RuntimeDbContext
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
        var ensure = Binding.Select<Action<IServiceCollection, Func<ServiceDescriptor, bool>?, string>>(
            provider,
            EnsureContextIsAvailable<RuntimeSqliteDbContext>,
            EnsureContextIsAvailable<RuntimeSqlServerDbContext>,
            EnsureContextIsAvailable<RuntimePostgreSqlDbContext>,
            EnsureContextIsAvailable<RuntimeMySqlDbContext>);
        ensure(services, ownsExistingContext, owner);
    }

    public static IReadOnlyCollection<ServiceDescriptor> ContextRegistrations(IServiceCollection services, string provider) =>
        services.Where(Binding.Select<Func<ServiceDescriptor, bool>>(
            provider,
            IsContextRegistration<RuntimeSqliteDbContext>,
            IsContextRegistration<RuntimeSqlServerDbContext>,
            IsContextRegistration<RuntimePostgreSqlDbContext>,
            IsContextRegistration<RuntimeMySqlDbContext>)).ToArray();

    private static bool IsContextRegistration<TContext>(ServiceDescriptor descriptor)
        where TContext : RuntimeDbContext
    {
        if (descriptor.ServiceType == typeof(TContext) ||
            descriptor.ServiceType == typeof(DbContextOptions<TContext>) ||
            descriptor.ServiceType == typeof(RuntimeDbContext))
            return true;

        return IsContextType(descriptor.ImplementationType) ||
               IsContextType(descriptor.ImplementationInstance?.GetType()) ||
               IsContextType(descriptor.ImplementationFactory?.Method.ReturnType);
    }

    private static bool IsContextType(Type? type) =>
        type is not null && typeof(RuntimeDbContext).IsAssignableFrom(type);

    /// <summary>
    /// Rejects a participant whose connection differs from one already registered. Participants share one context,
    /// so a second connection would otherwise be silently ignored in favor of whichever participant registered first.
    /// </summary>
    public static void EnsureCompatible(
        IServiceCollection services,
        string provider,
        string? connectionString,
        string? connectionName,
        string? schema = null,
        bool pooling = false)
    {
        var currentIdentity = EffectiveIdentity(provider, connectionString, connectionName, schema, pooling);
        foreach (var registration in services
                     .Select(descriptor => descriptor.ImplementationInstance)
                     .Select(RegisteredConnection)
                     .OfType<RegisteredContextOptions>())
        {
            if (!string.Equals(EffectiveIdentity(registration), currentIdentity, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Combined Runtime bookmarks and artifact EF persistence requires compatible provider options; {registration.Owner} is already registered differently.");
        }
    }

    /// <summary>What one participant asked the shared Runtime context to be bound to.</summary>
    private sealed record RegisteredContextOptions(string Owner, string Provider, string? ConnectionString, string? ConnectionName, string? Schema, bool Pooling);

    private static RegisteredContextOptions? RegisteredConnection(object? instance) => instance switch
    {
        RuntimeBookmarksEntityFrameworkCoreOptions options => new("Runtime bookmarks", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        RuntimeArtifactsEntityFrameworkCoreOptions options => new("Runtime artifacts", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        RuntimeActivityExecutionEntityFrameworkCoreOptions options => new("Runtime activity executions", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        RuntimeWorkflowExecutionEntityFrameworkCoreOptions options => new("Runtime workflow executions", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        RuntimeWorkflowAlterationEntityFrameworkCoreOptions options => new("Runtime alterations", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        RuntimeWorkflowTestScopeEntityFrameworkCoreOptions options => new("Runtime test scopes", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        RuntimeOperationalStateEntityFrameworkCoreOptions options => new("Runtime operational state", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        RuntimeDurableTimerEntityFrameworkCoreOptions options => new("Runtime durable timers", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        RuntimeSchedulerWorkQueueEntityFrameworkCoreOptions options => new("Runtime scheduler work", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        RuntimeSchedulerPoisonEntityFrameworkCoreOptions options => new("Runtime scheduler poison", options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling),
        _ => null
    };

    private static string EffectiveIdentity(RegisteredContextOptions options) =>
        EffectiveIdentity(options.Provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling);

    /// <summary>
    /// What two participants have to agree on to share one context. Schema and pooling join the connection here
    /// because they are equally part of what the shared registration builds, and whichever participant registers
    /// first would otherwise decide them for the rest in silence.
    /// </summary>
    private static string EffectiveIdentity(string provider, string? connectionString, string? connectionName, string? schema, bool pooling)
    {
        var normalizedProvider = EfRelationalProviderBinding.Normalize(provider);
        var suffix = $":schema:{schema?.Trim() ?? ""}:pooled:{pooling}";
        if (!string.IsNullOrWhiteSpace(connectionString))
            return $"{normalizedProvider}:connection:{connectionString}{suffix}";
        if (!string.IsNullOrWhiteSpace(connectionName))
            return $"{normalizedProvider}:name:{connectionName}{suffix}";
        return $"{normalizedProvider}:default{suffix}";
    }

    /// <summary>
    /// Registers the one shared Runtime context. Every participant registers it through here, so whichever registers
    /// first, the context resolves the same connection for the same options.
    /// </summary>
    public static IReadOnlyCollection<ServiceDescriptor> AddContext(
        IServiceCollection services,
        string provider,
        string? connectionString,
        string? connectionName,
        string? schema = null,
        bool pooling = false)
    {
        var addContext = Binding.Select<Func<IServiceCollection, RegisteredContextOptions, IReadOnlyCollection<ServiceDescriptor>>>(
            provider,
            AddContext<RuntimeSqliteDbContext>,
            AddContext<RuntimeSqlServerDbContext>,
            AddContext<RuntimePostgreSqlDbContext>,
            AddContext<RuntimeMySqlDbContext>);
        return addContext(services, new RegisteredContextOptions("Runtime", provider, connectionString, connectionName, schema, pooling));
    }

    private static IReadOnlyCollection<ServiceDescriptor> AddContext<TContext>(
        IServiceCollection services,
        RegisteredContextOptions options)
        where TContext : RuntimeDbContext
    {
        var start = services.Count;
        Binding.AddContext<TContext>(services, options.Pooling, (serviceProvider, builder) =>
            Binding.Apply(builder, serviceProvider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.TryAddScoped<RuntimeDbContext>(serviceProvider => serviceProvider.GetRequiredService<TContext>());
        return services.Skip(start).ToArray();
    }
}
