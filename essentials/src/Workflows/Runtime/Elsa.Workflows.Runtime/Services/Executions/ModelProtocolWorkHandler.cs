using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Executions;

internal sealed class ModelProtocolWorkHandler(RuntimeExecutionProtocolSelector selector) : IWorkflowSchedulerWorkHandler
{
    public string Name => nameof(ModelProtocolWorkHandler);
    public bool CanHandle(RuntimeSchedulerWorkItem workItem) => workItem.CommandKind == WorkflowExecutionCommandKind.ExecuteModel;
    public async ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        var selected = await selector.SelectAsync(workItem, cancellationToken);
        await selected.Driver.ExecuteAsync(selected.Context, cancellationToken);
    }
}
