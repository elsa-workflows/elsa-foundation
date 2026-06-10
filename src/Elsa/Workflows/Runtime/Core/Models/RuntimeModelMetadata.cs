using System.Collections.ObjectModel;

namespace Elsa.Workflows.Runtime.Core.Models;

internal static class RuntimeModelMetadata
{
    public static IReadOnlyDictionary<string, string> Snapshot(IReadOnlyDictionary<string, string>? metadata = null) =>
        new ReadOnlyDictionary<string, string>((metadata ?? new Dictionary<string, string>()).ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal));
}
