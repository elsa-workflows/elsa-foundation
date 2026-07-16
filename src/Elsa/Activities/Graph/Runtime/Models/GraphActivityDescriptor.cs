using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Activities.Graph.Runtime.Models;

/// <summary>
/// Stable Runtime schema 1 payload for a placed activity-graph boundary.
/// </summary>
public sealed class GraphActivityDescriptor
{
    [JsonConstructor]
    public GraphActivityDescriptor(
        string definitionId,
        string definitionVersionId,
        string version,
        string templateHash,
        string? entryNodeId,
        string? entryOccurrenceId,
        IReadOnlyList<GraphActivityInputDescriptor>? inputs,
        IReadOnlyList<GraphActivityVariableDescriptor>? variables,
        IReadOnlyList<GraphActivityOutputDescriptor>? outputs,
        IReadOnlyList<GraphActivityOutputMappingDescriptor>? outputMappings,
        IReadOnlyList<string>? requiredInputReferenceKeys,
        IReadOnlyList<string>? requiredOutputReferenceKeys)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(definitionVersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateHash);

        var effectiveEntryNodeId = !string.IsNullOrWhiteSpace(entryNodeId) ? entryNodeId : entryOccurrenceId;
        ArgumentException.ThrowIfNullOrWhiteSpace(effectiveEntryNodeId);

        DefinitionId = definitionId;
        DefinitionVersionId = definitionVersionId;
        Version = version;
        TemplateHash = templateHash;
        EntryNodeId = effectiveEntryNodeId;
        EntryOccurrenceId = entryOccurrenceId;
        Inputs = Snapshot(inputs);
        Variables = Snapshot(variables);
        Outputs = Snapshot(outputs);
        OutputMappings = Snapshot(outputMappings);
        RequiredInputReferenceKeys = SnapshotKeys(requiredInputReferenceKeys);
        RequiredOutputReferenceKeys = SnapshotKeys(requiredOutputReferenceKeys);
    }

    public string DefinitionId { get; }
    public string DefinitionVersionId { get; }
    public string Version { get; }
    public string TemplateHash { get; }
    public string EntryNodeId { get; }
    public string? EntryOccurrenceId { get; }
    public IReadOnlyList<GraphActivityInputDescriptor> Inputs { get; }
    public IReadOnlyList<GraphActivityVariableDescriptor> Variables { get; }
    public IReadOnlyList<GraphActivityOutputDescriptor> Outputs { get; }
    public IReadOnlyList<GraphActivityOutputMappingDescriptor> OutputMappings { get; }
    public IReadOnlyList<string> RequiredInputReferenceKeys { get; }
    public IReadOnlyList<string> RequiredOutputReferenceKeys { get; }

    private static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T>? values) =>
        Array.AsReadOnly((values ?? []).ToArray());

    private static IReadOnlyList<string> SnapshotKeys(IReadOnlyList<string>? values)
    {
        var snapshot = (values ?? []).ToArray();
        if (snapshot.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Required graph reference keys cannot contain blank values.", nameof(values));
        if (snapshot.Distinct(StringComparer.Ordinal).Count() != snapshot.Length)
            throw new ArgumentException("Required graph reference keys cannot contain duplicates.", nameof(values));
        return Array.AsReadOnly(snapshot);
    }
}

public sealed record GraphActivityInputDescriptor(
    string ReferenceKey,
    JsonElement Type,
    string StorageDriverKey,
    GraphActivityExpression? Default,
    string? Name = null);

public sealed record GraphActivityVariableDescriptor(
    string ReferenceKey,
    string Name,
    JsonElement Type,
    string StorageDriverKey,
    GraphActivityExpression? InitialValue);

public sealed record GraphActivityOutputDescriptor(
    string ReferenceKey,
    JsonElement Type,
    string StorageDriverKey,
    string? Name = null);

public sealed record GraphActivityOutputMappingDescriptor(
    string OutputReferenceKey,
    GraphActivityExpression Source);

public sealed record GraphActivityExpression(string Syntax, JsonElement Value);
