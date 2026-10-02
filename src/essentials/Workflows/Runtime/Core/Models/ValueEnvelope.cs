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
        ValueProtectionPolicy policy,
        WithheldValue? withheldValue = null)
        : this(type, presence, inlineValue, externalReference, policy, transientResource: null, withheldValue)
    {
    }

    private ValueEnvelope(
        ValueTypeDescriptor type,
        ValuePresence presence,
        JsonElement? inlineValue,
        DurableValueExternalReference? externalReference,
        ValueProtectionPolicy policy,
        object? transientResource,
        WithheldValue? withheldValue)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(policy);
        Validate(presence, inlineValue, externalReference, transientResource, withheldValue);

        Type = type;
        Presence = presence;
        InlineValue = inlineValue?.Clone();
        ExternalReference = externalReference;
        Policy = policy;
        TransientResource = transientResource;
        WithheldValue = withheldValue;
    }

    public ValueTypeDescriptor Type { get; }
    public ValuePresence Presence { get; }
    public JsonElement? InlineValue { get; }
    public DurableValueExternalReference? ExternalReference { get; }
    public ValueProtectionPolicy Policy { get; }
    [JsonIgnore]
    public object? TransientResource { get; }

    /// <summary>
    /// What stands in for a value that is deliberately not persisted. Set only when <see cref="Presence"/> is
    /// <see cref="ValuePresence.Withheld"/>, and omitted otherwise so every other envelope serializes and hashes
    /// exactly as before withheld values existed. Persisted under <c>withheld</c>; the member is named apart from the
    /// <see cref="Withheld(ValueTypeDescriptor, Models.WithheldValue, ValueProtectionPolicy)"/> factory.
    /// </summary>
    [JsonPropertyName("withheld")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public WithheldValue? WithheldValue { get; }

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
        new(type, ValuePresence.Present, null, null, ValueProtectionPolicy.Transient, resource, withheldValue: null);

    /// <summary>
    /// An envelope that records why a value is not here instead of the value: a secret reference that only activation
    /// may resolve, or a value whose policy requires encryption. It carries no inline, external, or transient payload.
    /// </summary>
    public static ValueEnvelope Withheld(ValueTypeDescriptor type, Models.WithheldValue withheld, ValueProtectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(withheld);
        return new(type, ValuePresence.Withheld, null, null, policy, withheld);
    }

    public ValueEnvelope Retype(ValueTypeDescriptor targetType) =>
        new(targetType, Presence, InlineValue, ExternalReference, Policy, TransientResource, WithheldValue);

    private static void Validate(
        ValuePresence presence,
        JsonElement? inlineValue,
        DurableValueExternalReference? externalReference,
        object? transientResource,
        Models.WithheldValue? withheld)
    {
        var payloadCount = (inlineValue.HasValue ? 1 : 0) + (externalReference is not null ? 1 : 0) + (transientResource is not null ? 1 : 0);

        if (presence == ValuePresence.Present && payloadCount != 1)
            throw new ArgumentException("A present value must carry exactly one inline, external, or transient payload.");

        if (presence != ValuePresence.Present && payloadCount != 0)
            throw new ArgumentException("An absent, explicit-null, or withheld value cannot carry a payload.");

        if (presence == ValuePresence.Withheld && withheld is null)
            throw new ArgumentException("A withheld value must state what was withheld.");

        if (presence != ValuePresence.Withheld && withheld is not null)
            throw new ArgumentException("Only a withheld value can carry a withheld marker.");
    }
}

public enum ValuePresence
{
    Absent,
    ExplicitNull,
    Present,
    /// <summary>
    /// The value is deliberately not stored here; <see cref="ValueEnvelope.WithheldValue"/> says what stands in for it.
    /// A reader that needs the value must resolve it or refuse loudly, never treat it as null.
    /// </summary>
    Withheld
}

/// <summary>Why a value was withheld from a persisted envelope.</summary>
public enum WithheldValueKind
{
    /// <summary>A secret reference. Its value is never persisted; only activation may resolve it.</summary>
    SecretReference,

    /// <summary>A value whose effective policy requires encryption. It cannot be recovered.</summary>
    PolicyRequiresEncryption
}

/// <summary>
/// The marker a withheld envelope carries in place of its value. A <see cref="WithheldValueKind.SecretReference"/>
/// carries the reference and the conversion plan from text to the input's type; neither is secret material.
/// </summary>
public sealed record WithheldValue
{
    public WithheldValue(WithheldValueKind kind, RuntimeSecretReference? secret = null, ValueConversionPlan? conversionPlan = null)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown withheld value kind.");
        if (kind == WithheldValueKind.SecretReference && secret is null)
            throw new ArgumentException("A withheld secret reference must carry its reference.", nameof(secret));
        if (kind != WithheldValueKind.SecretReference && secret is not null)
            throw new ArgumentException($"A withheld value of kind '{kind}' cannot carry a secret reference.", nameof(secret));

        Kind = kind;
        Secret = secret;
        ConversionPlan = conversionPlan;
    }

    public static WithheldValue SecretReference(RuntimeSecretReference secret, ValueConversionPlan? conversionPlan) =>
        new(WithheldValueKind.SecretReference, secret, conversionPlan);

    public WithheldValueKind Kind { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeSecretReference? Secret { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ValueConversionPlan? ConversionPlan { get; }
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
