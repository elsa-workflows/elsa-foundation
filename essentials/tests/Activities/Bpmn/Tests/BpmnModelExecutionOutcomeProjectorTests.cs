using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnModelExecutionOutcomeProjectorTests
{
    [Theory]
    [InlineData("Completed", WorkflowExecutionStatus.Completed)]
    [InlineData("Faulted", WorkflowExecutionStatus.Faulted)]
    [InlineData("Cancelled", WorkflowExecutionStatus.Cancelled)]
    public void Terminal_semantic_outcome_is_independent_of_cleanup(string result, WorkflowExecutionStatus expected)
    {
        var protocol = new RuntimeExecutionProtocol(BpmnModelProtocol.Key, BpmnModelProtocol.Version);
        var terminal = JsonSerializer.SerializeToElement(new { status = result });
        Assert.Equal(expected, BpmnModelExecutionOutcomeProjector.Read(protocol, RuntimeModelLifecycle.TerminalPendingSettlement, terminal));
        Assert.Equal(expected, BpmnModelExecutionOutcomeProjector.Read(protocol, RuntimeModelLifecycle.TerminalSettled, terminal));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"status\":42}")]
    [InlineData("{\"status\":\"FutureOutcome\"}")]
    [InlineData("{\"status\":\"Running\"}")]
    public void Unrecognized_results_remain_unknown(string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Null(BpmnModelExecutionOutcomeProjector.Read(new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), RuntimeModelLifecycle.TerminalSettled, document.RootElement));
    }

    [Fact]
    public void Other_protocols_and_nonterminal_states_cannot_borrow_a_terminal_outcome()
    {
        var terminal = JsonSerializer.SerializeToElement(new { status = "Completed" });
        Assert.Null(BpmnModelExecutionOutcomeProjector.Read(new("other.model", 1), RuntimeModelLifecycle.TerminalSettled, terminal));
        Assert.Null(BpmnModelExecutionOutcomeProjector.Read(new(BpmnModelProtocol.Key, BpmnModelProtocol.Version + 1), RuntimeModelLifecycle.TerminalSettled, terminal));
        Assert.Null(BpmnModelExecutionOutcomeProjector.Read(new(BpmnModelProtocol.Key, BpmnModelProtocol.Version), RuntimeModelLifecycle.Running, terminal));
    }
}
