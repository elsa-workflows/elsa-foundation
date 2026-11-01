using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.Bpmn.Core;

/// <summary>Direct-host model material, separate from the frozen native BpmnProcess authoring envelope.</summary>
public sealed class BpmnModelDefinition
{
    public BpmnModelDefinition(int formatVersion, JsonElement process, IReadOnlyList<string> humanBindings)
        : this(formatVersion, process, humanBindings, null) { }

    public BpmnModelDefinition(int formatVersion, JsonElement process, IReadOnlyList<string> humanBindings,
        IReadOnlyList<BpmnNativeServiceBinding>? serviceBindings)
        : this(formatVersion, process, humanBindings, serviceBindings, null) { }

    public BpmnModelDefinition(int formatVersion, JsonElement process, IReadOnlyList<string> humanBindings,
        IReadOnlyList<BpmnNativeServiceBinding>? serviceBindings, IReadOnlyList<string>? timerBindings)
        : this(formatVersion, process, humanBindings, serviceBindings, timerBindings, null) { }

    [JsonConstructor]
    public BpmnModelDefinition(int formatVersion, JsonElement process, IReadOnlyList<string> humanBindings,
        IReadOnlyList<BpmnNativeServiceBinding>? serviceBindings, IReadOnlyList<string>? timerBindings,
        IReadOnlyList<BpmnNativeEventBinding>? nativeEventBindings)
    {
        if (formatVersion is not (1 or 2 or 3 or 4 or 5)) throw new NotSupportedException("The BPMN model definition format is unsupported.");
        if (process.ValueKind != JsonValueKind.Object) throw new ArgumentException("A BPMN process must be an object.", nameof(process));
        ArgumentNullException.ThrowIfNull(humanBindings);
        if (humanBindings.Any(string.IsNullOrWhiteSpace) || humanBindings.Distinct(StringComparer.Ordinal).Count() != humanBindings.Count)
            throw new ArgumentException("BPMN human binding identities must be nonblank and unique.", nameof(humanBindings));
        FormatVersion = formatVersion; Process = process.Clone();
        HumanBindings = Array.AsReadOnly(humanBindings.Order(StringComparer.Ordinal).ToArray());
        if (formatVersion == 5 ? nativeEventBindings is not { Count: > 0 } || humanBindings.Count != 0 || serviceBindings is not null : nativeEventBindings is not null)
            throw new ArgumentException("Native event work requires model format 5 and its complete, exclusive binding inventory.", nameof(nativeEventBindings));
        if (nativeEventBindings is not null)
        {
            if (nativeEventBindings.Any(binding => binding is null) ||
                nativeEventBindings.Select(binding => binding.BindingId).Distinct(StringComparer.Ordinal).Count() != nativeEventBindings.Count)
                throw new ArgumentException("Native event binding identities must be unique.", nameof(nativeEventBindings));
            NativeEventBindings = Array.AsReadOnly(nativeEventBindings.OrderBy(binding => binding.BindingId, StringComparer.Ordinal).ToArray());
        }
        if (formatVersion == 4 ? timerBindings is not { Count: > 0 } : timerBindings is not null)
            throw new ArgumentException("Duration timers require model format 4 and its complete binding inventory.", nameof(timerBindings));
        if (timerBindings is not null)
        {
            if (timerBindings.Any(string.IsNullOrWhiteSpace) || timerBindings.Distinct(StringComparer.Ordinal).Count() != timerBindings.Count ||
                timerBindings.Any(binding => HumanBindings.Contains(binding, StringComparer.Ordinal)))
                throw new ArgumentException("Duration timer binding identities must be nonblank, unique and distinct from human work.", nameof(timerBindings));
            TimerBindings = Array.AsReadOnly(timerBindings.Order(StringComparer.Ordinal).ToArray());
        }
        if (formatVersion == 1 && serviceBindings is not null || formatVersion is 2 or 3 && serviceBindings is not { Count: > 0 })
            throw new ArgumentException("The native service profile requires its explicit versioned binding inventory.", nameof(serviceBindings));
        if (serviceBindings is not null)
        {
            if (serviceBindings.Any(binding => binding is null) ||
                serviceBindings.Select(binding => binding.BindingId).Distinct(StringComparer.Ordinal).Count() != serviceBindings.Count ||
                serviceBindings.Any(binding => HumanBindings.Contains(binding.BindingId, StringComparer.Ordinal) || TimerBindings?.Contains(binding.BindingId, StringComparer.Ordinal) == true))
                throw new ArgumentException("Human and native service binding identities must be distinct.", nameof(serviceBindings));
            ServiceBindings = Array.AsReadOnly(serviceBindings.OrderBy(binding => binding.BindingId, StringComparer.Ordinal).ToArray());
            if (formatVersion == 2 && ServiceBindings.Any(binding => binding.FaultPolicy is not null) ||
                formatVersion is 3 or 4 && ServiceBindings.Any(binding => binding.FaultPolicy is null))
                throw new ArgumentException("Explicit service fault policies require model format 3 and its complete policy inventory.", nameof(serviceBindings));
        }
    }
    public int FormatVersion { get; }
    public JsonElement Process { get; }
    public IReadOnlyList<string> HumanBindings { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BpmnNativeServiceBinding>? ServiceBindings { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? TimerBindings { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<BpmnNativeEventBinding>? NativeEventBindings { get; }
}

/// <summary>One signal catch's complete published native Event worker pin.</summary>
public sealed class BpmnNativeEventBinding
{
    public BpmnNativeEventBinding(string bindingId, WorkflowExecutableIdentity executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentNullException.ThrowIfNull(executable);
        foreach (var value in new[] { executable.ArtifactId, executable.ArtifactHash, executable.DefinitionId,
                     executable.DefinitionVersionId, executable.ArtifactVersion }) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        BindingId = bindingId; Executable = executable;
    }
    public string BindingId { get; }
    public WorkflowExecutableIdentity Executable { get; }
}

/// <summary>One service task's complete published native worker pin and modeled business-error code.</summary>
public sealed class BpmnNativeServiceBinding
{
    public BpmnNativeServiceBinding(string bindingId, WorkflowExecutableIdentity executable, string businessErrorCode)
        : this(bindingId, executable, businessErrorCode, null) { }

    [JsonConstructor]
    public BpmnNativeServiceBinding(string bindingId, WorkflowExecutableIdentity executable, string businessErrorCode,
        BpmnServiceFaultPolicy? faultPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(businessErrorCode);
        ArgumentNullException.ThrowIfNull(executable);
        foreach (var value in new[] { executable.ArtifactId, executable.ArtifactHash, executable.DefinitionId,
                     executable.DefinitionVersionId, executable.ArtifactVersion }) ArgumentException.ThrowIfNullOrWhiteSpace(value);
        BindingId = bindingId; Executable = executable; BusinessErrorCode = businessErrorCode; FaultPolicy = faultPolicy;
    }
    public string BindingId { get; }
    public WorkflowExecutableIdentity Executable { get; }
    public string BusinessErrorCode { get; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BpmnServiceFaultPolicy? FaultPolicy { get; }
}

/// <summary>Disposition of a technical native-worker failure; modeled business errors keep their own semantics.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<BpmnServiceFaultDisposition>))]
public enum BpmnServiceFaultDisposition { FinalFault, HoldForRetry }

/// <summary>Immutable attempt budget, including the first physical attempt, pinned by model format 3.</summary>
public sealed class BpmnServiceFaultPolicy
{
    public const int MaximumAttempts = 32;

    public BpmnServiceFaultPolicy(int formatVersion, BpmnServiceFaultDisposition disposition, int maxAttempts)
    {
        if (formatVersion != 1) throw new NotSupportedException("The service fault policy format is unsupported.");
        if (disposition is not (BpmnServiceFaultDisposition.FinalFault or BpmnServiceFaultDisposition.HoldForRetry))
            throw new ArgumentOutOfRangeException(nameof(disposition));
        if (maxAttempts is < 1 or > MaximumAttempts) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        if (disposition == BpmnServiceFaultDisposition.FinalFault && maxAttempts != 1)
            throw new ArgumentException("FinalFault permits one physical attempt.", nameof(maxAttempts));
        FormatVersion = formatVersion; Disposition = disposition; MaxAttempts = maxAttempts;
    }
    public int FormatVersion { get; }
    public BpmnServiceFaultDisposition Disposition { get; }
    public int MaxAttempts { get; }
}

/// <summary>The protocol and immutable material profile interpreted by the direct BPMN adapter.</summary>
public static class BpmnModelProtocol
{
    public const string Key = "elsa.bpmn";
    public const int Version = 1;
    public const string Profile = "foundation-direct-v1";
    public const string NativeWorkProfile = "foundation-native-work-v1";
}
