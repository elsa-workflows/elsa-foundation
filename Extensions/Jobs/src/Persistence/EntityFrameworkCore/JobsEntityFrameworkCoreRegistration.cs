using CShells.Features;
using Elsa.Jobs.Core;
using Elsa.Persistence.EntityFramework;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Jobs.Persistence.EntityFrameworkCore;

public class JobsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? Schema { get; set; }
    public bool Pooling { get; set; }
    public int MaximumPageSize { get; set; } = 100;
    public int MaximumClaimScan { get; set; } = 1000;
    public int MaximumWriteAttempts { get; set; } = EfWriteRetry.DefaultMaxAttempts;
    /// <summary>Opt-in terminal input lifetime, captured for each new admission. Null retains indefinitely.</summary>
    public TimeSpan? TerminalInputRetention { get; set; }
    /// <summary>Opt-in terminal result/progress/physical-report payload lifetime, captured for each new admission. Null retains indefinitely.</summary>
    public TimeSpan? TerminalResultRetention { get; set; }
}

public static class JobsEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddJobsEntityFrameworkCore(this IServiceCollection services,
        Action<JobsEntityFrameworkCoreOptions>? configure = null)
    {
        var options = new JobsEntityFrameworkCoreOptions();
        configure?.Invoke(options);
        return Register(services, options, shell: false);
    }

    internal static IServiceCollection Register(IServiceCollection services, JobsEntityFrameworkCoreOptions options, bool shell)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(JobsStoreSettings)))
            throw new JobException("jobs.persistence.duplicate-authority", "Jobs EF persistence is already configured in this service collection.");
        if (options.MaximumPageSize is < 1 or > 1000 || options.MaximumClaimScan is < 1 or > 100_000 || options.MaximumWriteAttempts is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(options), "Jobs persistence bounds are invalid.");
        ValidateRetention(options.TerminalInputRetention); ValidateRetention(options.TerminalResultRetention);
        var settings = new JobsStoreSettings(options.MaximumPageSize, options.MaximumClaimScan, options.MaximumWriteAttempts,
            options.TerminalInputRetention, options.TerminalResultRetention);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var connectionString = options.ConnectionString;
        var connectionName = options.ConnectionName;
        var schema = options.Schema;
        var pooling = options.Pooling;
        var binding = EfModuleBinding.For(typeof(JobsDbContext));
        void Configure(IServiceProvider services, DbContextOptionsBuilder builder) =>
            binding.Apply(builder, services, provider, connectionString, connectionName, schema);
        switch (provider)
        {
            case "sqlite": Context<JobsSqliteDbContext>(services, Configure, pooling); break;
            case "postgresql": Context<JobsPostgreSqlDbContext>(services, Configure, pooling); break;
            default: throw new NotSupportedException("Jobs currently supports SQLite and PostgreSQL bindings only.");
        }
        services.AddScoped<JobsDbContext>(x => x.GetRequiredService<IDbContextFactory<JobsDbContext>>().CreateDbContext());
        services.AddSingleton(settings);
        services.TryAddScoped<EfJobStore>(provider =>
        {
            var calculators = provider.GetServices<IJobRecurrenceCalculator>().Take(2).ToArray();
            if (calculators.Length > 1)
                throw new JobException("jobs.recurrence.duplicate-authority", "Jobs requires at most one recurrence calculator in this authority.");
            return new(provider.GetRequiredService<IDbContextFactory<JobsDbContext>>(), settings, calculators.SingleOrDefault());
        });
        services.TryAddScoped<IJobCommands>(x => x.GetRequiredService<EfJobStore>());
        services.TryAddScoped<IJobQueries>(x => x.GetRequiredService<IJobCommands>() as IJobQueries
            ?? throw new InvalidOperationException("A Jobs command replacement must implement IJobQueries or register its own query provider."));
        if (shell) services.AddEfModuleMigrations<JobsDbContext>(provider);
        else services.AddEfModuleHostMigrations<JobsDbContext>(provider);
        return services;
    }

    private static void ValidateRetention(TimeSpan? value)
    {
        if (value is { } duration && (duration <= TimeSpan.Zero || duration > TimeSpan.FromDays(36500)))
            throw new ArgumentOutOfRangeException(nameof(value), "A Jobs payload lifetime must be positive and at most 100 years.");
    }

    private static void Context<TContext>(IServiceCollection services, Action<IServiceProvider, DbContextOptionsBuilder> configure, bool pooling)
        where TContext : JobsDbContext
    {
        if (pooling) services.AddPooledDbContextFactory<TContext>(configure);
        else services.AddDbContextFactory<TContext>(configure, ServiceLifetime.Scoped);
        services.AddScoped<IDbContextFactory<JobsDbContext>, JobsContextFactory<TContext>>();
    }

    private sealed class JobsContextFactory<TContext>(IDbContextFactory<TContext> factory) : IDbContextFactory<JobsDbContext> where TContext : JobsDbContext
    {
        public JobsDbContext CreateDbContext() => factory.CreateDbContext();
        public async Task<JobsDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => await factory.CreateDbContextAsync(cancellationToken);
    }
}

[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Jobs")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "JobsEntityFrameworkCore", DisplayName = "Jobs EF Core Persistence", Description = "Durable Jobs authority using SQLite or PostgreSQL.")]
[UsesEfModule("JobsRuntime")]
public class JobsEntityFrameworkCoreFeature : JobsEntityFrameworkCoreOptions, IShellFeature
{
    public virtual void ConfigureServices(IServiceCollection services) => JobsEntityFrameworkCoreRegistration.Register(services, this, shell: true);
}

internal sealed record JobsStoreSettings(int MaximumPageSize, int MaximumClaimScan, int MaximumWriteAttempts,
    TimeSpan? TerminalInputRetention = null, TimeSpan? TerminalResultRetention = null);
