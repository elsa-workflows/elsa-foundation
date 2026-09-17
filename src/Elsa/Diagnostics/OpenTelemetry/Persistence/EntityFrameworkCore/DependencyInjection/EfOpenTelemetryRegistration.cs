using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.Stores;
using Elsa.Diagnostics.Persistence.Extensions;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.OpenTelemetry.Persistence.EntityFrameworkCore.DependencyInjection;

public static class EfOpenTelemetryRegistration
{
    private static readonly EfModuleBinding Binding = new(
        "OpenTelemetry",
        EfOpenTelemetryModule.HistoryTableName,
        typeof(OpenTelemetryDbContext).Assembly.GetName().Name,
        EfOpenTelemetryModule.DefaultConnectionName,
        EfOpenTelemetryModule.DefaultSqliteConnectionString);

    public static IServiceCollection AddOpenTelemetryEntityFrameworkCore(
        this IServiceCollection services,
        OpenTelemetryEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var addContext = Binding.Select<Action<IServiceCollection, OpenTelemetryEntityFrameworkCoreOptions>>(
            options.Provider,
            AddContext<OpenTelemetrySqliteDbContext>,
            AddContext<OpenTelemetrySqlServerDbContext>,
            AddContext<OpenTelemetryPostgreSqlDbContext>,
            AddContext<OpenTelemetryMySqlDbContext>);
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
        addContext(services, configuredOptions);
        services.AddSingleton(configuredOptions);
        services.ReplaceDiagnosticsStore<IOpenTelemetryStore, EfOpenTelemetryStore>(ServiceLifetime.Singleton);
        services.AddDiagnosticsPersistenceLifecycle<EfOpenTelemetryStore>();
        return services;
    }

    private static void AddContext<TContext>(IServiceCollection services, OpenTelemetryEntityFrameworkCoreOptions options)
        where TContext : EfOpenTelemetryDbContext
    {
        services.AddDbContext<TContext>((provider, builder) => Binding.Apply(builder, provider, options.Provider, options.ConnectionString, options.ConnectionName));
        services.AddScoped<OpenTelemetryDbContext>(provider => provider.GetRequiredService<TContext>());
        services.AddScoped<EfOpenTelemetryDbContext>(provider => (EfOpenTelemetryDbContext)provider.GetRequiredService<TContext>());
    }

    private sealed record EfOpenTelemetryRegistrationIdentity(
        string Provider,
        string? ConnectionString,
        string? ConnectionName,
        EfOpenTelemetryBinding Binding);
}
