using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Internal;

internal sealed class BpmnModelExecutionOutcomeProjector : IRuntimeModelExecutionOutcomeProjector
{
    public string ModelKey => "bpmn";
    public WorkflowExecutionStatus? Project(RuntimeModelExecutionState execution) =>
        Read(execution.Protocol, execution.Lifecycle, execution.TerminalResult);

    internal static WorkflowExecutionStatus? Read(RuntimeExecutionProtocol protocol, RuntimeModelLifecycle lifecycle, JsonElement? result)
    {
        if (protocol != new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version) ||
            lifecycle is not (RuntimeModelLifecycle.TerminalPendingSettlement or RuntimeModelLifecycle.TerminalSettled) ||
            result is not { ValueKind: JsonValueKind.Object } terminal ||
            !terminal.TryGetProperty("status", out var status) || status.ValueKind != JsonValueKind.String) return null;
        return status.GetString() switch
        {
            "Completed" => WorkflowExecutionStatus.Completed,
            "Faulted" => WorkflowExecutionStatus.Faulted,
            "Cancelled" => WorkflowExecutionStatus.Cancelled,
            _ => null
        };
    }
}
