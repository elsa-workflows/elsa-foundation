using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.EntityFrameworkCore;

public static class EfClusterMembershipServiceCollectionExtensions
{
    /// <summary>The EF provider's name, as startup diagnostics report it.</summary>
    public const string ProviderName = "entity-framework-core";

    private static readonly EfModuleBinding Binding = EfModuleBinding.For(typeof(ClusterMembershipDbContext));

    /// <summary>
    /// Composes the EF membership provider on a host container (spec 183, FR-024). It replaces the in-process default in
    /// either order, and refuses to sit beside any other provider (FR-001). The host starts only with an explicit host id
    /// (FR-003a) and a cleanup period longer than the expiry period plus the skew allowance (FR-031).
    /// </summary>
    /// <remarks>
    /// Call it on the host container, never from a shell feature: membership is selected once per host (ADR 0078,
    /// amended; spec 183, Decisions, Q20). Shell containers are built from copies of the host's registrations and all
    /// resolve the one member the host created. The module migrates through the plain-host migrator only, before the
    /// member joins, so a shell activation never migrates it again.
    /// </remarks>
    public static IServiceCollection AddEfClusterMembership(this IServiceCollection services, EfClusterMembershipOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        var host = new HostMember(options);
        // First, so a competing provider is refused before anything else of this one is registered.
        services.AddClusterMembershipProvider(new ClusterMembershipProviderRegistration(
            ProviderName,
            ClusterProviderKind.Durable,
            ServiceDescriptor.Singleton<IClusterMembership>(host.Membership)));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IValidateOptions<ClusterMembershipOptions>>(new CleanupPeriodValidator(options.CleanupPeriod));

        var addContext = Binding.Select<Action<IServiceCollection, EfClusterMembershipOptions>>(
            options.Provider,
            AddContext<ClusterMembershipSqliteDbContext>,
            AddContext<ClusterMembershipSqlServerDbContext>,
            AddContext<ClusterMembershipPostgreSqlDbContext>,
            AddContext<ClusterMembershipMySqlDbContext>);
        addContext(services, options);
        services.AddEfModuleHostMigrations<ClusterMembershipDbContext>(options.Provider);

        // After the migrator's hosted service, so the table exists before the member joins.
        services.AddSingleton<IHostedService>(host.Lifecycle);
        return services;
    }

    private static void AddContext<TContext>(IServiceCollection services, EfClusterMembershipOptions options)
        where TContext : ClusterMembershipDbContext
    {
        Binding.AddContext<TContext>(services, options.Pooling, (provider, builder) =>
            Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName, options.Schema));
        services.AddScoped<ClusterMembershipDbContext>(provider => provider.GetRequiredService<TContext>());
    }

    /// <summary>
    /// The one member a host's registrations share: created on first use, from the container that first asks, which is
    /// the host's own as it starts. Every shell container resolves the same instance through the copied descriptor.
    /// </summary>
    private sealed class HostMember(EfClusterMembershipOptions settings)
    {
        private readonly object _gate = new();
        private EfClusterMembership? _member;

        public IClusterMembership Membership(IServiceProvider services) => Get(services);

        public IHostedService Lifecycle(IServiceProvider services)
        {
            var timings = services.GetRequiredService<IOptions<ClusterMembershipOptions>>().Value;
            return new EfClusterMembershipLifecycle(
                Get(services),
                timings.HeartbeatInterval,
                // Cleanup runs every tenth of its period, and never more often than the heartbeat.
                TimeSpan.FromTicks(Math.Max(timings.HeartbeatInterval.Ticks, settings.CleanupPeriod.Ticks / 10)),
                services.GetRequiredService<TimeProvider>(),
                services.GetService<IHostApplicationLifetime>(),
                Loggers(services).CreateLogger<EfClusterMembershipLifecycle>());
        }

        private EfClusterMembership Get(IServiceProvider services)
        {
            lock (_gate)
                return _member ??= new EfClusterMembership(
                    services.GetRequiredService<IServiceScopeFactory>(),
                    services.GetRequiredService<IOptions<ClusterMembershipOptions>>(),
                    settings,
                    services.GetServices<IMemberReportSource<ReadabilitySection>>(),
                    services.GetServices<IMemberReportSource<RunnabilitySection>>(),
                    services.GetRequiredService<TimeProvider>(),
                    Loggers(services).CreateLogger<EfClusterMembership>());
        }

        private static ILoggerFactory Loggers(IServiceProvider services) =>
            services.GetService<ILoggerFactory>() ?? NullLoggerFactory.Instance;
    }

    private sealed class CleanupPeriodValidator(TimeSpan cleanupPeriod) : IValidateOptions<ClusterMembershipOptions>
    {
        public ValidateOptionsResult Validate(string? name, ClusterMembershipOptions options) =>
            cleanupPeriod > options.ExpiryPeriod + options.SkewAllowance
                ? ValidateOptionsResult.Success
                : ValidateOptionsResult.Fail(
                    $"{ClusterMembershipOptions.SectionName}:{EfClusterMembershipOptions.SectionKey}:{nameof(EfClusterMembershipOptions.CleanupPeriod)} " +
                    $"must exceed the expiry period plus the skew allowance, so no entry is deleted while a reader may still count it; it is " +
                    $"{cleanupPeriod} against {options.ExpiryPeriod} plus {options.SkewAllowance}.");
    }
}
