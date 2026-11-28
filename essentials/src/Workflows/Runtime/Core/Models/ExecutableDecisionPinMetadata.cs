namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Metadata keys for the frozen decision bound to a compiled activity node.</summary>
public static class ExecutableDecisionPinMetadata
{
    /// <summary>The historical pin, retained under its original executable identity.</summary>
    public const string HistoricalKey = "decisions.artifact.v1";

    /// <summary>A pin included in current executable content identity.</summary>
    public const string BehavioralKey = "decisions.artifact.v2";

    /// <summary>Reads one supported pin. Missing, ambiguous and oversized material is refused.</summary>
    public static string? Read(IReadOnlyDictionary<string, string> metadata)
    {
        var hasBehavioral = metadata.TryGetValue(BehavioralKey, out var behavioral);
        var hasHistorical = metadata.TryGetValue(HistoricalKey, out var historical);
        if (hasBehavioral == hasHistorical)
            return null;

        var pin = hasBehavioral ? behavioral : historical;
        return pin is { Length: > 0 and <= 2 * 1024 * 1024 } ? pin : null;
    }
}
