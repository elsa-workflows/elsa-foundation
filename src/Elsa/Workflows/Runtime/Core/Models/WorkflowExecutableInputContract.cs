using System.Text.Json;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The versioned, immutable workflow-input contract projected into an executable artifact.
/// </summary>
/// <remarks>
/// A missing contract on <see cref="WorkflowExecutable"/> identifies a legacy artifact. An explicitly empty
/// version-one contract identifies a newly compiled workflow that declares no inputs.
/// </remarks>
public sealed class WorkflowExecutableInputContract
{
    public const int CurrentVersion = 1;

    public WorkflowExecutableInputContract(
        int version,
        IReadOnlyCollection<WorkflowDeclaredInput> inputs)
    {
        if (version <= 0)
            throw new ArgumentOutOfRangeException(nameof(version), version, "The input contract version must be positive.");
        ArgumentNullException.ThrowIfNull(inputs);

        var snapshot = inputs
            .Select(input => input ?? throw new ArgumentException("The input contract cannot contain null declarations.", nameof(inputs)))
            .OrderBy(input => input.Name, StringComparer.Ordinal)
            .ToArray();
        var duplicateName = snapshot
            .GroupBy(input => input.Name, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)
            ?.Key;

        if (duplicateName is not null)
            throw new ArgumentException($"The declared workflow input name '{duplicateName}' is duplicated.", nameof(inputs));

        Version = version;
        Inputs = Array.AsReadOnly(snapshot);
    }

    /// <summary>The positive schema version of this contract.</summary>
    public int Version { get; }

    /// <summary>The canonical declared workflow inputs, ordered by ordinal name.</summary>
    public IReadOnlyCollection<WorkflowDeclaredInput> Inputs { get; }
}

/// <summary>
/// One immutable declared workflow input carried by an executable artifact.
/// </summary>
public sealed class WorkflowDeclaredInput
{
    public WorkflowDeclaredInput(
        string name,
        TypeReference type,
        bool isRequired,
        JsonElement? defaultValue = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(type.Alias);
        if (!Enum.IsDefined(type.CollectionKind))
            throw new ArgumentOutOfRangeException(nameof(type), type.CollectionKind, "The declared input collection kind is not supported.");
        if (defaultValue is { ValueKind: JsonValueKind.Undefined })
            throw new ArgumentException("A declared input default must be a defined JSON literal.", nameof(defaultValue));

        Name = name;
        Type = type;
        IsRequired = isRequired;
        DefaultValue = defaultValue?.Clone();
    }

    /// <summary>The case-sensitive workflow-input name.</summary>
    public string Name { get; }

    /// <summary>The shared alias and collection-shape type descriptor.</summary>
    public TypeReference Type { get; }

    /// <summary>Whether a value is required when no supported default is present.</summary>
    public bool IsRequired { get; }

    /// <summary>An optional, detached JSON literal default.</summary>
    public JsonElement? DefaultValue { get; }
}
