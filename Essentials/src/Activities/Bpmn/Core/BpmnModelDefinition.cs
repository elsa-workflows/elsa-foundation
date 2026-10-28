using System.Text.Json;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>Direct-host model material, separate from the frozen native BpmnProcess authoring envelope.</summary>
public sealed class BpmnModelDefinition
{
    public BpmnModelDefinition(int formatVersion, JsonElement process, IReadOnlyList<string> humanBindings)
    {
        if (formatVersion != 1) throw new NotSupportedException("The BPMN model definition format is unsupported.");
        if (process.ValueKind != JsonValueKind.Object) throw new ArgumentException("A BPMN process must be an object.", nameof(process));
        ArgumentNullException.ThrowIfNull(humanBindings);
        if (humanBindings.Any(string.IsNullOrWhiteSpace) || humanBindings.Distinct(StringComparer.Ordinal).Count() != humanBindings.Count)
            throw new ArgumentException("BPMN human binding identities must be nonblank and unique.", nameof(humanBindings));
        FormatVersion = formatVersion; Process = process.Clone();
        HumanBindings = Array.AsReadOnly(humanBindings.Order(StringComparer.Ordinal).ToArray());
    }
    public int FormatVersion { get; }
    public JsonElement Process { get; }
    public IReadOnlyList<string> HumanBindings { get; }
}

/// <summary>The protocol and immutable material profile interpreted by the direct BPMN adapter.</summary>
public static class BpmnModelProtocol
{
    public const string Key = "elsa.bpmn";
    public const int Version = 1;
    public const string Profile = "foundation-direct-v1";
}
