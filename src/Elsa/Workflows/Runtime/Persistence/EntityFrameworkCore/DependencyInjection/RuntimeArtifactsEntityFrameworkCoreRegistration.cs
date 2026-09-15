using Elsa.Persistence.EntityFramework;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
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
            var cacheOptions = CopyAndValidate(options.WorkflowExecutableCache);
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
                    !string.Equals(existing.ConnectionName, options.ConnectionName, StringComparison.Ordinal) ||
                    !CacheOptionsEqual(existing.WorkflowExecutableCache, cacheOptions))
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
            RuntimeEfCheckpointCompositionTransition.EnsureGroundworkCheckpointTransitionAllowed(services, "executable artifacts");
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
            BookmarkStateEfContextRegistration.EnsureCompatible(services, provider, options.ConnectionString, options.ConnectionName);
            var commitExistingBackendRemoval = existingBackend?.PrepareRemoveOwnedArtifacts(services);
            services.AddOptions<RuntimeRecoveryContinuationOptions>()
                .Configure(options => options.AllowEphemeralDevelopmentKey = false);
            services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, ValidateRuntimeRecoveryContinuationCodecStartupTask>());
            var configured = new RuntimeArtifactsEntityFrameworkCoreOptions { Provider = options.Provider, ConnectionString = options.ConnectionString, ConnectionName = options.ConnectionName, WorkflowExecutableCache = cacheOptions };
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
                ownedInfrastructure.AddRange(BookmarkStateEfContextRegistration.AddContext(services, provider, configured.ConnectionString, configured.ConnectionName));
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
            if (cacheOptions is null)
                services.AddScoped<IWorkflowExecutableStore>(p => p.GetRequiredService<EfWorkflowExecutableStore>());
            else
                ownedInfrastructure.AddRange(AddExecutableStore(services, cacheOptions));
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
                    .Distinct()
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

    /// <summary>
    /// Selects the executable-store surface. Enabled caching mirrors the Groundwork composition: ordinary scoped
    /// reads go through one bounded cache partitioned by persistence scope, and every other access policy reads the
    /// database directly while still invalidating cached entries it changes. The returned infrastructure is owned by
    /// the artifact backend together with the captured store surface.
    /// </summary>
    private static IReadOnlyCollection<ServiceDescriptor> AddExecutableStore(IServiceCollection services, WorkflowExecutableCacheOptions cacheOptions)
    {
        var start = services.Count;
        services.AddSingleton(cacheOptions);
        if (!cacheOptions.Enabled)
        {
            services.AddScoped<IWorkflowExecutableStore>(p => p.GetRequiredService<EfWorkflowExecutableStore>());
            return services.Skip(start).ToArray();
        }

        services.AddKeyedScoped<IWorkflowExecutableStore>(UncachedWorkflowExecutableStoreKey, (p, _) => p.GetRequiredService<EfWorkflowExecutableStore>());
        services.AddSingleton<WorkflowExecutableCache>();
        services.AddSingleton<EfWorkflowExecutableCacheLoader>();
        services.AddScoped(p =>
        {
            var context = p.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
            if (context.AccessPolicy != PersistenceAccessPolicy.Ordinary || context.Scope is not { } persistenceScope)
                throw new InvalidOperationException("The workflow executable cache adapter requires an ordinary persistence scope.");
            var loader = p.GetRequiredService<EfWorkflowExecutableCacheLoader>();
            return new CachingWorkflowExecutableStore(
                p.GetRequiredKeyedService<IWorkflowExecutableStore>(UncachedWorkflowExecutableStoreKey),
                p.GetRequiredService<WorkflowExecutableCache>(),
                persistenceScope.Value,
                (artifactId, cancellationToken) => loader.LoadAsync(persistenceScope, artifactId, cancellationToken));
        });
        services.AddScoped(p => new InvalidatingWorkflowExecutableStore(
            p.GetRequiredKeyedService<IWorkflowExecutableStore>(UncachedWorkflowExecutableStoreKey),
            p.GetRequiredService<WorkflowExecutableCache>(),
            p.GetRequiredService<IPersistenceAccessContextAccessor>().Current.Scope?.Value));
        services.AddScoped<IWorkflowExecutableStore>(p =>
        {
            var context = p.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
            return context.AccessPolicy == PersistenceAccessPolicy.Ordinary && context.Scope is not null
                ? p.GetRequiredService<CachingWorkflowExecutableStore>()
                : p.GetRequiredService<InvalidatingWorkflowExecutableStore>();
        });
        return services.Skip(start).ToArray();
    }

    private static WorkflowExecutableCacheOptions? CopyAndValidate(WorkflowExecutableCacheOptions? options)
    {
        if (options is null)
            return null;
        var copy = new WorkflowExecutableCacheOptions { Enabled = options.Enabled, Capacity = options.Capacity };
        copy.Validate();
        return copy;
    }

    private static bool CacheOptionsEqual(WorkflowExecutableCacheOptions? left, WorkflowExecutableCacheOptions? right) =>
        left is null ? right is null : right is not null && left.Enabled == right.Enabled && left.Capacity == right.Capacity;

    /// <summary>Key of the uncached EF store behind the optional executable cache.</summary>
    public const string UncachedWorkflowExecutableStoreKey = "Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.UncachedWorkflowExecutableStore";
}

/// <summary>
/// Loads a cache miss in its own operation scope bound to the requesting persistence scope: a shared in-flight
/// load can outlive the scope that started it, so it must not borrow that scope's context.
/// </summary>
internal sealed class EfWorkflowExecutableCacheLoader(IPersistenceOperationScopeFactory operationScopeFactory)
{
    public async ValueTask<WorkflowExecutable?> LoadAsync(PersistenceScope persistenceScope, string artifactId, CancellationToken cancellationToken)
    {
        await using var operationScope = await operationScopeFactory.CreateAsync(persistenceScope, cancellationToken);
        var store = operationScope.ServiceProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(
            RuntimeArtifactsEntityFrameworkCoreRegistration.UncachedWorkflowExecutableStoreKey);
        return await store.FindAsync(artifactId, cancellationToken);
    }
}

public sealed class RuntimeArtifactsEntityFrameworkCoreOptions
{
    public string Provider { get; set; } = "Sqlite";
    public string? ConnectionString { get; set; }
    public string? ConnectionName { get; set; }

    /// <summary>
    /// Bounded shell-local cache of immutable workflow executables, isolated by persistence scope. Null (this
    /// participant's default) registers no cache infrastructure and reads executables straight from the database;
    /// the Runtime EF aggregate always supplies it, as the Groundwork runtime composition does.
    /// </summary>
    public WorkflowExecutableCacheOptions? WorkflowExecutableCache { get; set; }
}
