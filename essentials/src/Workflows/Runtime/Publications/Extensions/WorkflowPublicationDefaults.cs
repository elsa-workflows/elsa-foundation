using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Extensions;

/// <summary>Supplies the shared workflow publication services used by the Publishing engine.</summary>
public static class WorkflowPublicationDefaults
{
    /// <summary>Registers the default material, control and projection services for workflow publications.</summary>
    public static void Add(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AddSharedServices(services);
        services.TryAddSingleton(provider =>
            provider.GetService<IInMemoryPublicationReservationStateSource>()?.PublicationReservations ??
            new InMemoryPublicationReservationState());
        AddActivationServices<PublicationActivationSwitch>(services);
    }

    internal static void AddSharedServices(IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.TryAddSingleton(TimeProvider.System);
        services.AddWorkflowPublicationTriggers();
        WorkflowPublicationArtifactDefaults.AddWorkflowArtifactDefaults(services);
        services.AddBuiltInIncidentStrategies();
    }

    internal static void AddActivationServices<TSwitch>(IServiceCollection services)
        where TSwitch : class, IWorkflowActivationSwitch
    {
        services.AddActivationChannelOptions();
        services.TryAddSingleton<IWorkflowActivationAuthority, InMemoryWorkflowActivationAuthority>();
        services.TryAddScoped<IWorkflowActivationSwitch, TSwitch>();
        services.TryAddScoped<IWorkflowActivationCoordinator, WorkflowActivationCoordinator>();
        WorkflowActivationSwitchCompositionValidator.Register(services);
    }
}
