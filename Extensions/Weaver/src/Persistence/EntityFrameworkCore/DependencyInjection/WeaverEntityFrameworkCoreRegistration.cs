using Elsa.Weaver.Extensions;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Services;
using Elsa.Weaver.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Weaver.Persistence.EntityFrameworkCore.DependencyInjection;

public static class WeaverEntityFrameworkCoreRegistration
{
    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(WeaverDbContext));

    /// <summary>
    /// Registers the module-owned context for the selected provider, the shared migration lifecycle, and selects the
    /// durable session record store and audit store over it.
    /// </summary>
    /// <exception cref="InvalidOperationException">A record store or audit store was already selected or registered outside its selection.</exception>
    public static IServiceCollection AddWeaverEntityFrameworkCore(this IServiceCollection services, WeaverEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var add = Binding.Select<Action<IServiceCollection, WeaverEntityFrameworkCoreOptions>>(options.Provider,
            AddContext<WeaverSqliteDbContext>, AddContext<WeaverSqlServerDbContext>, AddContext<WeaverPostgreSqlDbContext>, AddContext<WeaverMySqlDbContext>);
        services.AddWeaverStores<WeaverDbContext>(options.LeaseDuration);
        services.AddSingleton(options);
        add(services, options);
        services.AddEfModuleMigrations<WeaverDbContext>(options.Provider);
        return services;
    }

    /// <summary>
    /// Selects the durable session record store and the audit sink, reader and ownership over a registered context that
    /// installs <see cref="WeaverModelBuilderExtensions.AddWeaverModel"/>. Every operation uses a fresh scoped context and
    /// commits independently; it never enlists in a caller's tracked work or transaction. Payloads are protected with the
    /// registered <see cref="ISecretValueProtector"/>.
    /// </summary>
    /// <param name="leaseDuration">How long a write or acquisition keeps the owner's lease live, in database time; 30 seconds by default.</param>
    /// <exception cref="InvalidOperationException">A record store or audit store was already selected or registered outside its selection.</exception>
    public static IServiceCollection AddWeaverStores<TContext>(this IServiceCollection services, TimeSpan? leaseDuration = null) where TContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);
        var lease = WeaverEntityFrameworkCoreOptions.ValidateLease(leaseDuration ?? WeaverEntityFrameworkCoreOptions.DefaultLeaseDuration);
        services.TryAddSingleton(provider => new WeaverRecordCodec(provider.GetRequiredService<ISecretValueProtector>()));
        services.SelectWeaverSessionRecordStore(provider => new EfWeaverSessionRecordStore(provider.GetRequiredService<IServiceScopeFactory>(),
            static scoped => scoped.GetRequiredService<TContext>(), provider.GetRequiredService<WeaverRecordCodec>(), lease));
        services.SelectWeaverAuditStore(provider => new EfWeaverAuditStore(provider.GetRequiredService<IServiceScopeFactory>(),
            static scoped => scoped.GetRequiredService<TContext>(), provider.GetRequiredService<WeaverRecordCodec>()));
        return services;
    }

    private static void AddContext<TContext>(IServiceCollection services, WeaverEntityFrameworkCoreOptions options) where TContext : WeaverDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) => Binding.Apply(builder, provider,
            options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<WeaverDbContext>(provider => provider.GetRequiredService<TContext>());
    }
}

public sealed class WeaverEntityFrameworkCoreOptions
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
        : throw new ArgumentOutOfRangeException(nameof(duration), "The Weaver session lease must be from 15 seconds to one day, longer than the owner's renewal interval.");
}
