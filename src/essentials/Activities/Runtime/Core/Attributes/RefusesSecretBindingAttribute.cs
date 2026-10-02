namespace Elsa.Activities.Runtime.Core.Attributes;

/// <summary>
/// Declares that an activity input cannot take a <c>Secret</c> binding, because the activity would carry the
/// resolved value somewhere the runtime persists or because a publish-time reader needs the input as a literal.
/// Publication refuses a secret reference on the named input with <c>VF-ACT-012</c>.
/// </summary>
/// <remarks>
/// This is the way an activity author opts an input out of secret binding. It is read at publish time by
/// reflection over the activity's CLR type and is never written into the activity catalog, so adding it changes no
/// catalog hash.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class RefusesSecretBindingAttribute(string inputKey, SecretBindingRefusalReason reason) : Attribute
{
    /// <summary>The contract key of the input that refuses a secret reference.</summary>
    public string InputKey { get; } = !string.IsNullOrWhiteSpace(inputKey)
        ? inputKey
        : throw new ArgumentException("An input key is required.", nameof(inputKey));

    /// <summary>Why the input cannot hold a resolved secret.</summary>
    public SecretBindingRefusalReason Reason { get; } = Enum.IsDefined(reason)
        ? reason
        : throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown secret binding refusal reason.");
}

/// <summary>Why an activity input refuses a <c>Secret</c> binding.</summary>
public enum SecretBindingRefusalReason
{
    /// <summary>The activity copies the input's value into its own persisted state.</summary>
    PersistedByActivity,

    /// <summary>A publish-time reader needs the input as a literal, before any value could be resolved.</summary>
    FixedAtPublish,

    /// <summary>The activity returns the input's value in its result or copies it into a fault it reports.</summary>
    EchoedToOutput
}
