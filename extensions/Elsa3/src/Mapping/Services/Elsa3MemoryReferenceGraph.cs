using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa3.Models;

namespace Elsa3.Mapping.Services;

/// <summary>
/// Inventories legacy value-reference occurrences before any Elsa 4 lowering takes place.
/// The resulting graph is importer-local and deliberately contains enough structural context for
/// a later pass to resolve or reject each relationship without relying on traversal order.
/// Each node's inputs and outputs come from the same resolution node mapping uses (spec FR-011), so lowering and
/// mapping cannot disagree about a node's target.
/// </summary>
public sealed class Elsa3MemoryReferenceGraph
{
    /// <param name="root">The normalized root activity.</param>
    /// <param name="contractOf">The declared inputs and outputs of a node, or null when it has none.</param>
    /// <param name="jsonPaths">The original JSON path of nodes normalization moved out of a port, if any.</param>
    /// <param name="cancellationToken">Cancels the walk.</param>
    internal async ValueTask<Elsa3MemoryReferenceInventory> CollectAsync(
        Elsa3Activity root,
        Func<Elsa3Activity, Elsa3NodeContract?> contractOf,
        IReadOnlyDictionary<Elsa3Activity, string>? jsonPaths = null,
        CancellationToken cancellationToken = default,
        Func<Elsa3Activity, string, string>? sourceNameOf = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(contractOf);
        _contractOf = contractOf;
        _jsonPaths = jsonPaths;
        _sourceNameOf = sourceNameOf ?? ((_, name) => name);

        var occurrences = new List<Elsa3MemoryOccurrence>();
        var nodes = new List<Elsa3MemoryReferenceNode>();
        var frames = new List<Elsa3StructuralFrame>();
        var propertyContracts = new List<Elsa3PropertyContract>();
        var rootNodeId = GetNodeId(root, "$.root");
        var rootActivityPath = $"/{rootNodeId}";

        frames.Add(new Elsa3StructuralFrame(rootNodeId, null, rootNodeId, rootActivityPath));
        await CollectActivityAsync(
            root,
            "$.root",
            rootActivityPath,
            rootNodeId,
            occurrences,
            nodes,
            frames,
            propertyContracts,
            cancellationToken);

        return new Elsa3MemoryReferenceInventory(
            occurrences.ToArray(),
            nodes.ToArray(),
            frames.ToArray(),
            propertyContracts.ToArray());
    }

    private Func<Elsa3Activity, Elsa3NodeContract?> _contractOf = _ => null;
    private IReadOnlyDictionary<Elsa3Activity, string>? _jsonPaths;
    private Func<Elsa3Activity, string, string> _sourceNameOf = (_, name) => name;

    private async ValueTask CollectActivityAsync(
        Elsa3Activity activity,
        string jsonActivityPath,
        string activityPath,
        string structuralFrameId,
        List<Elsa3MemoryOccurrence> occurrences,
        List<Elsa3MemoryReferenceNode> nodes,
        List<Elsa3StructuralFrame> frames,
        List<Elsa3PropertyContract> propertyContracts,
        CancellationToken cancellationToken)
    {
        var nodeId = GetNodeId(activity, jsonActivityPath);
        nodes.Add(new Elsa3MemoryReferenceNode(nodeId, activityPath, structuralFrameId));

        cancellationToken.ThrowIfCancellationRequested();
        var version = _contractOf(activity) ?? Elsa3NodeContract.Empty;
        CollectPropertyContracts(activity, version, nodeId, activityPath, structuralFrameId, propertyContracts);
        CollectOccurrences(activity, version, nodeId, jsonActivityPath, activityPath, structuralFrameId, occurrences);

        var children = activity.Activities ?? [];
        if (children.Count == 0)
            return;

        var childFrameId = nodeId;
        if (!frames.Any(x => x.Id.Equals(childFrameId, StringComparison.Ordinal)))
            frames.Add(new Elsa3StructuralFrame(childFrameId, structuralFrameId, nodeId, activityPath));

        for (var index = 0; index < children.Count; index++)
        {
            var child = children[index];
            var childJsonPath = _jsonPaths?.GetValueOrDefault(child) ?? $"{jsonActivityPath}.activities[{index}]";
            var childNodeId = GetNodeId(child, childJsonPath);
            await CollectActivityAsync(
                child,
                childJsonPath,
                $"{activityPath}/{childNodeId}",
                childFrameId,
                occurrences,
                nodes,
                frames,
                propertyContracts,
                cancellationToken);
        }
    }

    private static void CollectPropertyContracts(
        Elsa3Activity activity,
        Elsa3NodeContract version,
        string nodeId,
        string activityPath,
        string structuralFrameId,
        ICollection<Elsa3PropertyContract> contracts)
    {
        foreach (var propertyName in activity.AdditionalProperties.Keys)
        {
            foreach (var input in version.Inputs.Where(value => Names(value.Name, value.ReferenceKey, propertyName)))
            {
                contracts.Add(new Elsa3PropertyContract(
                    nodeId,
                    activityPath,
                    structuralFrameId,
                    propertyName,
                    input.ReferenceKey,
                    Elsa3PropertyDirection.Input,
                    input.Type));
            }
            foreach (var output in version.Outputs.Where(value => Names(value.Name, value.ReferenceKey, propertyName)))
            {
                contracts.Add(new Elsa3PropertyContract(
                    nodeId,
                    activityPath,
                    structuralFrameId,
                    propertyName,
                    output.ReferenceKey,
                    Elsa3PropertyDirection.Output,
                    output.Type));
            }
        }
    }

    private void CollectOccurrences(
        Elsa3Activity activity,
        Elsa3NodeContract version,
        string nodeId,
        string jsonActivityPath,
        string activityPath,
        string structuralFrameId,
        List<Elsa3MemoryOccurrence> occurrences)
    {
        foreach (var (propertyName, value) in activity.AdditionalProperties)
        {
            if (!TryReadReference(value, out var memoryReferenceId, out var expression))
                continue;

            var jsonPath = $"{jsonActivityPath}{FormatJsonProperty(_sourceNameOf(activity, propertyName))}.memoryReference";
            var inputs = version.Inputs
                .Where(x => Names(x.Name, x.ReferenceKey, propertyName))
                .ToArray();
            var outputs = version.Outputs
                .Where(x => Names(x.Name, x.ReferenceKey, propertyName))
                .ToArray();

            foreach (var input in inputs)
                occurrences.Add(CreateOccurrence(input.ReferenceKey, Elsa3PropertyDirection.Input));

            foreach (var output in outputs)
                occurrences.Add(CreateOccurrence(output.ReferenceKey, Elsa3PropertyDirection.Output));

            if (inputs.Length == 0 && outputs.Length == 0)
                occurrences.Add(CreateOccurrence(propertyName, Elsa3PropertyDirection.Unknown));

            Elsa3MemoryOccurrence CreateOccurrence(string stablePropertyKey, Elsa3PropertyDirection direction) =>
                new(
                    memoryReferenceId,
                    jsonPath,
                    nodeId,
                    activityPath,
                    stablePropertyKey,
                    direction,
                    structuralFrameId,
                    expression);
        }
    }

    /// <summary>
    /// Whether a member named <paramref name="name"/> with reference key <paramref name="referenceKey"/> is the one a
    /// property names: normalization renames properties to exact Elsa 4 keys, and an alias binds by name.
    /// </summary>
    private static bool Names(string name, string referenceKey, string propertyName) =>
        StringComparer.Ordinal.Equals(referenceKey, propertyName) ||
        name.Equals(propertyName, StringComparison.OrdinalIgnoreCase) ||
        referenceKey.Equals(propertyName, StringComparison.OrdinalIgnoreCase);

    private static bool TryReadReference(
        JsonElement value,
        out string memoryReferenceId,
        out Elsa3Expression? expression)
    {
        memoryReferenceId = string.Empty;
        expression = null;

        if (value.ValueKind != JsonValueKind.Object ||
            !value.TryGetProperty("memoryReference", out var memoryReference))
            return false;

        if (memoryReference.ValueKind == JsonValueKind.Object &&
            memoryReference.TryGetProperty("id", out var id) &&
            id.ValueKind == JsonValueKind.String)
            memoryReferenceId = id.GetString() ?? string.Empty;

        if (value.TryGetProperty("expression", out var expressionElement) &&
            expressionElement.ValueKind == JsonValueKind.Object &&
            expressionElement.TryGetProperty("type", out var type) &&
            type.ValueKind == JsonValueKind.String &&
            expressionElement.TryGetProperty("value", out var expressionValue) &&
            expressionValue.ValueKind == JsonValueKind.String)
        {
            expression = new Elsa3Expression
            {
                Type = type.GetString() ?? string.Empty,
                Value = expressionValue.GetString() ?? string.Empty,
            };
        }

        return true;
    }

    private static string GetNodeId(Elsa3Activity activity, string fallback) =>
        !string.IsNullOrWhiteSpace(activity.NodeId)
            ? activity.NodeId
            : !string.IsNullOrWhiteSpace(activity.Id)
                ? activity.Id
                : fallback;

    private static string FormatJsonProperty(string propertyName)
    {
        if (propertyName.Length > 0 &&
            (char.IsLetter(propertyName[0]) || propertyName[0] is '_' or '$') &&
            propertyName.Skip(1).All(x => char.IsLetterOrDigit(x) || x is '_' or '$'))
            return $".{propertyName}";

        var escaped = propertyName.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("'", "\\'", StringComparison.Ordinal);
        return $"['{escaped}']";
    }
}

/// <summary>The declared inputs and outputs value-flow lowering sees for one node.</summary>
internal sealed record Elsa3NodeContract(IReadOnlyList<InputDefinition> Inputs, IReadOnlyList<OutputDefinition> Outputs)
{
    public static readonly Elsa3NodeContract Empty = new([], []);
}

public sealed record Elsa3MemoryReferenceInventory(
    IReadOnlyList<Elsa3MemoryOccurrence> Occurrences,
    IReadOnlyList<Elsa3MemoryReferenceNode> Nodes,
    IReadOnlyList<Elsa3StructuralFrame> StructuralFrames,
    IReadOnlyList<Elsa3PropertyContract> PropertyContracts);

public sealed record Elsa3MemoryOccurrence(
    string MemoryReferenceId,
    string JsonPath,
    string ActivityNodeId,
    string ActivityPath,
    string StablePropertyKey,
    Elsa3PropertyDirection Direction,
    string StructuralFrameId,
    Elsa3Expression? Expression);

public sealed record Elsa3MemoryReferenceNode(
    string ActivityNodeId,
    string ActivityPath,
    string StructuralFrameId);

public sealed record Elsa3PropertyContract(
    string ActivityNodeId,
    string ActivityPath,
    string StructuralFrameId,
    string PropertyName,
    string StablePropertyKey,
    Elsa3PropertyDirection Direction,
    Elsa.Primitives.Models.TypeReference Type);

public sealed record Elsa3StructuralFrame(
    string Id,
    string? ParentId,
    string OwnerActivityNodeId,
    string ActivityPath);

public enum Elsa3PropertyDirection
{
    Unknown,
    Input,
    Output,
}
