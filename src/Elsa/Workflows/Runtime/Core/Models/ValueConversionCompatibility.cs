using System.Text.Json;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Shared publication/runtime compatibility policy for pinned value conversion plans.</summary>
public static class ValueConversionCompatibility
{
    public static bool SameType(ValueTypeDescriptor source, ValueTypeDescriptor target) =>
        source.CollectionKind == target.CollectionKind &&
        SameElementType(source, target);

    public static bool SameElementType(ValueTypeDescriptor source, ValueTypeDescriptor target) =>
        StringComparer.OrdinalIgnoreCase.Equals(NormalizeNamespace(source.Alias), NormalizeNamespace(target.Alias)) &&
        source.SchemaVersion == target.SchemaVersion &&
        SameJson(source.Schema, target.Schema);

    public static bool IsNullableCompatibility(ValueTypeDescriptor source, ValueTypeDescriptor target) =>
        source.CollectionKind == target.CollectionKind &&
        IsNullableElementCompatibility(source, target);

    public static bool IsNullableElementCompatibility(ValueTypeDescriptor source, ValueTypeDescriptor target) =>
        StringComparer.OrdinalIgnoreCase.Equals(NormalizeAlias(source.Alias), TrimNullableSuffix(NormalizeAlias(target.Alias))) &&
        IsNullable(target.Alias) &&
        source.SchemaVersion == target.SchemaVersion &&
        SameJson(source.Schema, target.Schema);

    public static bool IsSafeNumericWidening(ValueTypeDescriptor source, ValueTypeDescriptor target) =>
        source.CollectionKind == CollectionKind.Single &&
        target.CollectionKind == CollectionKind.Single &&
        CanSafelyWidenElement(source, target);

    public static bool CanRecursivelyWidenCollection(ValueTypeDescriptor source, ValueTypeDescriptor target) =>
        source.CollectionKind != CollectionKind.Single &&
        target.CollectionKind != CollectionKind.Single &&
        IsSafeCollectionShapeConversion(source.CollectionKind, target.CollectionKind) &&
        (SameElementType(source, target) || IsNullableElementCompatibility(source, target) || CanSafelyWidenElement(source, target));

    public static bool CanSafelyWidenElement(ValueTypeDescriptor source, ValueTypeDescriptor target)
    {
        if (IsNullable(source.Alias) && !IsNullable(target.Alias))
            return false;
        if (source.SchemaVersion != target.SchemaVersion || !SameJson(source.Schema, target.Schema))
            return false;

        return (NormalizeAlias(source.Alias), NormalizeAlias(target.Alias)) switch
        {
            ("SByte", "Int16" or "Int32" or "Int64" or "Decimal") => true,
            ("Byte", "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" or "UInt64" or "Decimal") => true,
            ("Int16", "Int32" or "Int64" or "Decimal") => true,
            ("UInt16", "Int32" or "UInt32" or "Int64" or "UInt64" or "Decimal") => true,
            ("Int32", "Int64" or "Decimal") => true,
            ("UInt32", "Int64" or "UInt64" or "Decimal") => true,
            ("Int64", "Decimal") => true,
            ("UInt64", "Decimal") => true,
            ("Single", "Double") => true,
            _ => false
        };
    }

    public static bool IsCanonicalAnyTarget(ValueTypeDescriptor target) =>
        target.CollectionKind == CollectionKind.Single &&
        (StringComparer.OrdinalIgnoreCase.Equals(NormalizeAlias(target.Alias), "Elsa.Any") ||
         StringComparer.OrdinalIgnoreCase.Equals(NormalizeAlias(target.Alias), "Any") ||
         StringComparer.OrdinalIgnoreCase.Equals(NormalizeAlias(target.Alias), "JsonNode"));

    public static bool IsSafeCollectionShapeConversion(CollectionKind source, CollectionKind target) =>
        source == target ||
        (source is CollectionKind.Array or CollectionKind.List && target is CollectionKind.Array or CollectionKind.List);

    public static bool IsNumeric(string alias) => NumericRank(alias) >= 0;

    public static string NormalizeAlias(string alias)
    {
        return TrimNullableSuffix(NormalizeNamespace(alias));
    }

    private static string NormalizeNamespace(string alias)
    {
        var normalized = alias.StartsWith("System.", StringComparison.Ordinal)
            ? alias["System.".Length..]
            : alias;
        return normalized;
    }

    public static string TrimNullableSuffix(string alias) => alias.EndsWith("?", StringComparison.Ordinal) ? alias[..^1] : alias;

    public static bool IsNullable(string alias) => alias.EndsWith("?", StringComparison.Ordinal);

    private static bool SameJson(JsonElement? left, JsonElement? right) =>
        left.HasValue == right.HasValue && (!left.HasValue || JsonElement.DeepEquals(left.Value, right!.Value));

    private static int NumericRank(string alias) => NormalizeAlias(alias) switch
    {
        "Byte" or "SByte" => 0,
        "Int16" or "UInt16" => 1,
        "Int32" or "UInt32" => 2,
        "Int64" or "UInt64" => 3,
        "Single" => 4,
        "Double" => 5,
        "Decimal" => 6,
        _ => -1
    };
}
