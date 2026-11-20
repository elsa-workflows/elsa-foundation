using System.Collections.Immutable;
using System.Text.Json;

namespace Elsa.Workflows.Design.Validations.Core.Models;

/// <summary>
/// The one reading of an authored member path, shared by the data selection analysis and the binding compiler so the
/// two never disagree about which paths are valid. A member path is a nonempty array of strings, kept exactly as
/// authored (never split, trimmed or case-normalised), with at most <see cref="MaximumSegments"/> segments and at most
/// <see cref="MaximumLength"/> characters in total. The compiled read selection enforces the same bounds.
/// </summary>
public static class ActivityDataMemberPath
{
    /// <summary>Most segments a member path may have.</summary>
    public const int MaximumSegments = 64;

    /// <summary>Most characters a member path may have in total.</summary>
    public const int MaximumLength = 4096;

    /// <summary>Reads the <c>memberPath</c> property of an authored read payload.</summary>
    public static bool TryRead(JsonElement payload, out ImmutableArray<string> memberPath)
    {
        memberPath = [];
        if (payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty("memberPath", out var raw) || raw.ValueKind != JsonValueKind.Array)
            return false;
        var count = raw.GetArrayLength();
        if (count is 0 or > MaximumSegments)
            return false;
        var segments = ImmutableArray.CreateBuilder<string>(count);
        var length = 0;
        foreach (var item in raw.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;
            var segment = item.GetString()!;
            length += segment.Length;
            if (length > MaximumLength)
                return false;
            segments.Add(segment);
        }

        memberPath = segments.MoveToImmutable();
        return true;
    }

    /// <summary>Whether already-split segments are within the bounds.</summary>
    public static bool IsWithinBounds(IReadOnlyCollection<string> segments) =>
        segments.Count is > 0 and <= MaximumSegments && segments.All(segment => segment is not null) &&
        segments.Sum(segment => segment.Length) <= MaximumLength;
}
