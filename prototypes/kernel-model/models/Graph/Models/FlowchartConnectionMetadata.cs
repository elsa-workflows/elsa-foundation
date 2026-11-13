using System.Text.Json.Serialization;

namespace Elsa.Activities.Flowchart.Models;

public sealed record FlowchartConnectionMetadata
{
    [JsonConstructor]
    public FlowchartConnectionMetadata(
        string? policyKind = null,
        string? label = null,
        bool isDefault = false,
        IReadOnlyDictionary<string, string>? configuration = null)
    {
        PolicyKind = string.IsNullOrWhiteSpace(policyKind) ? null : policyKind.Trim();
        Label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        IsDefault = isDefault;
        Configuration = configuration ?? new Dictionary<string, string>();
    }

    public string? PolicyKind { get; init; }
    public string? Label { get; init; }
    public bool IsDefault { get; init; }
    public IReadOnlyDictionary<string, string> Configuration { get; init; }
}
