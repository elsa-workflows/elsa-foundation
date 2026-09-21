using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

public static class RuntimeWorkflowExecutionEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeWorkflowExecutionEntityFrameworkCore(this IServiceCollection services, RuntimeWorkflowExecutionEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services); ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existing = WorkflowExecutionStateStoreBackend.Find(services);
            if (existing?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
            {
                existing.EnsureOwnsRegisteredContract(services);
                var prior = services.Select(x => x.ImplementationInstance).OfType<RuntimeWorkflowExecutionEntityFrameworkCoreOptions>().SingleOrDefault();
                if (prior is null || !OptionsEqual(prior, options)) throw new InvalidOperationException("Runtime workflow execution EF persistence is already registered with different provider options.");
                RuntimeEfContextRegistration.EnsureContextIsAvailable(services, provider, "Runtime workflow executions", existing.Owns,
                    RuntimeArtifactStoreBackend.Find(services) is { } repeatArtifacts ? repeatArtifacts.Owns : null,
                    RuntimeActivityExecutionStoreBackend.Find(services) is { } repeatActivity ? repeatActivity.Owns : null,
                    BookmarkStateStoreBackend.Find(services) is { } repeatBookmarks ? repeatBookmarks.Owns : null,
                    RuntimeWorkflowAlterationStoreBackend.Find(services) is { } repeatAlterations ? repeatAlterations.Owns : null,
                    WorkflowTestScopeStoreBackend.Find(services) is { } repeatScopes ? repeatScopes.Owns : null);
                return services;
            }
            if (existing is not null) existing.EnsureOwnsRegisteredContract(services);
            else
            {
                var explicitStore = services.Where(x => x.ServiceType == typeof(IWorkflowExecutionStateStore)).ToArray();
                if (explicitStore.Any(x => x.ImplementationType != typeof(Elsa.Workflows.Runtime.Services.Executions.InMemoryWorkflowExecutionStateStore))) throw new InvalidOperationException("Runtime workflow execution EF persistence refuses to replace an unowned execution-state store.");
            }
            RuntimeEfContextRegistration.EnsureRecoveryContinuationSigningKeyCompatible(services, options.RecoveryContinuationSigningKey, "Runtime workflow executions");
            RuntimeEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName, options.Schema, options.Pooling);
            var commitExistingRemoval = existing?.PrepareRemoveOwnedArtifacts(services);
            var artifacts = RuntimeArtifactStoreBackend.Find(services); var activity = RuntimeActivityExecutionStoreBackend.Find(services); var bookmarks = BookmarkStateStoreBackend.Find(services); var alterations = RuntimeWorkflowAlterationStoreBackend.Find(services); var scopes = WorkflowTestScopeStoreBackend.Find(services); var operational = RuntimeOperationalStateStoreBackend.Find(services);
            if (artifacts?.Name == RuntimeArtifactStoreBackend.EntityFramework) artifacts.EnsureOwnsRegisteredContracts(services);
            if (activity?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework) activity.EnsureOwnsRegisteredContracts(services);
            if (bookmarks?.Name == BookmarkStateStoreBackend.EntityFramework) bookmarks.EnsureOwnsRegisteredAuxiliaryContracts(services);
            RuntimeEfContextRegistration.EnsureContextIsAvailable(services, provider, "Runtime workflow executions", artifacts?.Name == RuntimeArtifactStoreBackend.EntityFramework ? artifacts.Owns : null, activity?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework ? activity.Owns : null, bookmarks?.Name == BookmarkStateStoreBackend.EntityFramework ? bookmarks.Owns : null, alterations?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework ? alterations.Owns : null, scopes?.Name == WorkflowTestScopeStoreBackend.EntityFramework ? scopes.Owns : null, operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operational.Owns : null);
            var configured = new RuntimeWorkflowExecutionEntityFrameworkCoreOptions { Provider = options.Provider, ConnectionString = options.ConnectionString, ConnectionName = options.ConnectionName, Schema = options.Schema, Pooling = options.Pooling, RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey };
            services.AddOptions<RuntimeRecoveryContinuationOptions>().Configure(x => { if (!string.IsNullOrWhiteSpace(options.RecoveryContinuationSigningKey)) x.SigningKey = options.RecoveryContinuationSigningKey; x.AllowEphemeralDevelopmentKey = false; });
            services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());
            services.AddSingleton(configured);
            var owned = new List<ServiceDescriptor> { services.Last() };
            if (artifacts?.Name == RuntimeArtifactStoreBackend.EntityFramework) owned.AddRange(RuntimeEfContextRegistration.ContextRegistrations(services, provider).Where(artifacts.Owns));
            else if (activity?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework) owned.AddRange(RuntimeEfContextRegistration.ContextRegistrations(services, provider).Where(activity.Owns));
            else if (bookmarks?.Name == BookmarkStateStoreBackend.EntityFramework) owned.AddRange(RuntimeEfContextRegistration.ContextRegistrations(services, provider).Where(bookmarks.Owns));
            else if (alterations?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework) owned.AddRange(RuntimeEfContextRegistration.ContextRegistrations(services, provider).Where(alterations.Owns));
            else if (scopes?.Name == WorkflowTestScopeStoreBackend.EntityFramework) owned.AddRange(RuntimeEfContextRegistration.ContextRegistrations(services, provider).Where(scopes.Owns));
            else if (operational?.Name == RuntimeOperationalStateStoreBackend.EntityFramework) owned.AddRange(RuntimeEfContextRegistration.ContextRegistrations(services, provider).Where(operational.Owns));
            else owned.AddRange(RuntimeEfContextRegistration.AddContext(services, provider, configured.ConnectionString, configured.ConnectionName, configured.Schema, configured.Pooling));
            services.AddScoped<EfWorkflowExecutionStateStore>(); var concrete = services.Last(); owned.Add(concrete);
            services.RemoveAll<IWorkflowExecutionStateStore>();
            var contract = ServiceDescriptor.Scoped<IWorkflowExecutionStateStore>(p => p.GetRequiredService<EfWorkflowExecutionStateStore>()); services.Add(contract);
            WorkflowExecutionStateStoreBackend.Register(services, new WorkflowExecutionStateStoreBackend(WorkflowExecutionStateStoreBackend.EntityFramework, [contract, .. owned], collection => RemoveOwned(collection, owned)));
            commitExistingRemoval?.Invoke(services);
            return services;
        }
        catch { services.Clear(); foreach (var descriptor in snapshot) services.Add(descriptor); throw; }
    }

    private static bool OptionsEqual(RuntimeWorkflowExecutionEntityFrameworkCoreOptions a, RuntimeWorkflowExecutionEntityFrameworkCoreOptions b) => StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(a.Provider), EfRelationalProviderBinding.Normalize(b.Provider)) && a.ConnectionString == b.ConnectionString && a.ConnectionName == b.ConnectionName && a.RecoveryContinuationSigningKey == b.RecoveryContinuationSigningKey;
    private static void RemoveOwned(IServiceCollection services, IReadOnlyCollection<ServiceDescriptor> owned) { foreach (var descriptor in owned.Where(x => services.Contains(x) && RuntimeArtifactStoreBackend.Find(services)?.Owns(x) != true && RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(x) != true && BookmarkStateStoreBackend.Find(services)?.Owns(x) != true && RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(x) != true && WorkflowTestScopeStoreBackend.Find(services)?.Owns(x) != true && RuntimeOperationalStateStoreBackend.Find(services)?.Owns(x) != true)) services.Remove(descriptor); }
}

public sealed class RuntimeWorkflowExecutionEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Optional database schema for this module's tables and its own migrations history table. Falls back to
    /// <see cref="EfSchema.ConfigurationKey"/>, then to the provider's own default. Ignored on SQLite and refused
    /// on MySQL, where a schema is a database.
    /// </summary>
    public string? Schema { get; set; }

    /// <summary>Reuse contexts from a pool instead of constructing one per scope.</summary>
    public bool Pooling { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
}
