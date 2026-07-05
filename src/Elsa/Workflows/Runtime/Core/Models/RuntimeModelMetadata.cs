using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Mechanical snapshot helper shared by the runtime models (and the engine assembly since the
/// ADR 0033 split): produces an ordinal, read-only defensive copy of a metadata dictionary.
/// </summary>
public static class RuntimeModelMetadata
{
    public static IReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string>? metadata = null) =>
        new ReadOnlyDictionary<string, string>((metadata ?? new Dictionary<string, string>()).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
}
