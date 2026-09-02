using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Targets;
using Elsa.Workflows.Runtime.Attention;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Groundwork.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Persistence.Groundwork.Runtime;

/// <summary>
/// Atomically replaces the complete workflow-runtime persistence family with public Groundwork v2 stores.
/// </summary>
public static class GroundworkV2RuntimeRegistration
{
    /// <summary>Key for the uncached provider store behind the optional executable cache.</summary>
    public const string WorkflowExecutableProviderKey = "Elsa.Persistence.Groundwork.V2.WorkflowExecutableProvider";

    /// <summary>Registers the complete runtime family with bounded executable caching enabled.</summary>
    public static IServiceCollection AddGroundworkV2RuntimeStores(
        this IServiceCollection services,
        string? targetName = null) =>
        services.AddGroundworkV2RuntimeStores(new WorkflowExecutableCacheOptions(), targetName);

    /// <summary>Registers the complete runtime family with explicit executable-cache settings.</summary>
    public static IServiceCollection AddGroundworkV2RuntimeStores(
        this IServiceCollection services,
        WorkflowExecutableCacheOptions workflowExecutableCacheOptions,
        string? targetName = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(workflowExecutableCacheOptions);
        var cacheOptions = CopyAndValidate(workflowExecutableCacheOptions);
        var target = BindRuntimeTarget(services, targetName);

        services.AddPersistenceCore();
        // Recovery cursors may outlive this process or be consumed by another node. Groundwork therefore refuses
        // the runtime core's development-only ephemeral signer unless the host supplies RuntimeRecoveryContinuationOptions.SigningKey.
        services.AddOptions<RuntimeRecoveryContinuationOptions>()
            .Configure(options => options.AllowEphemeralDevelopmentKey = false);
        // Register the durable protector here as well as in AddWorkflowRuntime so direct Groundwork composition
        // cannot accidentally construct a scanner without an authenticated continuation boundary. Resolution fails
        // closed until the host supplies a stable signing key through RuntimeRecoveryContinuationOptions.
        services.TryAddSingleton<IRuntimeRecoveryContinuationCodec, HmacRuntimeRecoveryContinuationCodec>();
        services.ClaimWorkflowTestScopeProvider(typeof(GroundworkV2WorkflowTestScopeStore));
        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
            services.AddGroundworkStorageUnit(unit, target);

        RegisterExecutableStore(services, cacheOptions, target);
        ReplaceScoped<GroundworkV2BookmarkStateStore>(services, Standard<GroundworkV2BookmarkStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IBookmarkStateStore), typeof(IBookmarkStimulusIndex));
        ReplaceScoped<GroundworkV2ExecutableActivityTemplateStore>(services, Standard<GroundworkV2ExecutableActivityTemplateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IExecutableActivityTemplateStore), typeof(IExecutableActivityTemplateReader), typeof(IExecutableActivityTemplateWriter));
        ReplaceScoped<GroundworkV2WorkflowExecutableSourceReferenceStore>(services, Standard<GroundworkV2WorkflowExecutableSourceReferenceStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowExecutableSourceReferenceStore), typeof(IWorkflowExecutableSourceReferenceReader), typeof(IWorkflowExecutableSourceReferenceWriter));
        ReplaceScoped<GroundworkV2ActivityExecutionStateStore>(services, Standard<GroundworkV2ActivityExecutionStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IActivityExecutionStateStore));
        ReplaceScoped<GroundworkV2ActivityExecutionInspectionStore>(services, Standard<GroundworkV2ActivityExecutionInspectionStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IActivityExecutionInspectionStore), typeof(IActivityExecutionInspectionWriter));
        ReplaceScoped<GroundworkV2ActivityExecutionHierarchyStore>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                provider.GetService<IActivityExecutionHierarchyCursorCodec>(),
                target),
            typeof(IActivityExecutionHierarchyStore), typeof(IActivityExecutionHierarchyReader), typeof(IActivityExecutionHierarchyWriter));
        ReplaceScoped<GroundworkV2WorkflowExecutionStateStore>(services, Standard<GroundworkV2WorkflowExecutionStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowExecutionStateStore));
        ReplaceScoped<GroundworkV2WorkflowAlterationStore>(services, Standard<GroundworkV2WorkflowAlterationStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowAlterationStore));
        ReplaceScoped<GroundworkV2WorkflowTestScopeStore>(services, Standard<GroundworkV2WorkflowTestScopeStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowTestScopeStore), typeof(IWorkflowTestScopeAdmissionStore));
        ReplaceScoped<GroundworkV2WorkflowTestScopeCleanupStore>(services, Standard<GroundworkV2WorkflowTestScopeCleanupStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowTestScopeCleanupStore));
        ReplaceScoped<GroundworkV2DurableValueStateStore>(services, Standard<GroundworkV2DurableValueStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IDurableValueStateStore));
        ReplaceScoped<GroundworkV2SchedulerStateStore>(services, Standard<GroundworkV2SchedulerStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(ISchedulerStateStore));
        ReplaceScoped<GroundworkV2ExecutionLivenessStateStore>(services, Standard<GroundworkV2ExecutionLivenessStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IExecutionLivenessStateStore));
        ReplaceScoped<GroundworkV2RuntimeRecoveryScanner>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                target,
                provider.GetRequiredService<IRuntimeRecoveryContinuationCodec>()),
            typeof(IRuntimeRecoveryScanner));
        ReplaceScoped<GroundworkV2WorkflowHoldStateStore>(services, Standard<GroundworkV2WorkflowHoldStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowHoldStateStore));
        ReplaceScoped<GroundworkV2IncidentStateStore>(services, Standard<GroundworkV2IncidentStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IIncidentStateStore));
        ReplaceScoped<GroundworkV2WorkflowRuntimeAttentionQuery>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                provider.GetService<TimeProvider>(),
                target),
            typeof(IWorkflowRuntimeAttentionQuery));
        ReplaceScoped<GroundworkV2WorkflowDispatchStore>(services, Standard<GroundworkV2WorkflowDispatchStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowDispatchStore), typeof(IWorkflowDispatchQueryStore), typeof(IWorkflowDispatchDeleteStore),
            typeof(IWorkflowDispatchRetentionRootStore), typeof(IWorkflowDispatchAdmissionStore), typeof(IWorkflowDispatchCancellationStore));
        ReplaceScoped<GroundworkV2RuntimeCheckpointWriter>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                target,
                provider.GetService<TimeProvider>(),
                provider.GetService<IWorkflowExecutableRootWriteLeaseManager>()),
            typeof(IRuntimeCheckpointCommitStore));
        ReplaceScoped<GroundworkV2RuntimePostCommitOutboxStore>(services, Standard<GroundworkV2RuntimePostCommitOutboxStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IRuntimePostCommitOutboxStore), typeof(IPostCommitOutboxLookupStore),
            typeof(IRuntimePostCommitOutboxClaimStore), typeof(IRuntimePostCommitOutboxClaimCompletionStore),
            typeof(IWorkflowDispatchRedriveStore));
        ReplaceScoped<GroundworkV2WorkflowSchedulerWorkQueue>(services, Standard<GroundworkV2WorkflowSchedulerWorkQueue>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowSchedulerWorkQueue), typeof(IWorkflowSchedulerWorkClaimInspection));
        ReplaceScoped<GroundworkV2WorkflowSchedulerPoisonStore>(services, Standard<GroundworkV2WorkflowSchedulerPoisonStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowSchedulerPoisonStore));
        ReplaceScoped<GroundworkV2DurableTimerStateStore>(services, Standard<GroundworkV2DurableTimerStateStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IDurableTimerStore));
        ReplaceScoped<GroundworkV2WorkflowTriggerBindingStore>(services, Standard<GroundworkV2WorkflowTriggerBindingStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IWorkflowTriggerBindingStore));
        ReplaceScoped<GroundworkV2RecurringTriggerScheduleStore>(services, Standard<GroundworkV2RecurringTriggerScheduleStore>(target, static (sessions, access, target) => new(sessions, access, target)),
            typeof(IRecurringTriggerScheduleStore));
        ReplaceScoped<GroundworkV2WorkflowActivationAuthority>(services, provider => new(
                provider.GetRequiredService<IGroundworkStorageSessionSource>(),
                provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
                provider.GetRequiredService<GroundworkStorageTransactionFactory>(),
                target), typeof(IWorkflowActivationAuthority));

        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkV2CheckpointDurabilityEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkV2DispatchStoreDurabilityEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkV2OutboxDurabilityEvidence>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IWorkflowDispatchDurabilityEvidence, GroundworkV2SchedulerDurabilityEvidence>());
        return services;
    }

    private static WorkflowExecutableCacheOptions CopyAndValidate(WorkflowExecutableCacheOptions options)
    {
        var copy = new WorkflowExecutableCacheOptions { Enabled = options.Enabled, Capacity = options.Capacity };
        copy.Validate();
        return copy;
    }

    private static string BindRuntimeTarget(IServiceCollection services, string? targetName)
    {
        var target = GroundworkTargetNames.Normalize(targetName);
        var existing = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkV2RuntimeTarget))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<GroundworkV2RuntimeTarget>()
            .SingleOrDefault();
        if (existing is not null && !StringComparer.Ordinal.Equals(existing.Name, target))
        {
            throw new InvalidOperationException(
                $"The Groundwork v2 runtime is already bound to '{existing.Name}' and cannot also bind to '{target}'. " +
                "Elsa's unkeyed runtime contracts can use only one physical target.");
        }

        if (existing is null)
            services.AddSingleton(new GroundworkV2RuntimeTarget(target));
        return target;
    }

    private static void RegisterExecutableStore(
        IServiceCollection services,
        WorkflowExecutableCacheOptions options,
        string? targetName)
    {
        services.RemoveAll<GroundworkV2WorkflowExecutableStore>();
        services.RemoveAll<IWorkflowExecutableStore>();
        RemoveKeyed<IWorkflowExecutableStore>(services, WorkflowExecutableProviderKey);
        services.RemoveAll<CachingWorkflowExecutableStore>();
        services.RemoveAll<InvalidatingWorkflowExecutableStore>();
        services.RemoveAll<WorkflowExecutableCache>();
        services.RemoveAll<GroundworkV2WorkflowExecutableCacheLoader>();
        services.RemoveAll<WorkflowExecutableCacheOptions>();
        services.AddSingleton(options);
        services.AddScoped(provider => new GroundworkV2WorkflowExecutableStore(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName,
            provider.GetService<ILogger<GroundworkV2WorkflowExecutableStore>>()));

        if (!options.Enabled)
        {
            services.AddScoped<IWorkflowExecutableStore>(provider =>
                provider.GetRequiredService<GroundworkV2WorkflowExecutableStore>());
            return;
        }

        services.AddKeyedScoped<IWorkflowExecutableStore>(WorkflowExecutableProviderKey, (provider, _) =>
            provider.GetRequiredService<GroundworkV2WorkflowExecutableStore>());
        services.AddSingleton<WorkflowExecutableCache>();
        services.AddSingleton<GroundworkV2WorkflowExecutableCacheLoader>();
        services.AddScoped<CachingWorkflowExecutableStore>(provider =>
        {
            var context = provider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
            if (context.AccessPolicy != PersistenceAccessPolicy.Ordinary || context.Scope is null)
                throw new InvalidOperationException("The workflow executable cache adapter requires an ordinary persistence scope.");
            var persistenceScope = context.Scope;
            var loader = provider.GetRequiredService<GroundworkV2WorkflowExecutableCacheLoader>();
            return new(
                provider.GetRequiredKeyedService<IWorkflowExecutableStore>(WorkflowExecutableProviderKey),
                provider.GetRequiredService<WorkflowExecutableCache>(),
                persistenceScope.Value,
                (artifactId, cancellationToken) => loader.LoadAsync(persistenceScope, artifactId, cancellationToken));
        });
        services.AddScoped<InvalidatingWorkflowExecutableStore>(provider =>
        {
            var context = provider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
            return new(
                provider.GetRequiredKeyedService<IWorkflowExecutableStore>(WorkflowExecutableProviderKey),
                provider.GetRequiredService<WorkflowExecutableCache>(),
                context.Scope?.Value);
        });
        services.AddScoped<IWorkflowExecutableStore>(provider =>
        {
            var context = provider.GetRequiredService<IPersistenceAccessContextAccessor>().Current;
            return context.AccessPolicy == PersistenceAccessPolicy.Ordinary && context.Scope is not null
                ? provider.GetRequiredService<CachingWorkflowExecutableStore>()
                : provider.GetRequiredService<InvalidatingWorkflowExecutableStore>();
        });
    }

    private static Func<IServiceProvider, TImplementation> Standard<TImplementation>(
        string? targetName,
        Func<IGroundworkStorageSessionSource, IPersistenceAccessContextAccessor, string?, TImplementation> factory)
        where TImplementation : class =>
        provider => factory(
            provider.GetRequiredService<IGroundworkStorageSessionSource>(),
            provider.GetRequiredService<IPersistenceAccessContextAccessor>(),
            targetName);

    private static void ReplaceScoped<TImplementation>(
        IServiceCollection services,
        Func<IServiceProvider, TImplementation> factory,
        params Type[] contracts)
        where TImplementation : class
    {
        services.RemoveAll<TImplementation>();
        services.AddScoped(factory);
        foreach (var contract in contracts)
        {
            services.RemoveAll(contract);
            services.AddScoped(contract, provider => provider.GetRequiredService<TImplementation>());
        }
    }

    private static void RemoveKeyed<TContract>(IServiceCollection services, object key)
    {
        for (var index = services.Count - 1; index >= 0; index--)
        {
            var descriptor = services[index];
            if (descriptor.ServiceType == typeof(TContract) && descriptor.IsKeyedService && Equals(descriptor.ServiceKey, key))
                services.RemoveAt(index);
        }
    }
}

internal sealed class GroundworkV2WorkflowExecutableCacheLoader(IPersistenceOperationScopeFactory operationScopeFactory)
{
    public async ValueTask<WorkflowExecutable?> LoadAsync(
        PersistenceScope persistenceScope,
        string artifactId,
        CancellationToken cancellationToken)
    {
        await using var operationScope = await operationScopeFactory.CreateAsync(
            persistenceScope,
            cancellationToken);
        var store = operationScope.ServiceProvider.GetRequiredKeyedService<IWorkflowExecutableStore>(
            GroundworkV2RuntimeRegistration.WorkflowExecutableProviderKey);
        return await store.FindAsync(artifactId, cancellationToken);
    }
}

internal sealed record GroundworkV2RuntimeTarget(string? Name);

internal sealed class GroundworkV2CheckpointDurabilityEvidence(
    IGroundworkStorageSessionSource sessions,
    GroundworkV2RuntimeTarget target) : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Checkpoint;

    public WorkflowDispatchDurabilityLevel Level =>
        sessions is IGroundworkStorageCapabilitySource capabilitySource &&
        capabilitySource.Capabilities(target.Name).Any(capability => capability.Id.Equals(WellKnownCapabilities.AtomicCommit))
            ? WorkflowDispatchDurabilityLevel.Durable
            : WorkflowDispatchDurabilityLevel.ProcessLocal;
}

internal sealed class GroundworkV2DispatchStoreDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.DispatchStore;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}

internal sealed class GroundworkV2OutboxDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Outbox;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}

internal sealed class GroundworkV2SchedulerDurabilityEvidence : IWorkflowDispatchDurabilityEvidence
{
    public string Component => WorkflowDispatchDurabilityComponents.Scheduler;
    public WorkflowDispatchDurabilityLevel Level => WorkflowDispatchDurabilityLevel.Durable;
}
