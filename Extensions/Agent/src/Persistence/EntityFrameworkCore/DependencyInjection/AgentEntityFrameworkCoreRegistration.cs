using Elsa.Agent.Extensions;
using Elsa.Agent.Persistence.EntityFrameworkCore.Services;
using Elsa.Agent.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Persistence.EntityFrameworkCore.DependencyInjection;

public static class AgentEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(AgentDbContext));

    /// <summary>
    /// Registers the module-owned context for the selected provider, the shared migration lifecycle, and selects the
    /// durable session record store and audit store over it.
    /// </summary>
    /// <exception cref="InvalidOperationException">A record store or audit store was already selected or registered outside its selection.</exception>
    public static IServiceCollection AddAgentEntityFrameworkCore(this IServiceCollection services, AgentEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var add = Binding.Select<Action<IServiceCollection, AgentEntityFrameworkCoreOptions>>(options.Provider,
            AddContext<AgentSqliteDbContext>, AddContext<AgentSqlServerDbContext>, AddContext<AgentPostgreSqlDbContext>, AddContext<AgentMySqlDbContext>);
        services.AddAgentStores<AgentDbContext>(options.LeaseDuration);
        services.AddSingleton(options);
        add(services, options);
        services.AddEfModuleMigrations<AgentDbContext>(options.Provider);
        return services;
    }

    /// <summary>
    /// Selects the durable session record store and the audit sink, reader and ownership over a registered context that
    /// installs <see cref="AgentModelBuilderExtensions.AddAgentModel"/>. Every operation uses a fresh scoped context and
    /// commits independently; it never enlists in a caller's tracked work or transaction. Payloads are protected with the
    /// registered <see cref="ISecretValueProtector"/>.
    /// </summary>
    /// <param name="leaseDuration">How long a write or acquisition keeps the owner's lease live, in database time; 30 seconds by default.</param>
    /// <exception cref="InvalidOperationException">A record store or audit store was already selected or registered outside its selection.</exception>
    public static IServiceCollection AddAgentStores<TContext>(this IServiceCollection services, TimeSpan? leaseDuration = null) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        var lease = AgentEntityFrameworkCoreOptions.ValidateLease(leaseDuration ?? AgentEntityFrameworkCoreOptions.DefaultLeaseDuration);
        services.TryAddSingleton(provider => new AgentRecordCodec(provider.GetRequiredService<ISecretValueProtector>()));
        services.SelectAgentSessionRecordStore(provider => new EfAgentSessionRecordStore(provider.GetRequiredService<IServiceScopeFactory>(),
            static scoped => scoped.GetRequiredService<TContext>(), provider.GetRequiredService<AgentRecordCodec>(), lease));
        services.SelectAgentAuditStore(provider => new EfAgentAuditStore(provider.GetRequiredService<IServiceScopeFactory>(),
            static scoped => scoped.GetRequiredService<TContext>(), provider.GetRequiredService<AgentRecordCodec>()));
        return services;
    }

    private static void AddContext<TContext>(IServiceCollection services, AgentEntityFrameworkCoreOptions options) where TContext : AgentDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) => Binding.Apply(builder, provider,
            options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<AgentDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class AgentEntityFrameworkCoreOptions
{
    internal static readonly TimeSpan DefaultLeaseDuration = TimeSpan.FromSeconds(30);

    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
    public bool Pooling { get; set; }
    /// <summary>The owner's lease in database time; from 15 seconds (longer than the owner's 10-second renewal) to one day.</summary>
    public TimeSpan LeaseDuration { get; set; } = DefaultLeaseDuration;

    internal static TimeSpan ValidateLease(TimeSpan duration) => duration >= TimeSpan.FromSeconds(15) && duration <= TimeSpan.FromDays(1)
        ? duration
        : throw new ArgumentOutOfRangeException(nameof(duration), "The agent session lease must be from 15 seconds to one day, longer than the owner's renewal interval.");
}
