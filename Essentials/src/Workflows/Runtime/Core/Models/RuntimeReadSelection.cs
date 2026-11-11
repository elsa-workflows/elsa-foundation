using Elsa.Activities.Runtime.Core.Models;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The immutable read a member source makes inside its source value: an ordered path of exact serialized member
/// names, and what publication established about the member it reaches. It has no write semantics.
/// </summary>
/// <remarks>
/// <see cref="RuntimeReadSelectionMode.Known"/> pins the selected member's type, with presence and nullability
/// composed from the root and every prefix. <see cref="RuntimeReadSelectionMode.DeferredFit"/> leaves the source
/// shape unknown, and the runtime checks the actual value against the destination before the input snapshot.
/// </remarks>
public sealed class RuntimeReadSelection
{
    /// <summary>The only selection schema this runtime reads.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>The engine requirement an executable or template carries when any of its bindings has a selection.</summary>
    public const string RequirementKey = "elsa.runtime.input-member-selection";

    internal const string RequirementSchemaVersion = "1";
    internal const int MaximumSegments = 64;
    internal const int MaximumPathLength = 4096;

    [JsonConstructor]
    public RuntimeReadSelection(
        int schemaVersion,
        IReadOnlyList<string> segments,
        RuntimeReadSelectionMode mode,
        ValueTypeDescriptor? selectedType = null,
        bool? mayBeAbsent = null,
        bool? mayBeNull = null,
        ValueRepresentation? representation = null,
        string? profileRevision = null)
    {
        if (schemaVersion != CurrentSchemaVersion)
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), schemaVersion,
                $"Read selection schema '{schemaVersion}' is not supported; this runtime reads schema '{CurrentSchemaVersion}'.");
        ArgumentNullException.ThrowIfNull(segments);
        if (segments.Count is 0 or > MaximumSegments)
            throw new ArgumentException($"A read selection has 1 to {MaximumSegments} member segments.", nameof(segments));
        var copied = new string[segments.Count];
        var length = 0;
        for (var index = 0; index < segments.Count; index++)
        {
            copied[index] = segments[index] ?? throw new ArgumentException("A read selection segment cannot be null.", nameof(segments));
            length += copied[index].Length;
        }
        if (length > MaximumPathLength)
            throw new ArgumentException($"A read selection path has at most {MaximumPathLength} characters in total.", nameof(segments));
        if (!Enum.IsDefined(mode))
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown read selection mode.");
        if (profileRevision is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(profileRevision);
        if (mode == RuntimeReadSelectionMode.Known)
        {
            if (selectedType is null)
                throw new ArgumentException("A known read selection pins its selected type.", nameof(selectedType));
            if (profileRevision is null)
                throw new ArgumentException("A known read selection pins the serialization profile revision its facts came from.", nameof(profileRevision));
        }
        else if (selectedType is not null || mayBeAbsent is not null || mayBeNull is not null)
            throw new ArgumentException("A deferred read selection leaves the selected type, presence and nullability unknown.");

        SchemaVersion = schemaVersion;
        Segments = new ReadOnlyCollection<string>(copied);
        Mode = mode;
        SelectedType = selectedType;
        MayBeAbsent = mayBeAbsent;
        MayBeNull = mayBeNull;
        Representation = representation;
        ProfileRevision = profileRevision;
    }

    public int SchemaVersion { get; }

    /// <summary>Exact serialized member names, in order. A dot is part of a name; an empty name is a name.</summary>
    public IReadOnlyList<string> Segments { get; }

    public RuntimeReadSelectionMode Mode { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValueTypeDescriptor? SelectedType { get; }

    /// <summary>Whether the selected member can be absent; <see langword="null"/> when unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MayBeAbsent { get; }

    /// <summary>Whether the selected member, or any prefix, can be null; <see langword="null"/> when unknown.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? MayBeNull { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValueRepresentation? Representation { get; }

    /// <summary>The activity-value serialization profile revision the selection's facts were read under.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ProfileRevision { get; }
}

/// <summary>What publication established about the member a read selection reaches.</summary>
public enum RuntimeReadSelectionMode
{
    /// <summary>The member's type, presence and nullability are pinned from authoritative shape facts.</summary>
    Known = 0,

    /// <summary>The source shape is unknown; the runtime fits the actual value to the destination before the snapshot.</summary>
    DeferredFit = 1
}

/// <summary>Derives the member-selection engine requirement from the bindings an artifact holds.</summary>
internal static class RuntimeReadSelectionRequirement
{
    public static readonly RuntimeRequirement Requirement = new(RuntimeReadSelection.RequirementKey, RuntimeReadSelection.RequirementSchemaVersion);

    public static IEnumerable<RuntimeRequirement> For(IEnumerable<ExecutableNode> nodes, IEnumerable<RuntimeInputBinding?>? otherBindings = null) =>
        nodes.SelectMany(node => node.InputBindings.Values).Concat(otherBindings ?? []).Any(binding => binding?.Selection is not null)
            ? [Requirement]
            : [];
}
