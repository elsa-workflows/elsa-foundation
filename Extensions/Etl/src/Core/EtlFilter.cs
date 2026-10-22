using System.Text.Json;

namespace Elsa.Etl.Core;

/// <summary>A deterministic comparison of one mapped field with a JSON literal.</summary>
public sealed record EtlFilter
{
    /// <summary>Creates a filter and snapshots its JSON literal.</summary>
    public EtlFilter(string field, EtlComparison comparison, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(field);

        Field = field;
        Comparison = comparison;
        Value = value.Clone();
    }

    /// <summary>The source or lookup field to compare.</summary>
    public string Field { get; }

    /// <summary>The supported equality comparison.</summary>
    public EtlComparison Comparison { get; }

    /// <summary>The immutable JSON literal used as the comparison value.</summary>
    public JsonElement Value { get; }
}
