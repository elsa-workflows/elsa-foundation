using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
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
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            if (services.Any(x => x.ImplementationType == typeof(EfWorkflowExecutableStore)))
            {
                var registeredBackend = RuntimeArtifactStoreBackend.Find(services);
                registeredBackend?.EnsureOwnsRegisteredContracts(services);
                if (registeredBackend?.Name != RuntimeArtifactStoreBackend.EntityFramework)
                    throw new InvalidOperationException("Runtime artifacts EF persistence cannot reuse an unowned or differently owned registration.");
                var siblingBookmarksBackendForRepeat = BookmarkStateStoreBackend.Find(services);
                if (siblingBookmarksBackendForRepeat?.Name == BookmarkStateStoreBackend.EntityFramework)
                    siblingBookmarksBackendForRepeat.EnsureOwnsRegisteredContract(services);
                var existing = services.Select(x => x.ImplementationInstance).OfType<RuntimeArtifactsEntityFrameworkCoreOptions>().SingleOrDefault();
                if (existing is null ||
                    !string.Equals(EfRelationalProviderBinding.Normalize(existing.Provider), EfRelationalProviderBinding.Normalize(options.Provider), StringComparison.Ordinal) ||
                    !string.Equals(existing.ConnectionString, options.ConnectionString, StringComparison.Ordinal) ||
                    !string.Equals(existing.ConnectionName, options.ConnectionName, StringComparison.Ordinal))
                    throw new InvalidOperationException("Runtime artifacts EF persistence is already registered with different provider options.");
                BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                    services,
                    provider,
                    "Runtime artifacts",
                    registeredBackend.Owns,
                    BookmarkStateStoreBackend.Find(services) is { Name: BookmarkStateStoreBackend.EntityFramework } siblingBookmarksBackend
                        ? siblingBookmarksBackend.Owns
                        : null);
                return services;
            }

            var existingBackend = RuntimeArtifactStoreBackend.Find(services);
            if (existingBackend is not null)
                existingBackend.EnsureOwnsRegisteredContracts(services);
            else
                RuntimeArtifactStoreBackend.EnsureNoUnownedArtifactRegistrations(services);
            var existingBookmarksBackend = BookmarkStateStoreBackend.Find(services);
            if (existingBookmarksBackend?.Name == BookmarkStateStoreBackend.EntityFramework)
                existingBookmarksBackend.EnsureOwnsRegisteredContract(services);
            BookmarkStateEfContextRegistration.EnsureCompatible(
                services,
                provider,
                options.ConnectionString,
                options.ConnectionName,
                RuntimeArtifactEfModule.DefaultSqliteConnectionString);
            var commitExistingBackendRemoval = existingBackend?.PrepareRemoveOwnedArtifacts(services);
            services.AddOptions<RuntimeRecoveryContinuationOptions>()
                .Configure(options => options.AllowEphemeralDevelopmentKey = false);
            services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());
            var configured = new RuntimeArtifactsEntityFrameworkCoreOptions { Provider = options.Provider, ConnectionString = options.ConnectionString, ConnectionName = options.ConnectionName };
            var optionsStart = services.Count;
            services.AddSingleton(configured);
            var ownedInfrastructure = new List<ServiceDescriptor> { services[optionsStart] };
            var bookmarksBackend = existingBookmarksBackend;
            var bookmarksOwnContext = bookmarksBackend?.Name == BookmarkStateStoreBackend.EntityFramework;
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                bookmarksOwnContext ? bookmarksBackend!.Owns : null,
                "Runtime artifacts");
            if (bookmarksOwnContext)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(bookmarksBackend!.Owns));
            else
                switch (provider)
                {
                    case "sqlite": ownedInfrastructure.AddRange(AddContext<BookmarkStateSqliteDbContext>(services, configured, EfRelationalProviderBinding.UseSqlite)); break;
                    case "sqlserver": ownedInfrastructure.AddRange(AddContext<BookmarkStateSqlServerDbContext>(services, configured, EfRelationalProviderBinding.UseSqlServer)); break;
                    case "postgresql": ownedInfrastructure.AddRange(AddContext<BookmarkStatePostgreSqlDbContext>(services, configured, EfRelationalProviderBinding.UseNpgsql)); break;
                    case "mysql": ownedInfrastructure.AddRange(AddContext<BookmarkStateMySqlDbContext>(services, configured, EfRelationalProviderBinding.UseMySql)); break;
                }
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
            RuntimeArtifactStoreBackend.Register(services, new RuntimeArtifactStoreBackend(
                RuntimeArtifactStoreBackend.EntityFramework,
                RuntimeArtifactStoreBackend.CaptureArtifactSurfaceRegistrations(services)
                    .Concat(ownedInfrastructure)
                    .ToArray()));
            commitExistingBackendRemoval?.Invoke(services);
            return services;
        }
        catch
        {
            services.Clear();
            foreach (var descriptor in snapshot)
                services.Add(descriptor);
            throw;
        }
    }
    public static IServiceCollection AddRuntimeExecutableArtifactsEntityFrameworkCore(this IServiceCollection services, RuntimeArtifactsEntityFrameworkCoreOptions options) => services.AddRuntimeArtifactsEntityFrameworkCore(options);
    private static IReadOnlyCollection<ServiceDescriptor> AddContext<T>(IServiceCollection services, RuntimeArtifactsEntityFrameworkCoreOptions options, Action<DbContextOptionsBuilder, string, string, string?> bind) where T : BookmarkStateDbContext
    {
        var start = services.Count;
        services.AddDbContext<T>((provider, builder) => bind(builder, Resolve(provider, options), RuntimeArtifactEfModule.HistoryTableName, typeof(BookmarkStateDbContext).Assembly.GetName().Name));
        services.TryAddScoped<BookmarkStateDbContext>(p => p.GetRequiredService<T>());
        return services.Skip(start).ToArray();
    }
    private static string Resolve(IServiceProvider provider, RuntimeArtifactsEntityFrameworkCoreOptions options) { if (!string.IsNullOrWhiteSpace(options.ConnectionString)) return options.ConnectionString!; var cfg = provider.GetService<IConfiguration>(); if (!string.IsNullOrWhiteSpace(options.ConnectionName)) return cfg?.GetConnectionString(options.ConnectionName!) ?? throw new InvalidOperationException($"Runtime artifacts EF connection '{options.ConnectionName}' was not found."); var fallback = cfg?.GetConnectionString(RuntimeArtifactEfModule.DefaultConnectionName); if (!string.IsNullOrWhiteSpace(fallback)) return fallback!; if (EfRelationalProviderBinding.Normalize(options.Provider) == "sqlite") return RuntimeArtifactEfModule.DefaultSqliteConnectionString; throw new InvalidOperationException("Runtime artifacts EF requires ConnectionString or ConnectionName for a non-Sqlite provider."); }
}
public sealed class RuntimeArtifactsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
