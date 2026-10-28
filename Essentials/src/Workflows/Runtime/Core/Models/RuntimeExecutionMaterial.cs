using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Names the semantic protocol that interprets model material and committed model state.</summary>
public sealed record RuntimeExecutionProtocol
{
    public RuntimeExecutionProtocol(string key, int version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        if (version <= 0) throw new ArgumentOutOfRangeException(nameof(version));
        Key = key; Version = version;
    }

    public string Key { get; }
    public int Version { get; }
}

/// <summary>One model binding's immutable native or model artifact dependency.</summary>
public sealed class RuntimeModelDependency
{
    public RuntimeModelDependency(string bindingId, WorkflowExecutableIdentity executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bindingId);
        RuntimeModelMaterial.ValidatePin(executable);
        BindingId = bindingId; Executable = executable;
    }

    public string BindingId { get; }
    public WorkflowExecutableIdentity Executable { get; }
}

/// <summary>A declared output available to a model's consumers without loading its interpreter.</summary>
public sealed class RuntimeModelDeclaredOutput
{
    public RuntimeModelDeclaredOutput(string name, TypeReference type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(type.Alias);
        if (!Enum.IsDefined(type.CollectionKind)) throw new ArgumentOutOfRangeException(nameof(type));
        Name = name; Type = type;
    }

    public string Name { get; }
    public TypeReference Type { get; }
}

/// <summary>Versioned immutable model material, with no native root or activity-execution substitute.</summary>
/// <remarks>The selected artifact provider owns its content hash, physical incarnation, leases and retention.</remarks>
public sealed class RuntimeModelMaterial
{
    public const int CurrentFormatVersion = 1;

    public RuntimeModelMaterial(int formatVersion, WorkflowExecutableIdentity identity, RuntimeExecutionProtocol protocol,
        string profile, JsonElement definition, WorkflowExecutableInputContract inputContract,
        IReadOnlyList<RuntimeModelDeclaredOutput> outputs, IReadOnlyList<RuntimeModelDependency> dependencies,
        IReadOnlyList<RuntimeRequirement> runtimeRequirements)
    {
        if (formatVersion != CurrentFormatVersion)
            throw new NotSupportedException($"Model material format '{formatVersion}' is not supported.");
        ValidatePin(identity);
        ArgumentNullException.ThrowIfNull(protocol);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile);
        ArgumentNullException.ThrowIfNull(inputContract);
        ArgumentNullException.ThrowIfNull(outputs);
        ArgumentNullException.ThrowIfNull(dependencies);
        ArgumentNullException.ThrowIfNull(runtimeRequirements);
        if (definition.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("A model definition must be a defined JSON object.", nameof(definition));
        if (outputs.Any(item => item is null) || dependencies.Any(item => item is null) || runtimeRequirements.Any(item => item is null))
            throw new ArgumentException("Model material collections cannot contain null entries.");
        var orderedOutputs = outputs.OrderBy(item => item.Name, StringComparer.Ordinal).ToArray();
        var orderedDependencies = dependencies.OrderBy(item => item.BindingId, StringComparer.Ordinal).ToArray();
        if (orderedOutputs.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count() != orderedOutputs.Length)
            throw new ArgumentException("Declared model output names must be unique.", nameof(outputs));
        if (orderedDependencies.Select(item => item.BindingId).Distinct(StringComparer.Ordinal).Count() != orderedDependencies.Length)
            throw new ArgumentException("Model dependency bindings must be unique.", nameof(dependencies));
        FormatVersion = formatVersion; Identity = identity; Protocol = protocol; Profile = profile;
        Definition = definition.Clone(); InputContract = inputContract;
        Outputs = Array.AsReadOnly(orderedOutputs); Dependencies = Array.AsReadOnly(orderedDependencies);
        RuntimeRequirements = Array.AsReadOnly(runtimeRequirements.Distinct()
            .OrderBy(item => item.ConsumerKey, StringComparer.Ordinal).ThenBy(item => item.SchemaVersion, StringComparer.Ordinal).ToArray());
    }

    public int FormatVersion { get; }
    public WorkflowExecutableIdentity Identity { get; }
    public RuntimeExecutionProtocol Protocol { get; }
    public string Profile { get; }
    public JsonElement Definition { get; }
    public WorkflowExecutableInputContract InputContract { get; }
    public IReadOnlyList<RuntimeModelDeclaredOutput> Outputs { get; }
    public IReadOnlyList<RuntimeModelDependency> Dependencies { get; }
    public IReadOnlyList<RuntimeRequirement> RuntimeRequirements { get; }

    internal static void ValidatePin(WorkflowExecutableIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.ArtifactId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.ArtifactHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.DefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.DefinitionVersionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(identity.ArtifactVersion);
    }
}

/// <summary>The artifact provider's exact native or model material and current physical incarnation.</summary>
/// <remarks>This read result does not change the native executable's stored representation.</remarks>
public sealed class RuntimeExecutionMaterial
{
    public RuntimeExecutionMaterial(WorkflowExecutable nativeWorkflow, string incarnationId)
    {
        ArgumentNullException.ThrowIfNull(nativeWorkflow);
        ArgumentException.ThrowIfNullOrWhiteSpace(incarnationId);
        NativeWorkflow = nativeWorkflow; Identity = nativeWorkflow.Identity; IncarnationId = incarnationId;
    }

    public RuntimeExecutionMaterial(RuntimeModelMaterial model, string incarnationId)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(incarnationId);
        Model = model; Identity = model.Identity; IncarnationId = incarnationId;
    }

    public WorkflowExecutableIdentity Identity { get; }
    public string IncarnationId { get; }
    public WorkflowExecutable? NativeWorkflow { get; }
    public RuntimeModelMaterial? Model { get; }
}
