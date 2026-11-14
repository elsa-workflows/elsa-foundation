using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Pure JSON selection shared by completion and constructor-free prior-result setup.</summary>
public static class RuntimeResultProjectionSelector
{
    /// <summary>Validates all declared projections without constructing CLR values or writing storage.</summary>
    public static IReadOnlyDictionary<string, JsonElement?> Select(ActivityResultContract contract, JsonElement result)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var values = new Dictionary<string, JsonElement?>(StringComparer.Ordinal);
        foreach (var projection in contract.Projections.Values)
        {
            if (!projection.Policy.IsPersistable)
                throw new InvalidOperationException($"VF-ACT-005: Result projection '{projection.Key}' is not persistable.");
            if (!TrySelect(result, projection.Path, out var selected))
            {
                if (projection.IsRequired)
                    throw new InvalidOperationException($"VF-ACT-006: Required result projection '{projection.Key}' was not produced.");
                values.Add(projection.Key, null);
                continue;
            }
            if (projection.IsRequired && selected.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                throw new InvalidOperationException($"VF-ACT-006: Required result projection '{projection.Key}' was null.");
            values.Add(projection.Key, selected.Clone());
        }
        return values;
    }

    private static bool TrySelect(JsonElement root, string path, out JsonElement selected)
    {
        if (StringComparer.Ordinal.Equals(path, "$")) { selected = root; return true; }
        // A literal top-level name wins over nested navigation, including names containing dots.
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(path, out selected)) return true;
        selected = root;
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            if (selected.ValueKind != JsonValueKind.Object || !selected.TryGetProperty(segment, out selected)) return false;
        return true;
    }
}
