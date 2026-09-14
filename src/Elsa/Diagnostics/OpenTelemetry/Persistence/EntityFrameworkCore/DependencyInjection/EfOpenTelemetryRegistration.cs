using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Stores;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.DependencyInjection;

public static class EfOpenTelemetryRegistration
{
    public static IServiceCollection AddOpenTelemetryEntityFrameworkCore(
        this IServiceCollection services,
        OpenTelemetryEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        var binding = new EfOpenTelemetryBinding(options.TenantId, options.ScopeId, options.SourceId);
        binding.Validate();
        var registration = new EfOpenTelemetryRegistrationIdentity(
            provider,
            options.ConnectionString,
            options.ConnectionName,
            binding);
        var existingRegistration = services
            .Where(descriptor => descriptor.ServiceType == typeof(EfOpenTelemetryRegistrationIdentity))
            .Select(descriptor => descriptor.ImplementationInstance as EfOpenTelemetryRegistrationIdentity)
            .SingleOrDefault(identity => identity is not null);
        var storeRegistrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(IOpenTelemetryStore))
            .ToArray();
        if (existingRegistration is not null)
        {
            if (existingRegistration != registration || storeRegistrations.Length != 1)
                throw new InvalidOperationException("OpenTelemetry EF persistence is already registered with different options or a conflicting store selection.");
            return services;
        }

        var hasOnlyDefaultStore = storeRegistrations is [{ ImplementationType: null, ImplementationFactory: not null, ImplementationInstance: null }] &&
            services.Count(descriptor => descriptor.ServiceType == typeof(Elsa.Diagnostics.OpenTelemetry.Providers.InMemory.InMemoryOpenTelemetryStore)) == 1;
        if (storeRegistrations.Length != 0 && !hasOnlyDefaultStore)
            throw new InvalidOperationException("An explicit IOpenTelemetryStore is already registered.");

        var configuredOptions = new OpenTelemetryEntityFrameworkCoreOptions
        {
            Provider = options.Provider,
            ConnectionString = options.ConnectionString,
            ConnectionName = options.ConnectionName,
            TenantId = options.TenantId,
            ScopeId = options.ScopeId,
            SourceId = options.SourceId
        };
        services.AddSingleton(registration);
        services.TryAddSingleton(binding);
        services.TryAddSingleton<IOpenTelemetrySourceRegistry, Elsa.Diagnostics.OpenTelemetry.Services.OpenTelemetrySourceRegistry>();
        services.AddOptions<Elsa.Diagnostics.OpenTelemetry.Core.Options.OpenTelemetryDiagnosticsOptions>();
        services.RemoveAll<Elsa.Diagnostics.OpenTelemetry.Providers.InMemory.InMemoryOpenTelemetryStore>();
        switch (provider)
        {
            case "sqlite":
                AddContext<OpenTelemetrySqliteDbContext>(services, configuredOptions, EfRelationalProviderBinding.UseSqlite);
                break;
            case "sqlserver":
                AddContext<OpenTelemetrySqlServerDbContext>(services, configuredOptions, EfRelationalProviderBinding.UseSqlServer);
                break;
            case "postgresql":
                AddContext<OpenTelemetryPostgreSqlDbContext>(services, configuredOptions, EfRelationalProviderBinding.UseNpgsql);
                break;
            case "mysql":
                AddContext<OpenTelemetryMySqlDbContext>(services, configuredOptions, EfRelationalProviderBinding.UseMySql);
                break;
            default:
                throw new ArgumentException($"Unknown OpenTelemetry EF provider '{options.Provider}'. Expected Sqlite, SqlServer, PostgreSql, or MySql.", nameof(options));
        }
        services.AddSingleton(configuredOptions);
        services.ReplaceDiagnosticsStore<IOpenTelemetryStore, EfOpenTelemetryStore>(ServiceLifetime.Singleton);
        services.AddDiagnosticsPersistenceLifecycle<EfOpenTelemetryStore>();
        return services;
    }

    private static void AddContext<TContext>(IServiceCollection services, OpenTelemetryEntityFrameworkCoreOptions options, Action<DbContextOptionsBuilder, string, string, string?> bind)
        where TContext : EfOpenTelemetryDbContext
    {
        services.AddDbContext<TContext>((provider, builder) => bind(builder, ResolveConnectionString(provider, options), EfOpenTelemetryModule.HistoryTableName, typeof(OpenTelemetryDbContext).Assembly.GetName().Name));
        services.AddScoped<OpenTelemetryDbContext>(provider => provider.GetRequiredService<TContext>());
        services.AddScoped<EfOpenTelemetryDbContext>(provider => (EfOpenTelemetryDbContext)provider.GetRequiredService<TContext>());
    }

    internal static string ResolveConnectionString(IServiceProvider provider, OpenTelemetryEntityFrameworkCoreOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
            return options.ConnectionString;
        var configuration = provider.GetService<IConfiguration>();
        if (!string.IsNullOrWhiteSpace(options.ConnectionName))
        {
            var named = configuration?.GetConnectionString(options.ConnectionName);
            if (string.IsNullOrWhiteSpace(named))
                throw new InvalidOperationException($"OpenTelemetry EF connection '{options.ConnectionName}' was not found in ConnectionStrings.");
            return named;
        }
        var fallback = configuration?.GetConnectionString(EfOpenTelemetryModule.DefaultConnectionName);
        if (!string.IsNullOrWhiteSpace(fallback))
            return fallback;
        if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite")
            return EfOpenTelemetryModule.DefaultSqliteConnectionString;
        throw new InvalidOperationException("OpenTelemetry EF requires ConnectionString or ConnectionName for a non-Sqlite provider.");
    }

    private sealed record EfOpenTelemetryRegistrationIdentity(
        string Provider,
        string? ConnectionString,
        string? ConnectionName,
        EfOpenTelemetryBinding Binding);
}
