using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>Direct-host model material, separate from the frozen native BpmnProcess authoring envelope.</summary>
public sealed class BpmnModelDefinition
{
    public BpmnModelDefinition(int formatVersion, JsonElement process, IReadOnlyList<string> humanBindings)
        : this(formatVersion, process, humanBindings, null) { }

    [JsonConstructor]
    public BpmnModelDefinition(int formatVersion, JsonElement process, IReadOnlyList<string> humanBindings,
        IReadOnlyList<BpmnNativeServiceBinding>? serviceBindings)
    {
        if (formatVersion is not (1 or 2)) throw new NotSupportedException("The BPMN model definition format is unsupported.");
        if (process.ValueKind != JsonValueKind.Object) throw new ArgumentException("A BPMN process must be an object.", nameof(process));
        ArgumentNullException.ThrowIfNull(humanBindings);
        if (humanBindings.Any(string.IsNullOrWhiteSpace) || humanBindings.Distinct(StringComparer.Ordinal).Count() != humanBindings.Count)
            throw new ArgumentException("BPMN human binding identities must be nonblank and unique.", nameof(humanBindings));
        FormatVersion = formatVersion; Process = process.Clone();
        HumanBindings = Array.AsReadOnly(humanBindings.Order(StringComparer.Ordinal).ToArray());
        if (formatVersion == 1 && serviceBindings is not null || formatVersion == 2 && serviceBindings is not { Count: > 0 })
            throw new ArgumentException("The native service profile requires its explicit versioned binding inventory.", nameof(serviceBindings));
        if (serviceBindings is not null)
        {
            if (serviceBindings.Any(binding => binding is null) ||
                serviceBindings.Select(binding => binding.BindingId).Distinct(StringComparer.Ordinal).Count() != serviceBindings.Count ||
                serviceBindings.Any(binding => HumanBindings.Contains(binding.BindingId, StringComparer.Ordinal)))
                throw new ArgumentException("Human and native service binding identities must be distinct.", nameof(serviceBindings));
            ServiceBindings = Array.AsReadOnly(serviceBindings.OrderBy(binding => binding.BindingId, StringComparer.Ordinal).ToArray());
        }
    }
    public int FormatVersion { get; }
    public JsonElement Process { get; }
    public IReadOnlyList<string> HumanBindings { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BpmnNativeServiceBinding>? ServiceBindings { get; }
}

/// <summary>One service task's complete published native worker pin and modeled business-error code.</summary>
public sealed class BpmnNativeServiceBinding
{
    public BpmnNativeServiceBinding(string bindingId, WorkflowExecutableIdentity executable, string businessErrorCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessErrorCode);
        ArgumentNullException.ThrowIfNull(executable);
        foreach (var value in new[] { executable.ArtifactId, executable.ArtifactHash, executable.DefinitionId,
                     executable.DefinitionVersionId, executable.ArtifactVersion }) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        BindingId = bindingId; Executable = executable; BusinessErrorCode = businessErrorCode;
    }
    public string BindingId { get; }
    public WorkflowExecutableIdentity Executable { get; }
    public string BusinessErrorCode { get; }
}

/// <summary>The protocol and immutable material profile interpreted by the direct BPMN adapter.</summary>
public static class BpmnModelProtocol
{
    public const string Key = "elsa.bpmn";
    public const int Version = 1;
    public const string Profile = "foundation-direct-v1";
    public const string NativeWorkProfile = "foundation-native-work-v1";
}
