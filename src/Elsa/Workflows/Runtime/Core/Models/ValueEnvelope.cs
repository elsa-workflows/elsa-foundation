using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Persistable materialized value owned by a request, input snapshot, completion, state, trigger, or variable frame.
/// </summary>
public sealed record ValueEnvelope
{
    public ValueEnvelope(
        ValueTypeDescriptor type,
        ValuePresence presence,
        JsonElement? inlineValue,
        DurableValueExternalReference? externalReference,
        ValueProtectionPolicy policy)
        : this(type, presence, inlineValue, externalReference, policy, transientResource: null)
    {
    }

    private ValueEnvelope(
        ValueTypeDescriptor type,
        ValuePresence presence,
        JsonElement? inlineValue,
        DurableValueExternalReference? externalReference,
        ValueProtectionPolicy policy,
        object? transientResource)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(policy);
        Validate(presence, inlineValue, externalReference, transientResource);

        Type = type;
        Presence = presence;
        InlineValue = inlineValue?.Clone();
        ExternalReference = externalReference;
        Policy = policy;
        TransientResource = transientResource;
    }

    public ValueTypeDescriptor Type { get; }
    public ValuePresence Presence { get; }
    public JsonElement? InlineValue { get; }
    public DurableValueExternalReference? ExternalReference { get; }
    public ValueProtectionPolicy Policy { get; }
    [JsonIgnore]
    public object? TransientResource { get; }

    public static ValueEnvelope Absent(ValueTypeDescriptor type, ValueProtectionPolicy policy) =>
        new(type, ValuePresence.Absent, null, null, policy);

    public static ValueEnvelope Null(ValueTypeDescriptor type, ValueProtectionPolicy policy) =>
        new(type, ValuePresence.ExplicitNull, null, null, policy);

    public static ValueEnvelope Inline(ValueTypeDescriptor type, JsonElement value, ValueProtectionPolicy policy) =>
        new(type, ValuePresence.Present, value, null, policy);

    public static ValueEnvelope External(
        ValueTypeDescriptor type,
        DurableValueExternalReference externalReference,
        ValueProtectionPolicy policy) =>
        new(type, ValuePresence.Present, null, externalReference, policy);

    public static ValueEnvelope Transient(ValueTypeDescriptor type, object resource) =>
        new(type, ValuePresence.Present, null, null, ValueProtectionPolicy.Transient, resource);

    public ValueEnvelope Retype(ValueTypeDescriptor targetType) =>
        new(targetType, Presence, InlineValue, ExternalReference, Policy, TransientResource);

    private static void Validate(
        ValuePresence presence,
        JsonElement? inlineValue,
        DurableValueExternalReference? externalReference,
        object? transientResource)
    {
        var payloadCount = (inlineValue.HasValue ? 1 : 0) + (externalReference is not null ? 1 : 0) + (transientResource is not null ? 1 : 0);

        if (presence == ValuePresence.Present && payloadCount != 1)
            throw new ArgumentException("A present value must carry exactly one inline, external, or transient payload.");

        if (presence != ValuePresence.Present && payloadCount != 0)
            throw new ArgumentException("An absent or explicit-null value cannot carry a payload.");
    }
}

public enum ValuePresence
{
    Absent,
    ExplicitNull,
    Present
}

/// <summary>
/// Effective durability and protection requirements that travel with a materialized value.
/// </summary>
public sealed record ValueProtectionPolicy
{
    public ValueProtectionPolicy(
        DurableValueLifecycle lifecycle,
        DurableValueStorage storage,
        bool isSensitive = false,
        bool requiresEncryption = false,
        string? redactionMode = null,
        string? retentionPolicy = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        if (lifecycle == DurableValueLifecycle.None && storage != DurableValueStorage.None)
            throw new ArgumentException("A transient value must use the None storage strategy.", nameof(storage));

        if (lifecycle != DurableValueLifecycle.None && storage == DurableValueStorage.None)
            throw new ArgumentException("A durable value requires a storage strategy.", nameof(storage));

        Lifecycle = lifecycle;
        Storage = storage;
        IsSensitive = isSensitive;
        RequiresEncryption = requiresEncryption;
        RedactionMode = redactionMode;
        RetentionPolicy = retentionPolicy;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public static ValueProtectionPolicy Transient { get; } = new(
        DurableValueLifecycle.None,
        DurableValueStorage.None);

    public static ValueProtectionPolicy InstanceInline { get; } = new(
        DurableValueLifecycle.Instance,
        DurableValueStorage.Inline);

    public DurableValueLifecycle Lifecycle { get; }
    public DurableValueStorage Storage { get; }
    public bool IsSensitive { get; }
    public bool RequiresEncryption { get; }
    public string? RedactionMode { get; }
    public string? RetentionPolicy { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }

    /// <summary>
    /// Returns true when this destination/effective policy preserves every protection required by <paramref name="minimum"/>.
    /// </summary>
    public bool Satisfies(ValueProtectionPolicy minimum)
    {
        ArgumentNullException.ThrowIfNull(minimum);
        return ValuePolicyCombiner.Satisfies(this, minimum);
    }
}
