using System.Text.Json;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>What walking a read selection's segments over a resolved source value found.</summary>
internal enum RuntimeReadSelectionOutcome
{
    /// <summary>Every segment named a member; <see cref="RuntimeReadSelectionResult.Value"/> is the selected member.</summary>
    Present,

    /// <summary>An object lacked the segment at <see cref="RuntimeReadSelectionResult.SegmentIndex"/>.</summary>
    Absent,

    /// <summary>The source (index -1), a prefix, or the selected member itself was JSON null.</summary>
    Null,

    /// <summary>The value at <see cref="RuntimeReadSelectionResult.SegmentIndex"/> was not a JSON object.</summary>
    NotAnObject
}

/// <param name="SegmentIndex">
/// The segment that decided the outcome: for <see cref="RuntimeReadSelectionOutcome.Absent"/> the missing segment, for
/// <see cref="RuntimeReadSelectionOutcome.NotAnObject"/> the segment that could not be looked up, and for
/// <see cref="RuntimeReadSelectionOutcome.Null"/> the segment whose value was null, or -1 when the source itself was.
/// </param>
internal readonly record struct RuntimeReadSelectionResult(
    RuntimeReadSelectionOutcome Outcome,
    JsonElement Value,
    int SegmentIndex,
    JsonValueKind StoppedAt = JsonValueKind.Undefined);

/// <summary>
/// Walks the exact member segments of a read selection over a resolved whole value (#4078). It reads, never writes,
/// and has no path syntax: each segment is one literal member name.
/// </summary>
/// <remarks>
/// <para>
/// Names match exactly: ordinal and case-sensitive. A dot is part of a name, an empty name is a name, and <c>"0"</c>
/// is a member called <c>0</c>, never an index. This deliberately differs from input hydration, which matches a whole
/// value's members to CLR properties case-insensitively under the activity-value profile: a member read of
/// <c>customerName</c> does not find <c>CustomerName</c>.
/// </para>
/// <para>
/// When an object holds the same name more than once, the last occurrence wins, as it does when that object is
/// deserialized. A segment applied to anything that is not an object (an array, string, number or boolean) stops with
/// <see cref="RuntimeReadSelectionOutcome.NotAnObject"/>; there is no implicit indexing or fan-out. A null source or
/// prefix stops with <see cref="RuntimeReadSelectionOutcome.Null"/> without fabricating an object. Traversal visits each
/// member of each traversed object at most once, and a selection has at most 64 segments, so it is bounded by the
/// size of the value it reads.
/// </para>
/// </remarks>
internal static class RuntimeReadSelector
{
    /// <param name="source">The resolved whole value, or <see langword="null"/> when the source is an explicit null.</param>
    public static RuntimeReadSelectionResult Select(JsonElement? source, IReadOnlyList<string> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);
        if (source is not { } current || current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new(RuntimeReadSelectionOutcome.Null, default, -1, JsonValueKind.Null);

        for (var index = 0; index < segments.Count; index++)
        {
            if (current.ValueKind != JsonValueKind.Object)
                return new(RuntimeReadSelectionOutcome.NotAnObject, default, index, current.ValueKind);
            if (!TryGetLast(current, segments[index], out var member))
                return new(RuntimeReadSelectionOutcome.Absent, default, index, JsonValueKind.Object);
            if (member.ValueKind == JsonValueKind.Null)
                return new(RuntimeReadSelectionOutcome.Null, default, index, JsonValueKind.Null);
            current = member;
        }

        return new(RuntimeReadSelectionOutcome.Present, current.Clone(), segments.Count - 1, current.ValueKind);
    }

    // Enumerates explicitly rather than relying on JsonElement.TryGetProperty's handling of repeated names.
    private static bool TryGetLast(JsonElement value, string name, out JsonElement member)
    {
        var found = false;
        member = default;
        foreach (var property in value.EnumerateObject())
        {
            if (!property.NameEquals(name))
                continue;
            member = property.Value;
            found = true;
        }

        return found;
    }
}
