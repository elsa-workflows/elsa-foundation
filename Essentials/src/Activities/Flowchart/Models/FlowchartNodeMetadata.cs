using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record FlowchartNodeMetadata
{
    [JsonConstructor]
    public FlowchartNodeMetadata(string? policyKind = null, IReadOnlyDictionary<string, string>? configuration = null)
    {
        PolicyKind = string.IsNullOrWhiteSpace(policyKind) ? null : policyKind.Trim();
        Configuration = configuration ?? new Dictionary<string, string>();
    }

    public string? PolicyKind { get; init; }
    public IReadOnlyDictionary<string, string> Configuration { get; init; }
}
