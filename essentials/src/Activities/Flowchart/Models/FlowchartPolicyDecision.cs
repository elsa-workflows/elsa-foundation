using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record FlowchartPolicyDecision
{
    [JsonConstructor]
    public FlowchartPolicyDecision(
        IReadOnlyCollection<FlowchartPolicyCommand>? commands = null,
        IReadOnlyCollection<FlowchartDiagnosticEvent>? diagnostics = null)
    {
        Commands = commands ?? [];
        Diagnostics = diagnostics ?? [];
    }

    public IReadOnlyCollection<FlowchartPolicyCommand> Commands { get; init; }
    public IReadOnlyCollection<FlowchartDiagnosticEvent> Diagnostics { get; init; }
}
