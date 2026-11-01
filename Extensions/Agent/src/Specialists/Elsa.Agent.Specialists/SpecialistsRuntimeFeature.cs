using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Agent.Specialists.Core.DependencyInjection;
using Elsa.Agent.Specialists.Core.Contracts;
using CShells.Features;
using Elsa.Agent.Specialists.JsonSchema;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Agent.Specialists.Activities;
using Elsa.Agent.Specialists.Core.Models;
using Elsa.Agent.Specialists.Journal;
using Elsa.Agent.Specialists.Services;
using Elsa.Serialization.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Specialists;

/// <summary>Activates pinned native specialist boundaries and their background model-turn workers.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "SpecialistsRuntime", DisplayName = "Specialists (Runtime)",
    Description = "Executes published specialists through Microsoft Agent Framework. Requires an explicitly configured turn journal and model provider.",
    DependsOn = new object[] { "BackgroundActivities", "Secrets", "Serialization" })]
public class SpecialistsRuntimeFeature : IShellFeature
{
    /// <summary>Allows new logical operations. Disabling keeps matching admitted work, readers and settlement available.</summary>
    public bool AllowNewOperations { get; set; } = true;

    /// <summary>Protected content retention after native workflow completion; active work remains pinned. Defaults to thirty days.</summary>
    public TimeSpan ContentRetention { get; set; } = TimeSpan.FromDays(30);

    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (ContentRetention <= TimeSpan.Zero || ContentRetention > TimeSpan.FromDays(3650)) throw new ArgumentOutOfRangeException(nameof(ContentRetention));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new SpecialistRetentionPolicy(ContentRetention));
        services.TryAddScoped<SpecialistRetentionPruner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringTask, SpecialistRetentionPumpTask>());
        services.TryAddSingleton(new SpecialistAdmissionPolicy(AllowNewOperations));
        services.TryAddScoped<SpecialistSchemaValidator>();
        services.TryAddScoped<SpecialistNativeBinding>();
        services.TryAddScoped<SpecialistTurnJournal>();
        services.TryAddScoped<SpecialistModelRunner>();
        services.TryAddScoped<SpecialistTurnBudgetAllocator>();
        services.TryAddScoped<SpecialistTurnLeaseRenewer>();
        services.TryAddScoped<SpecialistSessionCodec>();
        services.TryAddScoped<SpecialistSettlementBoundary>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, SpecialistCheckpointEnricher>());
        services.AddRuntimePostCommitIntentHandler<SpecialistSettlementHandler>(SpecialistSettlementBoundary.AppliedKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)), requiresDurableSource: true);
        services.AddRuntimePostCommitIntentHandler<SpecialistSettlementHandler>(SpecialistSettlementBoundary.CancelKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)), requiresDurableSource: true);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityActivationStrategy, SpecialistActivationStrategy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeActivityConsumerCapability, SpecialistConsumerCapability>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, SpecialistTypesStartupTask>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask>(new SpecialistJournalRegistrationStartupTask(services)));
    }
}

internal sealed class SpecialistJournalRegistrationStartupTask(IServiceCollection services) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (services.Count(descriptor => descriptor.ServiceType == typeof(ISpecialistTurnStore)) != 1)
            throw new InvalidOperationException("SpecialistsRuntime requires exactly one ISpecialistTurnStore backend per shell.");
        return Task.CompletedTask;
    }
}

/// <summary>Explicit development/test storage. It cannot continue turns after a host restart.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "SpecialistsInMemoryJournal", DisplayName = "Specialists: In-memory Journal",
    Description = "Development and test only. Retains at most 10,000 turns for this host lifetime; missing turns are refused after restart.",
    DependsOn = new object[] { "SpecialistsRuntime" })]
public class SpecialistsInMemoryJournalFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPersistenceCore();
        services.TryAddSingleton<InMemorySpecialistTurnState>();
        services.TryAddSpecialistTurnStore<InMemorySpecialistTurnStore>(ServiceLifetime.Scoped);
    }
}

internal sealed class SpecialistActivationStrategy(SpecialistNativeBinding binding, SpecialistTurnJournal journal, SpecialistModelRunner runner, TimeProvider time, SpecialistTurnBudgetAllocator budgets, SpecialistTurnLeaseRenewer leases) : IActivityActivationStrategy
{
    public string ConsumerKey => SpecialistRuntimeIds.ConsumerKey;
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [SpecialistRuntimeIds.DescriptorSchema, SpecialistRuntimeIds.ContinuityDescriptorSchema];
    public bool RequiresInputHydration => false;
    public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationStrategyRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Descriptor.ConsumerKey != ConsumerKey || !SupportedSchemaVersions.Contains(request.Descriptor.SchemaVersion) || request.Contract.DescriptorKind != ConsumerKey)
            throw new InvalidOperationException("The specialist descriptor consumer or schema is unsupported.");
        IActivity activity = request.Contract.Activation.Capability switch
        {
            SpecialistRuntimeIds.FacadeCapability => new SpecialistActivity(binding, journal, runner),
            SpecialistRuntimeIds.TurnCapability when request.Contract.SupportsBackgroundExecution && request.Contract.Inputs.Count == 0 => new SpecialistModelTurn(binding, journal, runner, time, budgets, leases),
            _ => throw new InvalidOperationException("The specialist activation capability is unsupported.")
        };
        return ValueTask.FromResult(new ActivityActivationLease(activity));
    }
}

internal sealed class SpecialistConsumerCapability : IRuntimeActivityConsumerCapability
{
    public string ConsumerKey => SpecialistRuntimeIds.ConsumerKey;
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [SpecialistRuntimeIds.DescriptorSchema, SpecialistRuntimeIds.ContinuityDescriptorSchema];
}

internal sealed class SpecialistTypesStartupTask(IWellKnownTypeRegistry types) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        types.RegisterType(typeof(SpecialistActivity), SpecialistRuntimeIds.FacadeCapability);
        types.RegisterType(typeof(SpecialistModelTurn), SpecialistRuntimeIds.TurnCapability);
        return Task.CompletedTask;
    }
}
