using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services.Strategies;

/// <summary>Built-in public incident-resolution actions with capability-limited execution behavior.</summary>
public static class IncidentResolutionActions
{
    public static IIncidentResolutionAction FaultWorkflow() => FaultWorkflowAction.Instance;
    public static IIncidentResolutionAction ContinueWithIncidents() => ContinueWithIncidentsAction.Instance;
    public static IIncidentResolutionAction WaitForIntervention() => WaitForInterventionAction.Instance;

    private sealed class FaultWorkflowAction : IIncidentResolutionAction
    {
        public static readonly FaultWorkflowAction Instance = new();
        public string Kind => IncidentResolutionActionKinds.FaultWorkflow;

        public ValueTask ExecuteAsync(IncidentResolutionActionContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            context.KeepIncidentBlocking();
            context.RequestWorkflowFault();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ContinueWithIncidentsAction : IIncidentResolutionAction
    {
        public static readonly ContinueWithIncidentsAction Instance = new();
        public string Kind => IncidentResolutionActionKinds.ContinueWithIncidents;

        public ValueTask ExecuteAsync(IncidentResolutionActionContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            context.OpenIncident();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class WaitForInterventionAction : IIncidentResolutionAction
    {
        public static readonly WaitForInterventionAction Instance = new();
        public string Kind => IncidentResolutionActionKinds.WaitForIntervention;

        public ValueTask ExecuteAsync(IncidentResolutionActionContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            cancellationToken.ThrowIfCancellationRequested();
            context.KeepIncidentBlocking();
            return ValueTask.CompletedTask;
        }
    }
}
