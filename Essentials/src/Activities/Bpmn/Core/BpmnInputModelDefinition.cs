using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>Explicit input-capable model envelope; existing closed model formats keep their original representation.</summary>
public sealed class BpmnInputModelDefinition
{
    [JsonConstructor]
    public BpmnInputModelDefinition(int formatVersion, BpmnModelDefinition baseDefinition,
        IReadOnlyList<BpmnWorkerInputMapping> workerInputMappings)
    {
        if (formatVersion != 1) throw new NotSupportedException("The BPMN input-model format is unsupported.");
        ArgumentNullException.ThrowIfNull(baseDefinition);
        ArgumentNullException.ThrowIfNull(workerInputMappings);
        if (baseDefinition.FormatVersion is not (1 or 3 or 6) || workerInputMappings.Any(item => item is null) ||
            workerInputMappings.Select(item => item.BindingId).Distinct(StringComparer.Ordinal).Count() != workerInputMappings.Count)
            throw new ArgumentException("Input-capable BPMN requires an authored base profile and unique worker mappings.");
        FormatVersion = formatVersion; BaseDefinition = baseDefinition;
        WorkerInputMappings = Array.AsReadOnly(workerInputMappings.OrderBy(item => item.BindingId, StringComparer.Ordinal).ToArray());
    }
    public int FormatVersion { get; }
    public BpmnModelDefinition BaseDefinition { get; }
    public IReadOnlyList<BpmnWorkerInputMapping> WorkerInputMappings { get; }
}

/// <summary>One exact native worker's input mapping; preparation fills its actual pinned declared contract.</summary>
public sealed class BpmnWorkerInputMapping
{
    [JsonConstructor]
    public BpmnWorkerInputMapping(string bindingId, IReadOnlyDictionary<string, BpmnModelInputSource> inputs,
        WorkflowExecutableInputContract? workerInputContract = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId); ArgumentNullException.ThrowIfNull(inputs);
        if (inputs.Any(item => string.IsNullOrWhiteSpace(item.Key) || item.Value is null))
            throw new ArgumentException("Worker input mappings require declared names and non-null sources.", nameof(inputs));
        BindingId = bindingId;
        Inputs = new ReadOnlyDictionary<string, BpmnModelInputSource>(inputs.OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
        WorkerInputContract = workerInputContract;
    }
    public string BindingId { get; }
    public IReadOnlyDictionary<string, BpmnModelInputSource> Inputs { get; }
    public WorkflowExecutableInputContract? WorkerInputContract { get; }
}

/// <summary>Closed v1 selection: a literal JSON value or one declared model input, with no new expression language.</summary>
public sealed class BpmnModelInputSource
{
    [JsonConstructor]
    public BpmnModelInputSource(string kind, string? name = null, JsonElement? value = null)
    {
        if (kind is not ("Input" or "Literal") ||
            kind == "Input" && (string.IsNullOrWhiteSpace(name) || value.HasValue) ||
            kind == "Literal" && (name is not null || !value.HasValue || value.Value.ValueKind == JsonValueKind.Undefined))
            throw new ArgumentException("A model input source requires either its exact declared input name or a defined literal.");
        Kind = kind; Name = name; Value = value?.Clone();
    }
    public string Kind { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public JsonElement? Value { get; }
}
