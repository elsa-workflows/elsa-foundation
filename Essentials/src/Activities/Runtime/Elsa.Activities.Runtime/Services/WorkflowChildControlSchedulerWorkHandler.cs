using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Delivers a committed exact-call nonterminal batch through the generic ordered inbox.</summary>
internal sealed class WorkflowChildControlSchedulerWorkHandler(IServiceScopeFactory scopes, TimeProvider? timeProvider = null)
    : RuntimeSchedulerWorkHandlerBase<RuntimeChildControlCommandPayload>(scopes, timeProvider)
{
    // Persisted in poison/recovery records; preserve this wire value across any future type rename.
    internal const string HandlerName = "WorkflowChildControlSchedulerWorkHandler";
    public override string Name => HandlerName;
    public override bool CanHandle(RuntimeSchedulerWorkItem workItem) => workItem.CommandKind == WorkflowExecutionCommandKind.DeliverChildControl;

    protected override ValueTask HandleWithServicesAsync(RuntimeSchedulerWorkItem workItem,
        RuntimeChildControlCommandPayload payload, IServiceProvider services, CancellationToken cancellationToken) =>
        ChildControlDelivery.RunAsync(services, TimeProvider, workItem, payload, payload.Control,
            completionWorkItemId: null, cancellationToken);

    protected override RuntimeChildControlCommandPayload DeserializePayload(RuntimeSchedulerWorkItem workItem) =>
        SchedulerWorkItems.DeserializePayload<RuntimeChildControlCommandPayload>(workItem, "DeliverChildControl", "child control payload",
            ["pinnedExecutable", "executableNodeId", "activityExecutionId", "parentActivityExecutionId", "branchId", "control"]);
}
