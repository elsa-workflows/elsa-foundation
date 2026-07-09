using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public static class DefaultDiagnosticSnapshotFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] SensitiveNameFragments = ["password", "secret", "token", "apikey", "api_key", "authorization", "credential"];

    public static JsonElement Capture(object? value, string? valueName = null, RuntimeValueTypeDescriptor? type = null, DiagnosticSnapshotLimits? limits = null)
    {
        var snapshot = CaptureSnapshot(value, valueName, type, limits ?? DiagnosticSnapshotLimits.Default);
        return JsonSerializer.SerializeToElement(snapshot, snapshot.GetType(), SerializerOptions);
    }

    public static DiagnosticSnapshot CaptureSnapshot(object? value, string? valueName = null, RuntimeValueTypeDescriptor? type = null, DiagnosticSnapshotLimits? limits = null)
    {
        limits ??= DiagnosticSnapshotLimits.Default;

        if (IsSensitiveName(valueName))
            return Redacted("sensitive-name");

        try
        {
            if (value is null)
                return new DiagnosticSnapshot("null") { TypeName = type?.Id };

            var json = value is JsonElement element
                ? element.Clone()
                : JsonSerializer.SerializeToElement(value, value.GetType());

            var budget = new SnapshotBudget(limits.MaxTotalBytes);
            return CaptureJson(json, type?.Id ?? value.GetType().FullName, valueName, depth: 0, limits, budget);
        }
        catch
        {
            return new DiagnosticSnapshot("error") { Reason = "snapshot-failed", DisplayName = "The value could not be safely inspected." };
        }
    }

    private static DiagnosticSnapshot CaptureJson(JsonElement value, string? typeName, string? valueName, int depth, DiagnosticSnapshotLimits limits, SnapshotBudget budget)
    {
        if (IsSensitiveName(valueName))
            return Redacted("sensitive-name");

        if (depth >= limits.MaxDepth)
            return new DiagnosticSnapshot("truncated") { Reason = "max-depth" };

        if (!budget.TrySpend(value.GetRawText().Length))
            return new DiagnosticSnapshot("truncated") { Reason = "max-total-bytes" };

        return value.ValueKind switch
        {
            JsonValueKind.Null => new DiagnosticSnapshot("null") { TypeName = typeName },
            JsonValueKind.True => new DiagnosticSnapshot("scalar") { TypeName = typeName ?? "Boolean", Value = true },
            JsonValueKind.False => new DiagnosticSnapshot("scalar") { TypeName = typeName ?? "Boolean", Value = false },
            JsonValueKind.Number => new DiagnosticSnapshot("number") { TypeName = typeName ?? "Number", Value = value.TryGetInt64(out var integer) ? integer : value.GetDouble() },
            JsonValueKind.String => CaptureString(value.GetString() ?? "", typeName, limits),
            JsonValueKind.Object => CaptureObject(value, typeName, depth, limits, budget),
            JsonValueKind.Array => CaptureArray(value, typeName, depth, limits, budget),
            _ => new DiagnosticSnapshot("unsupported") { TypeName = typeName, Reason = "unsupported-json-kind" }
        };
    }

    private static DiagnosticSnapshot CaptureString(string value, string? typeName, DiagnosticSnapshotLimits limits)
    {
        var truncated = value.Length > limits.MaxStringLength;
        return new DiagnosticSnapshot("string")
        {
            TypeName = typeName ?? "String",
            Preview = truncated ? value[..limits.MaxStringLength] : value,
            Length = value.Length,
            Truncated = truncated
        };
    }

    private static DiagnosticSnapshot CaptureObject(JsonElement value, string? typeName, int depth, DiagnosticSnapshotLimits limits, SnapshotBudget budget)
    {
        var sourceProperties = value.EnumerateObject().ToArray();
        var properties = sourceProperties
            .Take(limits.MaxObjectProperties)
            .Select(property => new DiagnosticSnapshotProperty(property.Name, CaptureJson(property.Value, null, property.Name, depth + 1, limits, budget)))
            .ToList();

        var omittedCount = Math.Max(0, sourceProperties.Length - properties.Count);
        if (omittedCount > 0)
            properties.Add(new DiagnosticSnapshotProperty("...", new DiagnosticSnapshot("truncated") { Reason = "max-properties", OmittedCount = omittedCount }));

        return new DiagnosticSnapshot("object")
        {
            TypeName = typeName,
            Properties = properties,
            Truncated = omittedCount > 0
        };
    }

    private static DiagnosticSnapshot CaptureArray(JsonElement value, string? typeName, int depth, DiagnosticSnapshotLimits limits, SnapshotBudget budget)
    {
        var sourceItems = value.EnumerateArray().ToArray();
        var items = sourceItems
            .Take(limits.MaxArrayItems)
            .Select(item => CaptureJson(item, null, null, depth + 1, limits, budget))
            .ToList();

        var omittedCount = Math.Max(0, sourceItems.Length - items.Count);
        if (omittedCount > 0)
            items.Add(new DiagnosticSnapshot("truncated") { Reason = "max-array-items", OmittedCount = omittedCount });

        return new DiagnosticSnapshot("array")
        {
            TypeName = typeName,
            Items = items,
            ItemCount = sourceItems.Length,
            Truncated = omittedCount > 0
        };
    }

    private static DiagnosticSnapshot Redacted(string reason) => new("redacted") { Reason = reason, DisplayName = "Protected value" };

    private static bool IsSensitiveName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        SensitiveNameFragments.Any(fragment => name.Replace("-", "", StringComparison.Ordinal).Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private sealed class SnapshotBudget(int remaining)
    {
        private int _remaining = remaining;

        public bool TrySpend(int bytes)
        {
            if (_remaining <= 0)
                return false;

            _remaining -= Math.Max(1, bytes);
            return _remaining >= 0;
        }
    }
}
