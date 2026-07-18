using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Resolves the small, deterministic set of representation-aware conversions publication may pin into a
/// workflow executable. It deliberately does not inspect value contents or host configuration: those are runtime
/// concerns and would make a published executable non-reproducible.
/// </summary>
public sealed class ValueConversionPlanResolver(IValueConversionProfileRegistry? profileRegistry = null)
{
    private IValueConversionProfileRegistry ProfileRegistry => profileRegistry ?? BuiltInValueConversionProfileRegistry.Instance;
    public ValueConversionPlan Resolve(
        ValueTypeDescriptor sourceType,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode = ValueConversionMode.Auto,
        ValueConversionProfileReference? profile = null,
        ValueConversionLimits? limits = null,
        JsonElement? options = null)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(targetType);

        if (sourceRepresentation == ValueRepresentation.TransientResource)
            throw Reject(sourceType, sourceRepresentation, targetType, mode, profile,
                "transient resources cannot cross a durable workflow binding.");

        if (mode == ValueConversionMode.None)
        {
            if (!SameType(sourceType, targetType))
                throw Reject(sourceType, sourceRepresentation, targetType, mode, profile,
                    "mode 'None' requires an exact source and target contract match.");

            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.Identity, null, limits, options);
        }

        if (mode is ValueConversionMode.Json or ValueConversionMode.Xml or ValueConversionMode.Profile)
            return ResolveExplicitProfile(sourceType, sourceRepresentation, targetType, mode, profile, limits, options);

        if (SameType(sourceType, targetType))
            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.Identity, null, limits, options);

        if (IsNullableCompatibility(sourceType, targetType))
            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.NullableCompatibility, null, limits, options);

        if (IsSafeNumericWidening(sourceType, targetType))
            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.NumericWidening, null, limits, options);

        if (CanRecursivelyWidenCollection(sourceType, targetType))
            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.RecursiveCollection, null, limits, options);

        if (IsCanonicalAnyTarget(targetType) && sourceRepresentation is ValueRepresentation.TypedValue or ValueRepresentation.StructuredValue or ValueRepresentation.TextValue)
            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.CanonicalAny, null, limits, options);

        throw Reject(sourceType, sourceRepresentation, targetType, mode, profile, ExplainAutomaticRejection(sourceType, targetType));
    }

    private ValueConversionPlan ResolveExplicitProfile(
        ValueTypeDescriptor sourceType,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionProfileReference? profile,
        ValueConversionLimits? limits,
        JsonElement? options)
    {
        if (sourceRepresentation != ValueRepresentation.FormattedContent)
            throw Reject(sourceType, sourceRepresentation, targetType, mode, profile,
                "an explicit conversion profile requires a source declared as formatted content; ordinary text and binary values are not format-sniffed.");

        var pinnedProfile = mode switch
        {
            ValueConversionMode.Json => new ValueConversionProfileReference("elsa.json", "1"),
            ValueConversionMode.Xml => new ValueConversionProfileReference("elsa.xml", "1"),
            ValueConversionMode.Profile when profile is not null => profile,
            _ => throw Reject(sourceType, sourceRepresentation, targetType, mode, profile,
                "named-profile mode requires a profile id and version.")
        };

        if (mode == ValueConversionMode.Xml && IsCanonicalAnyTarget(targetType))
            throw Reject(sourceType, sourceRepresentation, targetType, mode, pinnedProfile,
                "XML has no universal canonical Any projection; select a documented XML-to-JSON profile instead.");

        if (!ProfileRegistry.TryGet(pinnedProfile, out var definition))
            throw Reject(sourceType, sourceRepresentation, targetType, mode, pinnedProfile,
                "the requested profile id and version are not available to publication.");

        if (!definition.Supports(sourceRepresentation, targetType))
            throw Reject(sourceType, sourceRepresentation, targetType, mode, pinnedProfile,
                "the requested profile does not support this source representation and target contract.");

        return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.Profile, pinnedProfile, limits, options);
    }

    private static ValueConversionPlan Create(
        ValueTypeDescriptor sourceType,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionOperation operation,
        ValueConversionProfileReference? profile,
        ValueConversionLimits? limits,
        JsonElement? options) =>
        new(
            ValueConversionPlan.CurrentSchemaVersion,
            sourceRepresentation,
            sourceType,
            targetType,
            mode,
            operation,
            profile,
            limits,
            options);

    private static bool SameType(ValueTypeDescriptor source, ValueTypeDescriptor target) =>
        source.CollectionKind == target.CollectionKind &&
        StringComparer.OrdinalIgnoreCase.Equals(source.Alias, target.Alias) &&
        source.SchemaVersion == target.SchemaVersion &&
        SameJson(source.Schema, target.Schema);

    private static bool IsNullableCompatibility(ValueTypeDescriptor source, ValueTypeDescriptor target) =>
        source.CollectionKind == target.CollectionKind &&
        StringComparer.OrdinalIgnoreCase.Equals(source.Alias, TrimNullableSuffix(target.Alias)) &&
        target.Alias.EndsWith("?", StringComparison.Ordinal) &&
        source.SchemaVersion == target.SchemaVersion &&
        SameJson(source.Schema, target.Schema);

    private static bool CanRecursivelyWidenCollection(ValueTypeDescriptor source, ValueTypeDescriptor target)
    {
        if (source.CollectionKind == CollectionKind.Single || target.CollectionKind == CollectionKind.Single ||
            !IsSafeCollectionShapeConversion(source.CollectionKind, target.CollectionKind))
            return false;

        var sourceElement = new ValueTypeDescriptor(source.Alias, CollectionKind.Single, source.Schema, source.SchemaVersion);
        var targetElement = new ValueTypeDescriptor(target.Alias, CollectionKind.Single, target.Schema, target.SchemaVersion);
        return SameType(sourceElement, targetElement) ||
               IsNullableCompatibility(sourceElement, targetElement) ||
               IsSafeNumericWidening(sourceElement, targetElement);
    }

    private static bool IsSafeNumericWidening(ValueTypeDescriptor source, ValueTypeDescriptor target)
    {
        if (source.CollectionKind != CollectionKind.Single ||
            target.CollectionKind != CollectionKind.Single ||
            (IsNullable(source.Alias) && !IsNullable(target.Alias)) ||
            source.SchemaVersion != target.SchemaVersion ||
            !SameJson(source.Schema, target.Schema))
            return false;

        var sourceAlias = TrimNullableSuffix(source.Alias);
        var targetAlias = TrimNullableSuffix(target.Alias);
        return (sourceAlias, targetAlias) switch
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

    private static bool IsCanonicalAnyTarget(ValueTypeDescriptor target) =>
        target.CollectionKind == CollectionKind.Single &&
        (StringComparer.OrdinalIgnoreCase.Equals(target.Alias, "Elsa.Any") ||
         StringComparer.OrdinalIgnoreCase.Equals(target.Alias, "Any") ||
         StringComparer.OrdinalIgnoreCase.Equals(target.Alias, "JsonNode"));

    private static string ExplainAutomaticRejection(ValueTypeDescriptor source, ValueTypeDescriptor target)
    {
        if (!IsSafeCollectionShapeConversion(source.CollectionKind, target.CollectionKind) &&
            (source.CollectionKind != CollectionKind.Single || target.CollectionKind != CollectionKind.Single))
            return "collection shape changes are ambiguous under Auto.";

        if (IsNumeric(TrimNullableSuffix(source.Alias)) && IsNumeric(TrimNullableSuffix(target.Alias)))
            return "numeric narrowing or cross-family numeric conversion is lossy under Auto.";

        return "no deterministic, supported Auto conversion exists for these contracts.";
    }

    private static ValueConversionPublicationException Reject(
        ValueTypeDescriptor sourceType,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionProfileReference? profile,
        string reason) =>
        new(sourceType, sourceRepresentation, targetType, mode, profile, reason);

    private static string TrimNullableSuffix(string alias) => alias.EndsWith("?", StringComparison.Ordinal) ? alias[..^1] : alias;

    private static bool IsNullable(string alias) => alias.EndsWith("?", StringComparison.Ordinal);

    private static bool SameJson(JsonElement? left, JsonElement? right) =>
        left.HasValue == right.HasValue && (!left.HasValue || JsonElement.DeepEquals(left.Value, right!.Value));

    private static bool IsSafeCollectionShapeConversion(CollectionKind source, CollectionKind target) =>
        source == target ||
        (source is CollectionKind.Array or CollectionKind.List && target is CollectionKind.Array or CollectionKind.List);

    private static bool IsNumeric(string alias) => NumericRank(alias) >= 0;

    private static int NumericRank(string alias) => alias switch
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

/// <summary>Publication-time registry for stable, versioned conversion profile capabilities.</summary>
public interface IValueConversionProfileRegistry
{
    bool TryGet(ValueConversionProfileReference profile, out ValueConversionProfileDefinition definition);
}

/// <summary>Pure conversion-profile capability declaration. Runtime must use the same pinned identity, never discovery.</summary>
public sealed record ValueConversionProfileDefinition(
    ValueConversionProfileReference Profile,
    IReadOnlySet<ValueRepresentation> SupportedSourceRepresentations,
    IReadOnlySet<string> SupportedTargetAliases)
{
    public bool Supports(ValueRepresentation sourceRepresentation, ValueTypeDescriptor targetType) =>
        SupportedSourceRepresentations.Contains(sourceRepresentation) &&
        (SupportedTargetAliases.Contains("*") || SupportedTargetAliases.Contains(targetType.Alias));
}

/// <summary>Small immutable registry used when a host does not contribute additional profiles.</summary>
public sealed class BuiltInValueConversionProfileRegistry : IValueConversionProfileRegistry
{
    public static BuiltInValueConversionProfileRegistry Instance { get; } = new();

    private readonly IReadOnlyDictionary<(string Id, string Version), ValueConversionProfileDefinition> definitions =
        new Dictionary<(string Id, string Version), ValueConversionProfileDefinition>
        {
            [("elsa.json", "1")] = new(
                new ValueConversionProfileReference("elsa.json", "1"),
                new HashSet<ValueRepresentation> { ValueRepresentation.FormattedContent },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" }),
            [("elsa.xml", "1")] = new(
                new ValueConversionProfileReference("elsa.xml", "1"),
                new HashSet<ValueRepresentation> { ValueRepresentation.FormattedContent },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" })
        };

    public bool TryGet(ValueConversionProfileReference profile, out ValueConversionProfileDefinition definition) =>
        definitions.TryGetValue((profile.Id, profile.Version), out definition!);
}

/// <summary>Publication failure with the complete pinned-contract context needed to fix a binding.</summary>
public sealed class ValueConversionPublicationException : ArgumentException
{
    public ValueConversionPublicationException(
        ValueTypeDescriptor sourceType,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionProfileReference? profile,
        string reason)
        : base($"VF-COER-001: Cannot resolve conversion from source representation '{sourceRepresentation}' and contract " +
               $"'{Describe(sourceType)}' to target contract '{Describe(targetType)}' using mode '{mode}'" +
               $"{(profile is null ? string.Empty : $" and profile '{profile.Id}@{profile.Version}'")}: {reason}")
    {
        SourceType = sourceType;
        SourceRepresentation = sourceRepresentation;
        TargetType = targetType;
        Mode = mode;
        Profile = profile;
        Reason = reason;
    }

    public ValueTypeDescriptor SourceType { get; }
    public ValueRepresentation SourceRepresentation { get; }
    public ValueTypeDescriptor TargetType { get; }
    public ValueConversionMode Mode { get; }
    public ValueConversionProfileReference? Profile { get; }
    public string Reason { get; }

    private static string Describe(ValueTypeDescriptor type) =>
        $"{type.Alias}/{type.CollectionKind}/schema:{type.SchemaVersion?.ToString() ?? "none"}";
}
