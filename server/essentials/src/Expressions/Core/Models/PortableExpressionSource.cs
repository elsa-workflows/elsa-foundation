using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Elsa.Expressions.Core.Models;

/// <summary>Source text and the argument naming convention of a portable boundary expression.</summary>
public sealed class PortableExpressionSource
{
    private PortableExpressionSource(string text, bool usesReferenceKeys)
    {
        Text = text;
        UsesReferenceKeys = usesReferenceKeys;
    }

    public string Text { get; }
    public bool UsesReferenceKeys { get; }

    public static bool TryRead(JsonElement value, bool allowReferenceKeys, [NotNullWhen(true)] out PortableExpressionSource? source)
    {
        source = null;
        if (value.ValueKind == JsonValueKind.String)
            source = new(value.GetString()!, false);
        else if (allowReferenceKeys && value.ValueKind == JsonValueKind.Object &&
                 value.TryGetProperty("inputBinding", out var binding) && binding.ValueKind == JsonValueKind.String &&
                 StringComparer.Ordinal.Equals(binding.GetString(), "referenceKey") &&
                 value.TryGetProperty("source", out var text) && text.ValueKind == JsonValueKind.String)
            source = new(text.GetString()!, true);

        return source is not null;
    }
}
