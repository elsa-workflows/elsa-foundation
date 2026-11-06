using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.StandaloneTriggers;

/// <summary>A declared input destination and a trigger-owned, pre-execution value selector.</summary>
public sealed class TriggerInputMapping
{
    [JsonConstructor]
    public TriggerInputMapping(string inputName, TriggerValueBinding binding)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputName);
        ArgumentNullException.ThrowIfNull(binding);
        InputName = inputName; Binding = binding;
    }

    public string InputName { get; }
    public TriggerValueBinding Binding { get; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TriggerLiteralBinding), "Literal")]
[JsonDerivedType(typeof(TriggerSourceBinding), "SourceField")]
[JsonDerivedType(typeof(TriggerExpressionBinding), "Expression")]
public abstract class TriggerValueBinding;

public sealed class TriggerLiteralBinding : TriggerValueBinding
{
    [JsonConstructor]
    public TriggerLiteralBinding(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("A literal is a JSON value; omission is not a literal null.", nameof(value));
        Value = value.Clone();
    }

    public JsonElement Value { get; }
}

/// <summary>Exact object property segments in the detached source envelope; missing and JSON null remain distinct.</summary>
public sealed class TriggerSourceBinding : TriggerValueBinding
{
    [JsonConstructor]
    public TriggerSourceBinding(IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Any(segment => segment is null)) throw new ArgumentException("Source path segments cannot be null.", nameof(path));
        Path = new ReadOnlyCollection<string>(path.ToArray());
    }

    public IReadOnlyList<string> Path { get; }
}

/// <summary>A pure expression template. Its parameters are materialized from detached literal/source selectors at admission.</summary>
public sealed class TriggerExpressionBinding : TriggerValueBinding
{
    [JsonConstructor]
    public TriggerExpressionBinding(string language, string source, TypeReference resultType, JsonElement options,
        string capabilityProfile, IReadOnlyDictionary<string, TriggerValueBinding> parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityProfile);
        ArgumentNullException.ThrowIfNull(resultType);
        ArgumentNullException.ThrowIfNull(parameters);
        if (options.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expression options must be an object.", nameof(options));
        if (parameters.Any(pair => string.IsNullOrWhiteSpace(pair.Key) || pair.Value is not (TriggerLiteralBinding or TriggerSourceBinding)))
            throw new ArgumentException("Template parameters must be named literal/source selectors.", nameof(parameters));
        Language = language; Source = source; ResultType = resultType; Options = options.Clone();
        CapabilityProfile = capabilityProfile;
        Parameters = new ReadOnlyDictionary<string, TriggerValueBinding>(parameters.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
    }

    public string Language { get; }
    public string Source { get; }
    public TypeReference ResultType { get; }
    public JsonElement Options { get; }
    public string CapabilityProfile { get; }
    public IReadOnlyDictionary<string, TriggerValueBinding> Parameters { get; }
}
