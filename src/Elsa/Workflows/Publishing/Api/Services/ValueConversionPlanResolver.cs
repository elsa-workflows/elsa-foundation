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
            if (!ValueConversionCompatibility.SameType(sourceType, targetType))
                throw Reject(sourceType, sourceRepresentation, targetType, mode, profile,
                    "mode 'None' requires an exact source and target contract match.");

            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.Identity, null, limits, options);
        }

        if (mode is ValueConversionMode.Json or ValueConversionMode.Xml or ValueConversionMode.Profile)
            return ResolveExplicitProfile(sourceType, sourceRepresentation, targetType, mode, profile, limits, options);

        if (ValueConversionCompatibility.SameType(sourceType, targetType))
            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.Identity, null, limits, options);

        if (ValueConversionCompatibility.IsNullableCompatibility(sourceType, targetType))
            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.NullableCompatibility, null, limits, options);

        if (ValueConversionCompatibility.IsSafeNumericWidening(sourceType, targetType))
            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.NumericWidening, null, limits, options);

        if (ValueConversionCompatibility.CanRecursivelyWidenCollection(sourceType, targetType))
            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.RecursiveCollection, null, limits, options);

        if (ValueConversionCompatibility.IsCanonicalAnyTarget(targetType) && sourceRepresentation is ValueRepresentation.TypedValue or ValueRepresentation.StructuredValue or ValueRepresentation.TextValue)
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

        if (mode == ValueConversionMode.Xml && ValueConversionCompatibility.IsCanonicalAnyTarget(targetType))
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

    private static string ExplainAutomaticRejection(ValueTypeDescriptor source, ValueTypeDescriptor target)
    {
        if (!ValueConversionCompatibility.IsSafeCollectionShapeConversion(source.CollectionKind, target.CollectionKind) &&
            (source.CollectionKind != CollectionKind.Single || target.CollectionKind != CollectionKind.Single))
            return "collection shape changes are ambiguous under Auto.";

        if (ValueConversionCompatibility.IsNumeric(source.Alias) && ValueConversionCompatibility.IsNumeric(target.Alias))
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
        new Dictionary<(string Id, string Version), ValueConversionProfileDefinition>();

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
