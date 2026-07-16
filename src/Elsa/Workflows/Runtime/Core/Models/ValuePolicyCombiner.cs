using Elsa.Activities.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The single authority for combining value-owner policy with additional policy minima.
/// A combination may strengthen protection, but never silently chooses between incompatible
/// custom storage, redaction, retention, or storage-profile requirements.
/// </summary>
public static class ValuePolicyCombiner
{
    public const string StorageProfileMetadataKey = "storageProfile";

    public static ActivityValuePolicy FromAuthoredStorage(
        string? storageProfile,
        bool isSensitive = false,
        ActivityValueLifecycle lifecycle = ActivityValueLifecycle.Instance) =>
        new(
            IsPersistable: true,
            IsSensitive: isSensitive,
            RequiresEncryption: false,
            Lifecycle: lifecycle,
            Storage: string.IsNullOrWhiteSpace(storageProfile) ? ActivityValueStorage.Inline : ActivityValueStorage.External,
            StorageProfile: NullIfWhiteSpace(storageProfile));

    public static ActivityValuePolicy Combine(
        ActivityValuePolicy owner,
        ActivityValuePolicy minimum,
        string valueRole)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(minimum);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueRole);

        return new ActivityValuePolicy(
            IsPersistable: owner.IsPersistable && minimum.IsPersistable,
            IsSensitive: owner.IsSensitive || minimum.IsSensitive,
            RequiresEncryption: owner.RequiresEncryption || minimum.RequiresEncryption,
            RedactionMode: CombineExact(owner.RedactionMode, minimum.RedactionMode, "redaction", valueRole),
            Lifecycle: CombineLifecycle(owner.Lifecycle, minimum.Lifecycle, valueRole),
            Storage: CombineStorage(owner.Storage, minimum.Storage, valueRole),
            StorageProfile: CombineExact(owner.StorageProfile, minimum.StorageProfile, "storage profile", valueRole),
            RetentionPolicy: CombineExact(owner.RetentionPolicy, minimum.RetentionPolicy, "retention", valueRole));
    }

    public static ValueProtectionPolicy ToProtectionPolicy(ActivityValuePolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (!policy.IsPersistable)
            return ValueProtectionPolicy.Transient;

        if (policy is
            {
                Lifecycle: ActivityValueLifecycle.Instance,
                Storage: ActivityValueStorage.Inline,
                IsSensitive: false,
                RequiresEncryption: false,
                RedactionMode: null,
                StorageProfile: null,
                RetentionPolicy: null
            })
            return ValueProtectionPolicy.InstanceInline;

        var metadata = string.IsNullOrWhiteSpace(policy.StorageProfile)
            ? null
            : new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [StorageProfileMetadataKey] = policy.StorageProfile
            };
        return new ValueProtectionPolicy(
            ToLifecycle(policy.Lifecycle),
            ToStorage(policy.Storage),
            policy.IsSensitive,
            policy.RequiresEncryption,
            policy.RedactionMode,
            policy.RetentionPolicy,
            metadata);
    }

    public static ValueProtectionPolicy RequireMinimum(
        ValueProtectionPolicy effective,
        ActivityValuePolicy minimum,
        string valueRole)
    {
        ArgumentNullException.ThrowIfNull(effective);
        var required = ToProtectionPolicy(minimum);
        if (!effective.Satisfies(required))
            throw new InvalidOperationException($"VF-ACT-005: {valueRole} would downgrade its declared persistence or protection policy.");
        return effective;
    }

    internal static bool Satisfies(ValueProtectionPolicy effective, ValueProtectionPolicy minimum)
    {
        if (LifecycleRank(effective.Lifecycle) < LifecycleRank(minimum.Lifecycle))
            return false;
        if (minimum.Lifecycle == DurableValueLifecycle.Custom && effective.Lifecycle != DurableValueLifecycle.Custom)
            return false;
        if (StorageRank(effective.Storage) < StorageRank(minimum.Storage))
            return false;
        if (minimum.Storage == DurableValueStorage.Custom && effective.Storage != DurableValueStorage.Custom)
            return false;
        if (minimum.IsSensitive && !effective.IsSensitive)
            return false;
        if (minimum.RequiresEncryption && !effective.RequiresEncryption)
            return false;
        if (minimum.RedactionMode is not null && !StringComparer.Ordinal.Equals(effective.RedactionMode, minimum.RedactionMode))
            return false;
        if (minimum.RetentionPolicy is not null && !StringComparer.Ordinal.Equals(effective.RetentionPolicy, minimum.RetentionPolicy))
            return false;

        return !minimum.Metadata.TryGetValue(StorageProfileMetadataKey, out var requiredProfile) ||
               effective.Metadata.TryGetValue(StorageProfileMetadataKey, out var effectiveProfile) &&
               StringComparer.Ordinal.Equals(effectiveProfile, requiredProfile);
    }

    private static ActivityValueLifecycle CombineLifecycle(
        ActivityValueLifecycle owner,
        ActivityValueLifecycle minimum,
        string valueRole)
    {
        if (owner == minimum)
            return owner;
        if (owner == ActivityValueLifecycle.Custom || minimum == ActivityValueLifecycle.Custom)
            throw Incompatible("custom lifecycle", valueRole);
        return LifecycleRank(owner) >= LifecycleRank(minimum) ? owner : minimum;
    }

    private static ActivityValueStorage CombineStorage(
        ActivityValueStorage owner,
        ActivityValueStorage minimum,
        string valueRole)
    {
        if (owner == minimum)
            return owner;
        if (owner == ActivityValueStorage.Custom || minimum == ActivityValueStorage.Custom)
            throw Incompatible("custom storage", valueRole);
        return StorageRank(owner) >= StorageRank(minimum) ? owner : minimum;
    }

    private static string? CombineExact(string? owner, string? minimum, string policyName, string valueRole)
    {
        owner = NullIfWhiteSpace(owner);
        minimum = NullIfWhiteSpace(minimum);
        if (owner is not null && minimum is not null && !StringComparer.Ordinal.Equals(owner, minimum))
            throw Incompatible(policyName, valueRole);
        return minimum ?? owner;
    }

    private static InvalidOperationException Incompatible(string policyName, string valueRole) =>
        new($"VF-ACT-005: {valueRole} declares an incompatible {policyName} policy.");

    private static int LifecycleRank(ActivityValueLifecycle lifecycle) => lifecycle switch
    {
        ActivityValueLifecycle.Instance => 1,
        ActivityValueLifecycle.Result => 2,
        ActivityValueLifecycle.Audit => 3,
        ActivityValueLifecycle.Custom => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null)
    };

    private static int LifecycleRank(DurableValueLifecycle lifecycle) => lifecycle switch
    {
        DurableValueLifecycle.None => 0,
        DurableValueLifecycle.Instance => 1,
        DurableValueLifecycle.Result => 2,
        DurableValueLifecycle.Audit => 3,
        DurableValueLifecycle.Custom => 4,
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null)
    };

    private static int StorageRank(ActivityValueStorage storage) => storage switch
    {
        ActivityValueStorage.Inline => 1,
        ActivityValueStorage.External => 2,
        ActivityValueStorage.Custom => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(storage), storage, null)
    };

    private static int StorageRank(DurableValueStorage storage) => storage switch
    {
        DurableValueStorage.None => 0,
        DurableValueStorage.Inline => 1,
        DurableValueStorage.External => 2,
        DurableValueStorage.Custom => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(storage), storage, null)
    };

    private static DurableValueLifecycle ToLifecycle(ActivityValueLifecycle lifecycle) => lifecycle switch
    {
        ActivityValueLifecycle.Instance => DurableValueLifecycle.Instance,
        ActivityValueLifecycle.Result => DurableValueLifecycle.Result,
        ActivityValueLifecycle.Audit => DurableValueLifecycle.Audit,
        ActivityValueLifecycle.Custom => DurableValueLifecycle.Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null)
    };

    private static DurableValueStorage ToStorage(ActivityValueStorage storage) => storage switch
    {
        ActivityValueStorage.Inline => DurableValueStorage.Inline,
        ActivityValueStorage.External => DurableValueStorage.External,
        ActivityValueStorage.Custom => DurableValueStorage.Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(storage), storage, null)
    };

    private static string? NullIfWhiteSpace(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
