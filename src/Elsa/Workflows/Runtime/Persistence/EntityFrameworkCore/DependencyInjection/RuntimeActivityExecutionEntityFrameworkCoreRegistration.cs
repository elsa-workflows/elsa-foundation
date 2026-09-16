using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

public static class RuntimeActivityExecutionEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeActivityExecutionEntityFrameworkCore(this IServiceCollection services, RuntimeActivityExecutionEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var existingBackend = RuntimeActivityExecutionStoreBackend.Find(services);
            if (existingBackend?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework)
            {
                existingBackend.EnsureOwnsRegisteredContracts(services);
                var existingOptions = services.Select(descriptor => descriptor.ImplementationInstance)
                    .OfType<RuntimeActivityExecutionEntityFrameworkCoreOptions>().SingleOrDefault();
                if (existingOptions is null || !OptionsEqual(existingOptions, options))
                    throw new InvalidOperationException("Runtime activity execution EF persistence is already registered with different provider options.");
                EnsureContext(services, provider, existingBackend, "Runtime activity executions");
                return services;
            }

            if (existingBackend is not null)
                existingBackend.EnsureOwnsRegisteredContracts(services);
            else
                RuntimeActivityExecutionStoreBackend.EnsureNoUnownedRegistrations(services);

            var bookmarksBackend = BookmarkStateStoreBackend.Find(services);
            if (bookmarksBackend?.Name == BookmarkStateStoreBackend.EntityFramework)
                bookmarksBackend.EnsureOwnsRegisteredContract(services);
            var artifactsBackend = RuntimeArtifactStoreBackend.Find(services);
            if (artifactsBackend?.Name == RuntimeArtifactStoreBackend.EntityFramework)
                artifactsBackend.EnsureOwnsRegisteredContracts(services);
            var workflowBackend = WorkflowExecutionStateStoreBackend.Find(services);
            if (workflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
                workflowBackend.EnsureOwnsRegisteredContract(services);
            var alterationBackend = RuntimeWorkflowAlterationStoreBackend.Find(services);
            if (alterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework)
                alterationBackend.EnsureOwnsRegisteredContracts(services);
            var scopeBackend = WorkflowTestScopeStoreBackend.Find(services);
            if (scopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework)
                scopeBackend.EnsureOwnsRegisteredContracts(services);
            var operationalBackend = RuntimeOperationalStateStoreBackend.Find(services);

            BookmarkStateEfContextRegistration.EnsureRecoveryContinuationSigningKeyCompatible(services, options.RecoveryContinuationSigningKey, "Runtime activity executions");
            BookmarkStateEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName);
            EnsureContext(services, provider, bookmarksBackend?.Name == BookmarkStateStoreBackend.EntityFramework ? bookmarksBackend.Owns : null,
                artifactsBackend?.Name == RuntimeArtifactStoreBackend.EntityFramework ? artifactsBackend.Owns : null,
                workflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework ? workflowBackend.Owns : null,
                alterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework ? alterationBackend.Owns : null,
                scopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework ? scopeBackend.Owns : null,
                operationalBackend?.Name == RuntimeOperationalStateStoreBackend.EntityFramework ? operationalBackend.Owns : null,
                "Runtime activity executions");
            var commitExistingRemoval = existingBackend?.PrepareRemoveOwnedArtifacts(services);

            services.AddOptions<ActivityExecutionHierarchyCursorOptions>();
            services.Configure<ActivityExecutionHierarchyCursorOptions>(configuredOptions =>
            {
                if (!string.IsNullOrWhiteSpace(options.HierarchyCursorSigningKey))
                    configuredOptions.SigningKey = options.HierarchyCursorSigningKey;
                configuredOptions.AllowEphemeralDevelopmentKey = false;
            });
            services.TryAddSingleton<IActivityExecutionHierarchyCursorCodec, HmacActivityExecutionHierarchyCursorCodec>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateActivityExecutionHierarchyCursorCodecStartupTask>());
            services.AddOptions<RuntimeRecoveryContinuationOptions>()
                .Configure(configuredOptions =>
                {
                    if (!string.IsNullOrWhiteSpace(options.RecoveryContinuationSigningKey))
                        configuredOptions.SigningKey = options.RecoveryContinuationSigningKey;
                    configuredOptions.AllowEphemeralDevelopmentKey = false;
                });
            services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());
            var configured = new RuntimeActivityExecutionEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName,
                HierarchyCursorSigningKey = options.HierarchyCursorSigningKey,
                RecoveryContinuationSigningKey = options.RecoveryContinuationSigningKey
            };
            var optionsDescriptor = ServiceDescriptor.Singleton(configured);
            services.Add(optionsDescriptor);
            var ownedInfrastructure = new List<ServiceDescriptor> { optionsDescriptor };
            var ownsBookmarkContext = bookmarksBackend?.Name == BookmarkStateStoreBackend.EntityFramework;
            var ownsArtifactContext = artifactsBackend?.Name == RuntimeArtifactStoreBackend.EntityFramework;
            if (ownsBookmarkContext)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(bookmarksBackend!.Owns));
            else if (ownsArtifactContext)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(artifactsBackend!.Owns));
            else if (workflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(workflowBackend.Owns));
            else if (alterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(alterationBackend.Owns));
            else if (scopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(scopeBackend.Owns));
            else if (operationalBackend?.Name == RuntimeOperationalStateStoreBackend.EntityFramework)
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider).Where(operationalBackend.Owns));
            else
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.AddContext(services, provider, configured.ConnectionString, configured.ConnectionName));

            services.RemoveAll<EfActivityExecutionStateStore>();
            services.RemoveAll<EfActivityExecutionInspectionStore>();
            services.RemoveAll<EfActivityExecutionHierarchyStore>();
            services.AddScoped<EfActivityExecutionStateStore>();
            services.AddScoped<EfActivityExecutionInspectionStore>();
            services.AddScoped<EfActivityExecutionHierarchyStore>();
            var stateConcrete = services[^3];
            var inspectionConcrete = services[^2];
            var hierarchyConcrete = services[^1];
            ownedInfrastructure.Add(stateConcrete);
            ownedInfrastructure.Add(inspectionConcrete);
            ownedInfrastructure.Add(hierarchyConcrete);

            services.RemoveAll<IActivityExecutionStateStore>();
            services.RemoveAll<IActivityExecutionInspectionStore>();
            services.RemoveAll<IActivityExecutionInspectionWriter>();
            services.RemoveAll<IActivityExecutionHierarchyStore>();
            services.RemoveAll<IActivityExecutionHierarchyReader>();
            services.RemoveAll<IActivityExecutionHierarchyWriter>();
            var state = ServiceDescriptor.Scoped<IActivityExecutionStateStore>(p => p.GetRequiredService<EfActivityExecutionStateStore>());
            var inspection = ServiceDescriptor.Scoped<IActivityExecutionInspectionStore>(p => p.GetRequiredService<EfActivityExecutionInspectionStore>());
            var inspectionWriter = ServiceDescriptor.Scoped<IActivityExecutionInspectionWriter>(p => p.GetRequiredService<EfActivityExecutionInspectionStore>());
            var hierarchy = ServiceDescriptor.Scoped<IActivityExecutionHierarchyStore>(p => p.GetRequiredService<EfActivityExecutionHierarchyStore>());
            var hierarchyReader = ServiceDescriptor.Scoped<IActivityExecutionHierarchyReader>(p => p.GetRequiredService<EfActivityExecutionHierarchyStore>());
            var hierarchyWriter = ServiceDescriptor.Scoped<IActivityExecutionHierarchyWriter>(p => p.GetRequiredService<EfActivityExecutionHierarchyStore>());
            services.Add(state);
            services.Add(inspection);
            services.Add(inspectionWriter);
            services.Add(hierarchy);
            services.Add(hierarchyReader);
            services.Add(hierarchyWriter);
            var ownedSurface = RuntimeActivityExecutionStoreBackend.CaptureSurfaceRegistrations(services);
            RuntimeActivityExecutionStoreBackend.Register(services, new RuntimeActivityExecutionStoreBackend(
                RuntimeActivityExecutionStoreBackend.EntityFramework,
                ownedSurface.Concat(ownedInfrastructure).Distinct().ToArray(),
                collection => RemoveOwnedArtifacts(collection, ownedInfrastructure)));
            commitExistingRemoval?.Invoke(services);
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

    public static IServiceCollection AddRuntimeActivityExecutionStateEntityFrameworkCore(this IServiceCollection services, RuntimeActivityExecutionEntityFrameworkCoreOptions options) => services.AddRuntimeActivityExecutionEntityFrameworkCore(options);
    public static IServiceCollection AddRuntimeActivityExecutionsEntityFrameworkCore(this IServiceCollection services, RuntimeActivityExecutionEntityFrameworkCoreOptions options) => services.AddRuntimeActivityExecutionEntityFrameworkCore(options);

    private static bool OptionsEqual(RuntimeActivityExecutionEntityFrameworkCoreOptions left, RuntimeActivityExecutionEntityFrameworkCoreOptions right) =>
        StringComparer.Ordinal.Equals(EfRelationalProviderBinding.Normalize(left.Provider), EfRelationalProviderBinding.Normalize(right.Provider)) &&
        StringComparer.Ordinal.Equals(left.ConnectionString, right.ConnectionString) &&
        StringComparer.Ordinal.Equals(left.ConnectionName, right.ConnectionName) &&
        StringComparer.Ordinal.Equals(left.HierarchyCursorSigningKey, right.HierarchyCursorSigningKey) &&
        StringComparer.Ordinal.Equals(left.RecoveryContinuationSigningKey, right.RecoveryContinuationSigningKey);

    private static void EnsureContext(IServiceCollection services, string provider, RuntimeActivityExecutionStoreBackend backend, string owner)
    {
        var bookmarks = BookmarkStateStoreBackend.Find(services);
        var artifacts = RuntimeArtifactStoreBackend.Find(services);
        var workflow = WorkflowExecutionStateStoreBackend.Find(services);
        var alteration = RuntimeWorkflowAlterationStoreBackend.Find(services);
        var scope = WorkflowTestScopeStoreBackend.Find(services);
        var operational = RuntimeOperationalStateStoreBackend.Find(services);
        Func<ServiceDescriptor, bool>? bookmarksOwner = bookmarks is null ? null : bookmarks.Owns;
        Func<ServiceDescriptor, bool>? artifactsOwner = artifacts is null ? null : artifacts.Owns;
        Func<ServiceDescriptor, bool>? workflowOwner = workflow is null ? null : workflow.Owns;
        Func<ServiceDescriptor, bool>? alterationOwner = alteration is null ? null : alteration.Owns;
        Func<ServiceDescriptor, bool>? scopeOwner = scope is null ? null : scope.Owns;
        Func<ServiceDescriptor, bool>? operationalOwner = operational is null ? null : operational.Owns;
        BookmarkStateEfContextRegistration.EnsureContextIsAvailable(services, provider, owner, backend.Owns, bookmarksOwner, artifactsOwner, workflowOwner, alterationOwner, scopeOwner, operationalOwner);
    }

    private static void EnsureContext(IServiceCollection services, string provider, Func<ServiceDescriptor, bool>? bookmarksOwner, Func<ServiceDescriptor, bool>? artifactsOwner, Func<ServiceDescriptor, bool>? workflowOwner, Func<ServiceDescriptor, bool>? alterationOwner, Func<ServiceDescriptor, bool>? scopeOwner, Func<ServiceDescriptor, bool>? operationalOwner, string owner) =>
        BookmarkStateEfContextRegistration.EnsureContextIsAvailable(services, provider, owner, bookmarksOwner, artifactsOwner, workflowOwner, alterationOwner, scopeOwner, operationalOwner);

    private static void RemoveOwnedArtifacts(IServiceCollection services, IReadOnlyCollection<ServiceDescriptor> ownedInfrastructure)
    {
        if (ownedInfrastructure.Any(descriptor => !services.Contains(descriptor)))
            throw new InvalidOperationException("Runtime activity execution EF persistence no longer exclusively owns its infrastructure registrations.");
        foreach (var descriptor in ownedInfrastructure.Where(descriptor =>
                     BookmarkStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeOperationalStateStoreBackend.Find(services)?.Owns(descriptor) != true))
            services.Remove(descriptor);
    }
}

public sealed class RuntimeActivityExecutionEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
    public string? HierarchyCursorSigningKey { get; set; }
    public string? RecoveryContinuationSigningKey { get; set; }
}
