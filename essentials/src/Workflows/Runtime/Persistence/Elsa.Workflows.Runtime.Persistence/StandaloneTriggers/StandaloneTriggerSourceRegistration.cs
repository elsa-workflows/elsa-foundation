using Elsa.Events.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Incidents;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Services.Triggers;

internal static class StandaloneTriggerSourceRegistration
{
    internal static void AddPreparedServices(IServiceCollection services,
        Func<IServiceProvider, IRuntimePostCommitOutboxProcessor> createOutbox)
    {
        AddServices(services);
        services.TryAddSingleton<PreparedStandaloneAdmission>();
        services.TryAddScoped<IWorkflowExecutableInputValidator, WorkflowExecutableInputValidator>();
        services.TryAddScoped<IWorkflowExecutableStartPolicy, AllowWorkflowExecutableStartPolicy>();
        services.AddOptions<RuntimeFaultCaptureOptions>();
        services.TryAddSingleton<IRuntimeFaultCapturePolicy, DefaultRuntimeFaultCapturePolicy>();
        services.AddScoped<IRuntimePostCommitIntentDispatcher, RuntimePostCommitIntentDispatcher>();
        services.AddScoped<IRuntimePostCommitOutboxProcessor>(provider => new PreparedStandaloneOutboxProcessor(createOutbox(provider)));
    }

    internal static void AddServices(IServiceCollection services)
    {
        StandaloneTriggerProviderCompositionValidator.Register(services);
        services.TryAddScoped<ITriggerInputMapper, TriggerInputMapper>();
        services.TryAddScoped<TriggerTargetResolver>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEventHandler<TriggerTargetModelsDiscovering>, CollectTriggerTargetModels>());
        services.TryAddScoped<TriggerManagementTargets>();
        services.TryAddScoped<IStandaloneTriggerTargetDiscovery, StandaloneTriggerTargetDiscovery>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ITriggerTargetModelSource, NativeTriggerTargetModelSource>());
        services.TryAddScoped<StandaloneTriggerAdmission>();
        services.TryAddScoped<IStandaloneTriggerIngressFactory, StandaloneTriggerIngressFactory>();
        services.TryAddScoped<ITriggerManagementCommitter, ProtectedTriggerManagementCommitter>();
        services.TryAddScoped<IRuntimeIngressTransportObserver>(provider => provider.GetRequiredService<IRuntimeIngressDeliveryStore>() as IRuntimeIngressTransportObserver
            ?? new UnavailableIngressTransportObserver());
        services.AddRuntimeDeliverySourceExecutor<StandaloneTriggerDeliveryExecutor>(RuntimeStandaloneIngressSource.StandaloneTriggerDeliveryKind,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)));
    }
}

internal sealed class UnavailableIngressTransportObserver : IRuntimeIngressTransportObserver;
