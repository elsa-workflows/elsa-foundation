using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;

namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Identifies and applies the host's provider-neutral physical-name transformation. The identity is
/// part of the composition fingerprint; hosts must change it whenever transformation behavior changes.
/// Provider legality, quoting and length normalization happen after this transformation.
/// </summary>
public sealed class GroundworkStorageNamingPolicyOptions
{
    private readonly Func<GroundworkStorageNamingContext, string> _transformer;

    public GroundworkStorageNamingPolicyOptions(
        string identity,
        Func<GroundworkStorageNamingContext, string> transformer)
    {
        PolicyIdentity = GroundworkCompositionImmutability.RequiredIdentity(
            identity,
            nameof(identity),
            "A stable naming-policy identity is required.");
        _transformer = transformer ?? throw new ArgumentNullException(nameof(transformer));
    }

    /// <summary>A stable identity for fingerprinting this transformation policy.</summary>
    public string PolicyIdentity { get; }

    /// <summary>The no-op host policy, which preserves every feature-default logical name.</summary>
    public static GroundworkStorageNamingPolicyOptions Identity { get; } =
        new("identity", context => context.FeatureDefaultLogicalName);

    /// <summary>Applies the host transformation and rejects unusable physical names at the seam.</summary>
    public string Transform(GroundworkStorageNamingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var transformed = _transformer(context);
        return GroundworkCompositionImmutability.RequiredIdentity(
            transformed,
            nameof(context),
            $"Naming policy '{PolicyIdentity}' returned a blank logical name.");
    }
}

/// <summary>Provider-neutral input to the host's physical-name transformation.</summary>
public sealed record GroundworkStorageNamingContext
{
    public GroundworkStorageNamingContext(
        string featureIdentity,
        StorageUnitIdentity storageUnit,
        PhysicalObjectKind objectKind,
        string featureDefaultLogicalName)
    {
        FeatureIdentity = GroundworkCompositionImmutability.RequiredIdentity(
            featureIdentity,
            nameof(featureIdentity),
            "A stable feature identity is required.");
        StorageUnit = storageUnit ?? throw new ArgumentNullException(nameof(storageUnit));
        GroundworkCompositionImmutability.RequiredIdentity(
            storageUnit.Value,
            nameof(storageUnit),
            "A storage-unit identity is required.");
        ObjectKind = Enum.IsDefined(objectKind)
            ? objectKind
            : throw new ArgumentOutOfRangeException(nameof(objectKind), objectKind, null);
        FeatureDefaultLogicalName = GroundworkCompositionImmutability.RequiredIdentity(
            featureDefaultLogicalName,
            nameof(featureDefaultLogicalName),
            "A feature-default logical name is required.");
    }

    public string FeatureIdentity { get; }

    public StorageUnitIdentity StorageUnit { get; }

    public PhysicalObjectKind ObjectKind { get; }

    public string FeatureDefaultLogicalName { get; }
}
