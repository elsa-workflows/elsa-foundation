using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

public static class RuntimeArtifactsEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeArtifactsEntityFrameworkCore(this IServiceCollection services, RuntimeArtifactsEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        if (services.Any(x => x.ImplementationType == typeof(EfWorkflowExecutableStore)))
            return services;
        var provider = EfRelationalProviderBinding.Normalize(options.Provider);
        _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
        BookmarkStateEfContextRegistration.EnsureCompatible(
            services,
            provider,
            options.ConnectionString,
            options.ConnectionName,
            RuntimeArtifactEfModule.DefaultSqliteConnectionString);
        var configured = new RuntimeArtifactsEntityFrameworkCoreOptions { Provider = options.Provider, ConnectionString = options.ConnectionString, ConnectionName = options.ConnectionName };
        services.TryAddSingleton(configured);
        switch (provider)
        {
            case "sqlite":
                AddContext<BookmarkStateSqliteDbContext>(services, configured, EfRelationalProviderBinding.UseSqlite);
                break;
            case "sqlserver":
                AddContext<BookmarkStateSqlServerDbContext>(services, configured, EfRelationalProviderBinding.UseSqlServer);
                break;
            case "postgresql":
                AddContext<BookmarkStatePostgreSqlDbContext>(services, configured, EfRelationalProviderBinding.UseNpgsql);
                break;
            case "mysql":
                AddContext<BookmarkStateMySqlDbContext>(services, configured, EfRelationalProviderBinding.UseMySql);
                break;
            default:
                throw new ArgumentException($"Unknown Runtime artifacts EF provider '{options.Provider}'.", nameof(options));
        }
        EnsureReplaceable<IWorkflowExecutableStore>(services);
        EnsureReplaceable<IExecutableActivityTemplateStore>(services);
        EnsureReplaceable<IWorkflowExecutableSourceReferenceStore>(services);
        EnsureReplaceable<IExecutableActivityTemplateReader>(services);
        EnsureReplaceable<IExecutableActivityTemplateWriter>(services);
        EnsureReplaceable<IWorkflowExecutableSourceReferenceReader>(services);
        EnsureReplaceable<IWorkflowExecutableSourceReferenceWriter>(services);
        services.RemoveAll<EfWorkflowExecutableStore>();
        services.RemoveAll<EfExecutableActivityTemplateStore>();
        services.RemoveAll<EfWorkflowExecutableSourceReferenceStore>();
        services.AddScoped<EfWorkflowExecutableStore>();
        services.AddScoped<EfExecutableActivityTemplateStore>();
        services.AddScoped<EfWorkflowExecutableSourceReferenceStore>();
        services.RemoveAll<IWorkflowExecutableStore>();
        services.RemoveAll<IExecutableActivityTemplateStore>();
        services.RemoveAll<IExecutableActivityTemplateReader>();
        services.RemoveAll<IExecutableActivityTemplateWriter>();
        services.RemoveAll<IWorkflowExecutableSourceReferenceStore>();
        services.RemoveAll<IWorkflowExecutableSourceReferenceReader>();
        services.RemoveAll<IWorkflowExecutableSourceReferenceWriter>();
        services.AddScoped<IWorkflowExecutableStore>(p => p.GetRequiredService<EfWorkflowExecutableStore>());
        services.AddScoped<IExecutableActivityTemplateStore>(p => p.GetRequiredService<EfExecutableActivityTemplateStore>());
        services.AddScoped<IExecutableActivityTemplateReader>(p => p.GetRequiredService<EfExecutableActivityTemplateStore>());
        services.AddScoped<IExecutableActivityTemplateWriter>(p => p.GetRequiredService<EfExecutableActivityTemplateStore>());
        services.AddScoped<IWorkflowExecutableSourceReferenceStore>(p => p.GetRequiredService<EfWorkflowExecutableSourceReferenceStore>());
        services.AddScoped<IWorkflowExecutableSourceReferenceReader>(p => p.GetRequiredService<EfWorkflowExecutableSourceReferenceStore>());
        services.AddScoped<IWorkflowExecutableSourceReferenceWriter>(p => p.GetRequiredService<EfWorkflowExecutableSourceReferenceStore>());
        return services;
    }
    public static IServiceCollection AddRuntimeExecutableArtifactsEntityFrameworkCore(this IServiceCollection services, RuntimeArtifactsEntityFrameworkCoreOptions options) => services.AddRuntimeArtifactsEntityFrameworkCore(options);
    private static void AddContext<T>(IServiceCollection services, RuntimeArtifactsEntityFrameworkCoreOptions options, Action<DbContextOptionsBuilder, string, string, string?> bind) where T : BookmarkStateDbContext
    { services.AddDbContext<T>((provider, builder) => bind(builder, Resolve(provider, options), RuntimeArtifactEfModule.HistoryTableName, typeof(BookmarkStateDbContext).Assembly.GetName().Name)); services.TryAddScoped<BookmarkStateDbContext>(p => p.GetRequiredService<T>()); }
    private static string Resolve(IServiceProvider provider, RuntimeArtifactsEntityFrameworkCoreOptions options) { if (!string.IsNullOrWhiteSpace(options.ConnectionString)) return options.ConnectionString!; var cfg = provider.GetService<IConfiguration>(); if (!string.IsNullOrWhiteSpace(options.ConnectionName)) return cfg?.GetConnectionString(options.ConnectionName!) ?? throw new InvalidOperationException($"Runtime artifacts EF connection '{options.ConnectionName}' was not found."); var fallback = cfg?.GetConnectionString(RuntimeArtifactEfModule.DefaultConnectionName); if (!string.IsNullOrWhiteSpace(fallback)) return fallback!; if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite") return RuntimeArtifactEfModule.DefaultSqliteConnectionString; throw new InvalidOperationException("Runtime artifacts EF requires ConnectionString or ConnectionName for a non-Sqlite provider."); }
    private static void EnsureReplaceable<T>(IServiceCollection services)
    {
        var descriptors = services.Where(x => x.ServiceType == typeof(T)).ToArray();
        if (descriptors.Any(x => x.ImplementationType is not null && x.ImplementationType != typeof(InMemoryWorkflowExecutableStore) && x.ImplementationType != typeof(InMemoryExecutableActivityTemplateStore) && x.ImplementationType != typeof(InMemoryWorkflowExecutableSourceReferenceStore) && !x.ImplementationType.Namespace?.StartsWith("Elsa.Persistence.Groundwork", StringComparison.Ordinal) == true) ||
            descriptors.Any(x => x.ImplementationFactory is not null && !x.ImplementationFactory.Method.DeclaringType?.Namespace?.StartsWith("Elsa.Persistence.Groundwork", StringComparison.Ordinal) == true))
            throw new InvalidOperationException($"An explicit {typeof(T).Name} is already registered; Runtime artifact EF persistence refuses to replace it implicitly.");
        foreach (var descriptor in descriptors)
            services.Remove(descriptor);
    }
}
public sealed class RuntimeArtifactsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
