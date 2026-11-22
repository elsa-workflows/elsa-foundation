using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Agent.Core.DependencyInjection;
using Elsa.Agent.Core.Contracts;
using CShells.Features;
using Elsa.Agent.JsonSchema;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Agent.Activities;
using Elsa.Agent.Core.Models;
using Elsa.Agent.Journal;
using Elsa.Agent.Services;
using Elsa.Serialization.Core;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent;

/// <summary>Activates pinned native agent boundaries and their background model-turn workers.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Runtime")]
[ShellFeature(name: "AgentRuntime", DisplayName = "Agents (Runtime)",
    Description = "Executes published agents through Microsoft Agent Framework. Requires an explicitly configured turn journal and model provider.",
    DependsOn = new object[] { "BackgroundActivities", "Secrets", "Serialization" })]
public class AgentRuntimeFeature : IShellFeature
{
    /// <summary>Allows new logical operations. Disabling keeps matching admitted work, readers and settlement available.</summary>
    public bool AllowNewOperations { get; set; } = true;

    /// <summary>Uses validated streaming model responses. Requires a streaming-qualified model contribution; defaults to false.</summary>
    public bool StreamResponses { get; set; }

    /// <summary>Protected content retention after native workflow completion; active work remains pinned. Defaults to thirty days.</summary>
    public TimeSpan ContentRetention { get; set; } = TimeSpan.FromDays(30);

    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        if (ContentRetention <= TimeSpan.Zero || ContentRetention > TimeSpan.FromDays(3650)) throw new ArgumentOutOfRangeException(nameof(ContentRetention));
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(new AgentRetentionPolicy(ContentRetention));
        services.TryAddScoped<AgentRetentionPruner>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringTask, AgentRetentionPumpTask>());
        services.TryAddSingleton(new AgentAdmissionPolicy(AllowNewOperations));
        services.TryAddSingleton(new AgentStreamingPolicy(StreamResponses));
        services.TryAddScoped<AgentSchemaValidator>();
        services.TryAddScoped<AgentNativeBinding>();
        services.TryAddScoped<IAgentInvocationInspector, AgentInvocationInspector>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IActivityStateViewProjector, AgentInvocationStateViewProjector>());
        services.TryAddScoped<AgentTurnJournal>();
        services.TryAddScoped<AgentModelRunner>();
        services.TryAddScoped<AgentTurnBudgetAllocator>();
        services.TryAddScoped<AgentTurnLeaseRenewer>();
        services.TryAddScoped<AgentSessionCodec>();
        services.TryAddScoped<AgentSettlementBoundary>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitEnricher, AgentCheckpointEnricher>());
        services.AddRuntimePostCommitIntentHandler<AgentSettlementHandler>(AgentSettlementBoundary.AppliedKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)), requiresDurableSource: true);
        services.AddRuntimePostCommitIntentHandler<AgentSettlementHandler>(AgentSettlementBoundary.CancelKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)), requiresDurableSource: true);
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IActivityActivationStrategy, AgentActivationStrategy>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IRuntimeActivityConsumerCapability, AgentConsumerCapability>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IStartupTask, AgentTypesStartupTask>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupTask>(new AgentRegistrationStartupTask(services)));
    }
}

internal sealed class AgentRegistrationStartupTask(IServiceCollection services) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (services.Count(descriptor => descriptor.ServiceType == typeof(IAgentTurnStore)) != 1)
            throw new InvalidOperationException("AgentRuntime requires exactly one IAgentTurnStore backend per shell.");
        if (services.Count(descriptor => descriptor.ServiceType == typeof(IAgentInvocationInspector)) != 1)
            throw new InvalidOperationException("AgentRuntime requires exactly one IAgentInvocationInspector per shell.");
        return Task.CompletedTask;
    }
}

/// <summary>Explicit development/test storage. It cannot continue turns after a host restart.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ManifestFeatureCategory("Persistence")]
[ShellFeature(name: "AgentInMemoryJournal", DisplayName = "Agents: In-memory Journal",
    Description = "Development and test only. Retains at most 10,000 turns for this host lifetime; missing turns are refused after restart.",
    DependsOn = new object[] { "AgentRuntime" })]
public class AgentInMemoryJournalFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddPersistenceCore();
        services.TryAddSingleton<InMemoryAgentTurnState>();
        services.TryAddAgentTurnStore<InMemoryAgentTurnStore>(ServiceLifetime.Scoped);
    }
}

internal sealed class AgentActivationStrategy(AgentNativeBinding binding, AgentTurnJournal journal, AgentModelRunner runner, TimeProvider time, AgentTurnBudgetAllocator budgets, AgentTurnLeaseRenewer leases) : IActivityActivationStrategy
{
    public string ConsumerKey => AgentRuntimeIds.ConsumerKey;
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [AgentRuntimeIds.DescriptorSchema, AgentRuntimeIds.ContinuityDescriptorSchema];
    public bool RequiresInputHydration => false;
    public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationStrategyRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Descriptor.ConsumerKey != ConsumerKey || !SupportedSchemaVersions.Contains(request.Descriptor.SchemaVersion) || request.Contract.DescriptorKind != ConsumerKey)
            throw new InvalidOperationException("The agent descriptor consumer or schema is unsupported.");
        IActivity activity = request.Contract.Activation.Capability switch
        {
            AgentRuntimeIds.FacadeCapability => new AgentActivity(binding, journal, runner),
            AgentRuntimeIds.TurnCapability when request.Contract.SupportsBackgroundExecution && request.Contract.Inputs.Count == 0 => new AgentModelTurn(binding, journal, runner, time, budgets, leases),
            _ => throw new InvalidOperationException("The agent activation capability is unsupported.")
        };
        return ValueTask.FromResult(new ActivityActivationLease(activity));
    }
}

internal sealed class AgentConsumerCapability : IRuntimeActivityConsumerCapability
{
    public string ConsumerKey => AgentRuntimeIds.ConsumerKey;
    public IReadOnlyCollection<string> SupportedSchemaVersions { get; } = [AgentRuntimeIds.DescriptorSchema, AgentRuntimeIds.ContinuityDescriptorSchema];
}

internal sealed class AgentTypesStartupTask(IWellKnownTypeRegistry types) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        types.RegisterType(typeof(AgentActivity), AgentRuntimeIds.FacadeCapability);
        types.RegisterType(typeof(AgentModelTurn), AgentRuntimeIds.TurnCapability);
        return Task.CompletedTask;
    }
}
