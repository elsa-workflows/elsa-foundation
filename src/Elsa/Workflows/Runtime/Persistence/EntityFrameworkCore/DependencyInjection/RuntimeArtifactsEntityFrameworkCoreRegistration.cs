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
                var siblingActivityBackendForRepeat = RuntimeActivityExecutionStoreBackend.Find(services);
                if (siblingActivityBackendForRepeat?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework)
                    siblingActivityBackendForRepeat.EnsureOwnsRegisteredContracts(services);
                var siblingWorkflowBackendForRepeat = WorkflowExecutionStateStoreBackend.Find(services);
                if (siblingWorkflowBackendForRepeat?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
                    siblingWorkflowBackendForRepeat.EnsureOwnsRegisteredContract(services);
                var siblingAlterationBackendForRepeat = RuntimeWorkflowAlterationStoreBackend.Find(services);
                if (siblingAlterationBackendForRepeat?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework)
                    siblingAlterationBackendForRepeat.EnsureOwnsRegisteredContracts(services);
                var siblingScopeBackendForRepeat = WorkflowTestScopeStoreBackend.Find(services);
                if (siblingScopeBackendForRepeat?.Name == WorkflowTestScopeStoreBackend.EntityFramework)
                    siblingScopeBackendForRepeat.EnsureOwnsRegisteredContracts(services);
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
                        : null,
                    siblingActivityBackendForRepeat?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework
                        ? siblingActivityBackendForRepeat.Owns
                        : null,
                    siblingWorkflowBackendForRepeat?.Name == WorkflowExecutionStateStoreBackend.EntityFramework
                        ? siblingWorkflowBackendForRepeat.Owns
                        : null,
                    siblingAlterationBackendForRepeat?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework
                        ? siblingAlterationBackendForRepeat.Owns
                        : null,
                    siblingScopeBackendForRepeat?.Name == WorkflowTestScopeStoreBackend.EntityFramework
                        ? siblingScopeBackendForRepeat.Owns
                        : null,
                    RuntimeOperationalStateStoreBackend.Find(services) is { Name: RuntimeOperationalStateStoreBackend.EntityFramework } siblingOperationalBackend
                        ? siblingOperationalBackend.Owns
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
            var existingActivityBackend = RuntimeActivityExecutionStoreBackend.Find(services);
            if (existingActivityBackend?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework)
                existingActivityBackend.EnsureOwnsRegisteredContracts(services);
            var existingWorkflowBackend = WorkflowExecutionStateStoreBackend.Find(services);
            if (existingWorkflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
                existingWorkflowBackend.EnsureOwnsRegisteredContract(services);
            var existingAlterationBackend = RuntimeWorkflowAlterationStoreBackend.Find(services);
            if (existingAlterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework)
                existingAlterationBackend.EnsureOwnsRegisteredContracts(services);
            var existingScopeBackend = WorkflowTestScopeStoreBackend.Find(services);
            if (existingScopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework)
                existingScopeBackend.EnsureOwnsRegisteredContracts(services);
            var existingOperationalBackend = RuntimeOperationalStateStoreBackend.Find(services);
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
            var activityBackend = existingActivityBackend;
            var activityOwnContext = activityBackend?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework;
            var alterationBackend = existingAlterationBackend;
            var alterationOwnContext = alterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework;
            var scopeBackend = existingScopeBackend;
            var scopeOwnContext = scopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework;
            var operationalBackend = existingOperationalBackend;
            var operationalOwnContext = operationalBackend?.Name == RuntimeOperationalStateStoreBackend.EntityFramework;
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime artifacts",
                bookmarksOwnContext ? bookmarksBackend!.Owns : null,
                activityOwnContext ? activityBackend!.Owns : null,
                existingWorkflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework
                    ? existingWorkflowBackend.Owns
                    : null,
                existingAlterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework
                    ? existingAlterationBackend.Owns
                    : null,
                existingScopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework
                    ? existingScopeBackend.Owns
                    : null,
                operationalOwnContext ? operationalBackend!.Owns : null);

            if (bookmarksOwnContext)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(bookmarksBackend!.Owns));
            else if (activityOwnContext)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(activityBackend!.Owns));
            else if (existingWorkflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(existingWorkflowBackend.Owns));
            else if (alterationOwnContext)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(alterationBackend!.Owns));
            else if (scopeOwnContext)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(scopeBackend!.Owns));
            else if (operationalOwnContext)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(operationalBackend!.Owns));
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
