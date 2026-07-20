using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Resolves the small, deterministic set of representation-aware conversions publication may pin into a
/// workflow executable. It deliberately does not inspect value contents or host configuration: those are runtime
/// concerns and would make a published executable non-reproducible.
/// </summary>
public sealed class ValueConversionPlanResolver(
    IValueConversionProfileRegistry? profileRegistry = null,
    IWellKnownTypeRegistry? wellKnownTypeRegistry = null)
{
    private IValueConversionProfileRegistry ProfileRegistry => profileRegistry ?? BuiltInValueConversionProfileRegistry.Instance;
    public ValueConversionPlan Resolve(
        ValueTypeDescriptor sourceType,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode = ValueConversionMode.Auto,
        ValueConversionProfileReference? profile = null,
        ValueConversionLimits? limits = null,
        JsonElement? options = null,
        ValueConversionBindingContext? binding = null)
    {
        ArgumentNullException.ThrowIfNull(sourceType);
        ArgumentNullException.ThrowIfNull(targetType);

        if (sourceRepresentation == ValueRepresentation.TransientResource)
            throw Reject(sourceType, sourceRepresentation, targetType, mode, profile, binding,
                ValueConversionRejectionReason.TransientResourceBinding,
                "transient resources cannot cross a durable workflow binding.");

        if (mode == ValueConversionMode.None)
        {
            if (!ValueConversionCompatibility.SameType(sourceType, targetType))
                throw Reject(sourceType, sourceRepresentation, targetType, mode, profile, binding,
                    ValueConversionRejectionReason.NoneModeContractMismatch,
                    "mode 'None' requires an exact source and target contract match.");

            return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.Identity, null, limits, options);
        }

        if (mode is ValueConversionMode.Json or ValueConversionMode.Xml or ValueConversionMode.Profile)
            return ResolveExplicitProfile(sourceType, sourceRepresentation, targetType, mode, profile, limits, options, binding);

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

        if (sourceRepresentation is ValueRepresentation.FormattedContent or ValueRepresentation.StructuredValue &&
            IsJsonProfileTarget(targetType) &&
            ProfileRegistry.TryGet(new ValueConversionProfileReference("elsa.json", "1"), out var jsonDefinition) &&
            jsonDefinition.Supports(sourceRepresentation, targetType))
        {
            return Create(
                sourceType,
                sourceRepresentation,
                targetType,
                mode,
                ValueConversionOperation.Profile,
                jsonDefinition.Profile,
                limits,
                options);
        }

        if (sourceRepresentation is ValueRepresentation.FormattedContent or ValueRepresentation.StructuredValue &&
            !ValueConversionCompatibility.IsCanonicalAnyTarget(targetType) &&
            !ValueConversionCompatibility.IsJsonObjectTarget(targetType))
        {
            throw Reject(sourceType, sourceRepresentation, targetType, mode, profile, binding,
                ValueConversionRejectionReason.JsonTypedTargetRequired,
                "JSON conversion requires a registered typed target alias.");
        }

        var (autoReason, autoMessage) = ExplainAutomaticRejection(sourceType, targetType);
        throw Reject(sourceType, sourceRepresentation, targetType, mode, profile, binding, autoReason, autoMessage);
    }

    private ValueConversionPlan ResolveExplicitProfile(
        ValueTypeDescriptor sourceType,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionProfileReference? profile,
        ValueConversionLimits? limits,
        JsonElement? options,
        ValueConversionBindingContext? binding)
    {
        if (sourceRepresentation != ValueRepresentation.FormattedContent)
            throw Reject(sourceType, sourceRepresentation, targetType, mode, profile, binding,
                ValueConversionRejectionReason.ExplicitProfileRequiresFormattedContent,
                "an explicit conversion profile requires a source declared as formatted content; ordinary text and binary values are not format-sniffed.");

        var pinnedProfile = mode switch
        {
            ValueConversionMode.Json => new ValueConversionProfileReference("elsa.json", "1"),
            ValueConversionMode.Xml => new ValueConversionProfileReference("elsa.xml", "1"),
            ValueConversionMode.Profile when profile is not null => profile,
            _ => throw Reject(sourceType, sourceRepresentation, targetType, mode, profile, binding,
                ValueConversionRejectionReason.NamedProfileReferenceRequired,
                "named-profile mode requires a profile id and version.")
        };

        if (mode == ValueConversionMode.Xml && ValueConversionCompatibility.IsCanonicalAnyTarget(targetType))
            throw Reject(sourceType, sourceRepresentation, targetType, mode, pinnedProfile, binding,
                ValueConversionRejectionReason.XmlCanonicalAnyUnsupported,
                "XML has no universal canonical Any projection; select a documented XML-to-JSON profile instead.");

        if (!ProfileRegistry.TryGet(pinnedProfile, out var definition))
            throw Reject(sourceType, sourceRepresentation, targetType, mode, pinnedProfile, binding,
                ValueConversionRejectionReason.ProfileNotAvailable,
                "the requested profile id and version are not available to publication.");

        if (!definition.Supports(sourceRepresentation, targetType))
            throw Reject(sourceType, sourceRepresentation, targetType, mode, pinnedProfile, binding,
                ValueConversionRejectionReason.ProfileUnsupportedContract,
                "the requested profile does not support this source representation and target contract.");
        if (StringComparer.Ordinal.Equals(pinnedProfile.Id, "elsa.json") && !IsJsonProfileTarget(targetType))
            throw Reject(sourceType, sourceRepresentation, targetType, mode, pinnedProfile, binding,
                ValueConversionRejectionReason.JsonProfileTargetInvalid,
                "JSON conversion requires Any, JsonObject, or a registered typed target alias.");
        if (StringComparer.Ordinal.Equals(pinnedProfile.Id, "elsa.xml") && !IsXmlProfileTarget(targetType))
            throw Reject(sourceType, sourceRepresentation, targetType, mode, pinnedProfile, binding,
                ValueConversionRejectionReason.XmlProfileTargetInvalid,
                "XML conversion requires a registered typed target alias; XML has no universal canonical Any projection.");

        return Create(sourceType, sourceRepresentation, targetType, mode, ValueConversionOperation.Profile, pinnedProfile, limits, options);
    }

    private bool IsJsonProfileTarget(ValueTypeDescriptor targetType) =>
        ValueConversionCompatibility.IsCanonicalAnyTarget(targetType) ||
        ValueConversionCompatibility.IsJsonObjectTarget(targetType) ||
        IsRegisteredTypedAlias(targetType);

    private bool IsXmlProfileTarget(ValueTypeDescriptor targetType) => IsRegisteredTypedAlias(targetType);

    private bool IsRegisteredTypedAlias(ValueTypeDescriptor targetType)
    {
        if (wellKnownTypeRegistry is null)
            return false;

        return wellKnownTypeRegistry.TryGetTypeOrDefault(targetType.Alias, out var type) &&
               type != typeof(object) &&
               !ValueConversionCompatibility.IsCanonicalAnyTarget(targetType) &&
               !ValueConversionCompatibility.IsJsonObjectTarget(targetType);
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

    private static (ValueConversionRejectionReason Reason, string Message) ExplainAutomaticRejection(ValueTypeDescriptor source, ValueTypeDescriptor target)
    {
        if (!ValueConversionCompatibility.IsSafeCollectionShapeConversion(source.CollectionKind, target.CollectionKind) &&
            (source.CollectionKind != CollectionKind.Single || target.CollectionKind != CollectionKind.Single))
            return (ValueConversionRejectionReason.AutomaticCollectionShapeAmbiguous, "collection shape changes are ambiguous under Auto.");

        if (ValueConversionCompatibility.IsNumeric(source.Alias) && ValueConversionCompatibility.IsNumeric(target.Alias))
            return (ValueConversionRejectionReason.AutomaticNumericLossy, "numeric narrowing or cross-family numeric conversion is lossy under Auto.");

        return (ValueConversionRejectionReason.AutomaticUnsupported, "no deterministic, supported Auto conversion exists for these contracts.");
    }

    private static ValueConversionPublicationException Reject(
        ValueTypeDescriptor sourceType,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionProfileReference? profile,
        ValueConversionBindingContext? binding,
        ValueConversionRejectionReason reasonCode,
        string reason) =>
        ValueConversionPublicationException.Rejected(sourceType, sourceRepresentation, targetType, mode, profile, reasonCode, reason, binding);

}

/// <summary>Publication-time registry for stable, versioned conversion profile capabilities.</summary>
public interface IValueConversionProfileRegistry
{
    bool TryGet(ValueConversionProfileReference profile, out ValueConversionProfileDefinition definition);
    IReadOnlyCollection<ValueConversionProfileDefinition> List() => [];
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
                new HashSet<ValueRepresentation> { ValueRepresentation.FormattedContent, ValueRepresentation.StructuredValue },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" }),
            [("elsa.xml", "1")] = new(
                new ValueConversionProfileReference("elsa.xml", "1"),
                new HashSet<ValueRepresentation> { ValueRepresentation.FormattedContent },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "*" })
        };

    public bool TryGet(ValueConversionProfileReference profile, out ValueConversionProfileDefinition definition) =>
        definitions.TryGetValue((profile.Id, profile.Version), out definition!);

    public IReadOnlyCollection<ValueConversionProfileDefinition> List() =>
        definitions.Values.OrderBy(definition => definition.Profile.Id, StringComparer.Ordinal)
            .ThenBy(definition => definition.Profile.Version, StringComparer.Ordinal)
            .ToArray();
}

/// <summary>The workflow binding a conversion failure originated from, threaded from the compilation context.</summary>
public enum ValueConversionBindingKind
{
    /// <summary>An authored activity input binding.</summary>
    Input,

    /// <summary>An authored activity output capture into a workflow variable.</summary>
    Output,

    /// <summary>A direct activity-result input binding resolved by the result plan linker.</summary>
    ActivityResult
}

/// <summary>
/// Identifies the failing node and binding reference key so a conversion rejection can be reported as
/// structured diagnostics rather than reparsed from a message string.
/// </summary>
public sealed record ValueConversionBindingContext(
    string NodeId,
    string ReferenceKey,
    ValueConversionBindingKind Kind);

/// <summary>Stable machine-readable reason a conversion could not be resolved at publication.</summary>
public enum ValueConversionRejectionReason
{
    TransientResourceBinding,
    NoneModeContractMismatch,
    JsonTypedTargetRequired,
    ExplicitProfileRequiresFormattedContent,
    NamedProfileReferenceRequired,
    XmlCanonicalAnyUnsupported,
    ProfileNotAvailable,
    ProfileUnsupportedContract,
    JsonProfileTargetInvalid,
    XmlProfileTargetInvalid,
    AutomaticCollectionShapeAmbiguous,
    AutomaticNumericLossy,
    AutomaticUnsupported,
    ProducerNodeMissing,
    ProducerResultContractMissing,
    UnknownResultProjection
}

/// <summary>Publication failure with the complete pinned-contract context needed to fix a binding.</summary>
public sealed class ValueConversionPublicationException : ArgumentException
{
    private ValueConversionPublicationException(
        string message,
        ValueConversionRejectionReason reasonCode,
        string reason,
        ValueTypeDescriptor? sourceType,
        ValueRepresentation? sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionProfileReference? profile,
        ValueConversionBindingContext? binding)
        : base(message)
    {
        ReasonCode = reasonCode;
        Reason = reason;
        SourceType = sourceType;
        SourceRepresentation = sourceRepresentation;
        TargetType = targetType;
        Mode = mode;
        Profile = profile;
        Binding = binding;
    }

    /// <summary>Stable reason code identifying the rejection, independent of the human message.</summary>
    public ValueConversionRejectionReason ReasonCode { get; }

    /// <summary>The human-readable explanation appended to the diagnostic message.</summary>
    public string Reason { get; }

    /// <summary>The resolved source contract, or <c>null</c> when the source contract could not be resolved.</summary>
    public ValueTypeDescriptor? SourceType { get; }

    /// <summary>The resolved source representation, or <c>null</c> when the source contract could not be resolved.</summary>
    public ValueRepresentation? SourceRepresentation { get; }

    public ValueTypeDescriptor TargetType { get; }
    public ValueConversionMode Mode { get; }
    public ValueConversionProfileReference? Profile { get; }

    /// <summary>The failing node and binding reference key, when threaded from the compilation context.</summary>
    public ValueConversionBindingContext? Binding { get; }

    /// <summary>Rejects a conversion whose full source and target contracts are known.</summary>
    public static ValueConversionPublicationException Rejected(
        ValueTypeDescriptor sourceType,
        ValueRepresentation sourceRepresentation,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionProfileReference? profile,
        ValueConversionRejectionReason reasonCode,
        string reason,
        ValueConversionBindingContext? binding = null) =>
        new(
            $"VF-COER-001: Cannot resolve conversion from source representation '{sourceRepresentation}' and contract " +
            $"'{DescribeContract(sourceType)}' to target contract '{DescribeContract(targetType)}' using mode '{mode}'" +
            $"{(profile is null ? string.Empty : $" and profile '{profile.Id}@{profile.Version}'")}: {reason}",
            reasonCode,
            reason,
            sourceType,
            sourceRepresentation,
            targetType,
            mode,
            profile,
            binding);

    /// <summary>
    /// Rejects a direct activity-result binding whose producer source contract could not be resolved.
    /// The caller supplies the verbatim human message so existing linker diagnostics are preserved.
    /// </summary>
    public static ValueConversionPublicationException SourceContractUnavailable(
        string message,
        ValueConversionRejectionReason reasonCode,
        ValueTypeDescriptor targetType,
        ValueConversionMode mode,
        ValueConversionProfileReference? profile,
        ValueConversionBindingContext binding) =>
        new(message, reasonCode, message, null, null, targetType, mode, profile, binding);

    /// <summary>Formats a portable type contract as <c>alias/collectionKind/schema:version</c>.</summary>
    public static string DescribeContract(ValueTypeDescriptor type) =>
        $"{type.Alias}/{type.CollectionKind}/schema:{type.SchemaVersion?.ToString() ?? "none"}";
}
