using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;

public static class RuntimeBookmarksEntityFrameworkCoreRegistration
{
    public static IServiceCollection AddRuntimeBookmarksEntityFrameworkCore(
        this IServiceCollection services,
        RuntimeBookmarksEntityFrameworkCoreOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        var snapshot = services.ToArray();
        try
        {
            var provider = EfRelationalProviderBinding.Normalize(options.Provider);
            _ = EfRelationalProviderBinding.ExpectedProviderName(options.Provider);
            var configured = new RuntimeBookmarksEntityFrameworkCoreOptions
            {
                Provider = options.Provider,
                ConnectionString = options.ConnectionString,
                ConnectionName = options.ConnectionName
            };
            BookmarkStateEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName);

            var existingBackend = BookmarkStateStoreBackend.Find(services);
            existingBackend?.EnsureOwnsRegisteredContract(services);
            if (existingBackend is not null && existingBackend.Name == BookmarkStateStoreBackend.EntityFramework)
            {
                existingBackend.EnsureOwnsRegisteredAuxiliaryContracts(services);
                var siblingArtifactsBackend = RuntimeArtifactStoreBackend.Find(services);
                if (siblingArtifactsBackend?.Name == RuntimeArtifactStoreBackend.EntityFramework)
                    siblingArtifactsBackend.EnsureOwnsRegisteredContracts(services);
                var siblingActivityBackend = RuntimeActivityExecutionStoreBackend.Find(services);
                if (siblingActivityBackend?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework)
                    siblingActivityBackend.EnsureOwnsRegisteredContracts(services);
                var siblingWorkflowBackend = WorkflowExecutionStateStoreBackend.Find(services);
                if (siblingWorkflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
                    siblingWorkflowBackend.EnsureOwnsRegisteredContract(services);
                var siblingAlterationBackend = RuntimeWorkflowAlterationStoreBackend.Find(services);
                if (siblingAlterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework)
                    siblingAlterationBackend.EnsureOwnsRegisteredContracts(services);
                var siblingScopeBackend = WorkflowTestScopeStoreBackend.Find(services);
                if (siblingScopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework)
                    siblingScopeBackend.EnsureOwnsRegisteredContracts(services);
                var siblingOperationalBackend = RuntimeOperationalStateStoreBackend.Find(services);
                var existingOptions = services
                    .Select(descriptor => descriptor.ImplementationInstance)
                    .OfType<RuntimeBookmarksEntityFrameworkCoreOptions>()
                    .SingleOrDefault();
                if (existingOptions is null ||
                    !string.Equals(EfRelationalProviderBinding.Normalize(existingOptions.Provider), provider, StringComparison.Ordinal) ||
                    !string.Equals(existingOptions.ConnectionString, options.ConnectionString, StringComparison.Ordinal) ||
                    !string.Equals(existingOptions.ConnectionName, options.ConnectionName, StringComparison.Ordinal))
                    throw new InvalidOperationException("Runtime bookmarks EF persistence is already registered with different provider options.");
                BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                    services,
                    provider,
                    "Runtime bookmarks",
                    existingBackend.Owns,
                    siblingArtifactsBackend?.Name == RuntimeArtifactStoreBackend.EntityFramework
                        ? siblingArtifactsBackend.Owns
                        : null,
                    siblingActivityBackend?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework
                        ? siblingActivityBackend.Owns
                        : null,
                    siblingWorkflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework
                        ? siblingWorkflowBackend.Owns
                        : null,
                    siblingAlterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework
                        ? siblingAlterationBackend.Owns
                        : null,
                    siblingScopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework
                        ? siblingScopeBackend.Owns
                        : null,
                    siblingOperationalBackend?.Name == RuntimeOperationalStateStoreBackend.EntityFramework
                        ? siblingOperationalBackend.Owns
                        : null);
                return services;
            }

            var artifactsBackend = RuntimeArtifactStoreBackend.Find(services);
            var artifactsOwnContext = artifactsBackend?.Name == RuntimeArtifactStoreBackend.EntityFramework;
            if (artifactsOwnContext)
                artifactsBackend!.EnsureOwnsRegisteredContracts(services);
            var activityBackend = RuntimeActivityExecutionStoreBackend.Find(services);
            var activityOwnContext = activityBackend?.Name == RuntimeActivityExecutionStoreBackend.EntityFramework;
            if (activityOwnContext)
                activityBackend!.EnsureOwnsRegisteredContracts(services);
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
            BookmarkStateEfContextRegistration.EnsureContextIsAvailable(
                services,
                provider,
                "Runtime bookmarks",
                artifactsOwnContext ? artifactsBackend!.Owns : null,
                activityOwnContext ? activityBackend!.Owns : null,
                workflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework
                    ? workflowBackend.Owns
                    : null,
                alterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework
                    ? alterationBackend.Owns
                    : null,
                scopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework
                    ? scopeBackend.Owns
                    : null,
                operationalBackend?.Name == RuntimeOperationalStateStoreBackend.EntityFramework
                    ? operationalBackend.Owns
                    : null);

            if (existingBackend is null && BookmarkStateStoreBackend.HasRegisteredContract(services))
            {
                BookmarkStateStoreBackend.EnsureRuntimeDefaultsOwnRegisteredContracts(services);
                BookmarkStateStoreBackend.RemoveDefaultStimulusIndex(services);
                BookmarkStateStoreBackend.RemoveDefaultStateStore(services);
            }
            var commitExistingBackendRemoval = existingBackend?.PrepareRemoveOwnedArtifacts(services);

            var ownedArtifacts = new List<ServiceDescriptor>();
            var optionsDescriptor = ServiceDescriptor.Singleton(configured);
            services.Add(optionsDescriptor);
            ownedArtifacts.Add(optionsDescriptor);
            if (artifactsOwnContext)
                ownedArtifacts.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(artifactsBackend!.Owns));
            if (activityOwnContext)
                ownedArtifacts.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(activityBackend!.Owns));
            if (!artifactsOwnContext && !activityOwnContext && workflowBackend?.Name == WorkflowExecutionStateStoreBackend.EntityFramework)
                ownedArtifacts.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(workflowBackend.Owns));
            if (!artifactsOwnContext && !activityOwnContext && workflowBackend?.Name != WorkflowExecutionStateStoreBackend.EntityFramework && alterationBackend?.Name == RuntimeWorkflowAlterationStoreBackend.EntityFramework)
                ownedArtifacts.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(alterationBackend.Owns));
            if (!artifactsOwnContext && !activityOwnContext && workflowBackend?.Name != WorkflowExecutionStateStoreBackend.EntityFramework && alterationBackend?.Name != RuntimeWorkflowAlterationStoreBackend.EntityFramework && scopeBackend?.Name == WorkflowTestScopeStoreBackend.EntityFramework)
                ownedArtifacts.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                    .Where(scopeBackend.Owns));
            if (!artifactsOwnContext && !activityOwnContext && workflowBackend?.Name != WorkflowExecutionStateStoreBackend.EntityFramework && alterationBackend?.Name != RuntimeWorkflowAlterationStoreBackend.EntityFramework && scopeBackend?.Name != WorkflowTestScopeStoreBackend.EntityFramework)
            {
                if (operationalBackend?.Name == RuntimeOperationalStateStoreBackend.EntityFramework)
                    ownedArtifacts.AddRange(BookmarkStateEfContextRegistration.ContextRegistrations(services, provider)
                        .Where(operationalBackend.Owns));
                else
                    ownedArtifacts.AddRange(BookmarkStateEfContextRegistration.AddContext(services, provider, configured.ConnectionString, configured.ConnectionName));
            }

            var descriptorState = ServiceDescriptor.Scoped<IBookmarkStateStore>(provider => provider.GetRequiredService<EfBookmarkStateStore>());
            var descriptorIndex = ServiceDescriptor.Scoped<IBookmarkStimulusIndex>(provider => provider.GetRequiredService<EfBookmarkStateStore>());
            var storeDescriptor = ServiceDescriptor.Scoped<EfBookmarkStateStore, EfBookmarkStateStore>();
            services.Add(descriptorState);
            services.Add(storeDescriptor);
            ownedArtifacts.Add(storeDescriptor);
            services.Add(descriptorIndex);
            BookmarkStateStoreBackend.Register(services, new BookmarkStateStoreBackend(
                BookmarkStateStoreBackend.EntityFramework,
                descriptorState,
                descriptorIndex,
                collection => RemoveEfArtifacts(collection, ownedArtifacts),
                ownedArtifacts));
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

    private static void RemoveEfArtifacts(IServiceCollection services, IReadOnlyCollection<ServiceDescriptor> ownedArtifacts)
    {
        if (ownedArtifacts.Any(descriptor => services.Count(candidate => ReferenceEquals(candidate, descriptor)) != 1))
            throw new InvalidOperationException("Runtime bookmarks EF persistence no longer exclusively owns its auxiliary registrations.");

        foreach (var descriptor in ownedArtifacts.Where(descriptor =>
                     RuntimeArtifactStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeActivityExecutionStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowExecutionStateStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeWorkflowAlterationStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     WorkflowTestScopeStoreBackend.Find(services)?.Owns(descriptor) != true &&
                     RuntimeOperationalStateStoreBackend.Find(services)?.Owns(descriptor) != true))
        {
            // Artifact EF may reuse this context and records the same descriptor as a sibling owner.
            // Keep it alive while replacing only the bookmark backend; the artifact backend remains valid.
            services.Remove(descriptor);
        }
    }
}

public sealed class RuntimeBookmarksEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }
}
