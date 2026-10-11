using System.Text.Json;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// The payload of an authored secret reference, the value a binding of expression type
/// <see cref="CredentialInputBinding.SecretExpressionType"/> carries: a JSON object with a non-blank text <c>name</c>, an
/// optional text <c>typeName</c> and an optional text <c>scope</c> (a null member reads as absent), and no other member and
/// no member twice. Member names compare ordinally, as the Studio secret picker and the secret reference models write them.
/// </summary>
/// <remarks>
/// This is the one definition of a well-formed secret reference (spec 188, FR-009): the credential-literal rule accepts a
/// bound <c>Secret</c> payload on a credential input only when it is well formed, and publication reads every secret
/// reference through <see cref="Read"/>, so save and publish agree. A member outside the three is refused rather than
/// ignored, because a stored definition would carry it, and an extra member is a place a value could ride.
/// </remarks>
public static class SecretReferencePayload
{
    /// <summary>The required member: the name of the referenced secret.</summary>
    public const string NameMember = "name";

    /// <summary>The optional member: the secret type the referenced secret must have.</summary>
    public const string TypeNameMember = "typeName";

    /// <summary>The optional member: the scope the referenced secret must have.</summary>
    public const string ScopeMember = "scope";

    /// <summary>The members a well-formed payload may carry, compared ordinally.</summary>
    public static IReadOnlySet<string> MemberNames { get; } = new HashSet<string>([NameMember, TypeNameMember, ScopeMember], StringComparer.Ordinal);

    /// <summary>
    /// Reads <paramref name="value"/>, a <see cref="JsonElement"/> or a value serialized as one: the reference's members
    /// when it is well formed, or what is wrong with it.
    /// </summary>
    public static SecretReferencePayloadReading Read(object? value)
    {
        var payload = value is JsonElement element ? element : JsonSerializer.SerializeToElement(value);
        if (payload.ValueKind != JsonValueKind.Object)
            return SecretReferencePayloadReading.Defective("carries no object reference payload");

        if (NonBlankText(payload, NameMember) is not { } name)
            return SecretReferencePayloadReading.Defective($"carries no '{NameMember}'");

        // Neither defect names the offending member: a member name is authored text, so it could carry a value.
        var memberNames = payload.EnumerateObject().Select(member => member.Name).ToArray();
        if (memberNames.Any(member => !MemberNames.Contains(member)))
            return SecretReferencePayloadReading.Defective($"carries a member other than '{NameMember}', '{TypeNameMember}' and '{ScopeMember}'");
        if (memberNames.Distinct(StringComparer.Ordinal).Count() != memberNames.Length)
            return SecretReferencePayloadReading.Defective("carries a member more than once");

        var nonText = new[] { TypeNameMember, ScopeMember }.FirstOrDefault(member => !IsOptionalText(payload, member));
        return nonText is not null
            ? SecretReferencePayloadReading.Defective($"carries a non-text '{nonText}'")
            : SecretReferencePayloadReading.WellFormed(new(name, OptionalText(payload, TypeNameMember), OptionalText(payload, ScopeMember)));
    }

    /// <summary>True when <paramref name="value"/> is a well-formed secret reference payload.</summary>
    public static bool IsWellFormed(object? value) => Read(value).IsWellFormed;

    private static bool IsOptionalText(JsonElement payload, string member) =>
        !payload.TryGetProperty(member, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.String;

    private static string? NonBlankText(JsonElement payload, string member) =>
        OptionalText(payload, member) is { } text && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static string? OptionalText(JsonElement payload, string member) =>
        payload.TryGetProperty(member, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}

/// <summary>
/// The result of <see cref="SecretReferencePayload.Read"/>: exactly one of <see cref="Reference"/>, the members of a
/// well-formed reference, and <see cref="Defect"/>, a fixed phrase that may name one of the three declared members but
/// never carries the payload or an authored member name. A defect completes a sentence such as "input 'x' uses
/// expression type 'Secret' but ...". Only <see cref="SecretReferencePayload.Read"/> creates one.
/// </summary>
public sealed class SecretReferencePayloadReading
{
    private SecretReferencePayloadReading(SecretReferenceMembers? reference, string? defect)
    {
        Reference = reference;
        Defect = defect;
    }

    /// <summary>The members of the reference, when the payload is well formed; null otherwise.</summary>
    public SecretReferenceMembers? Reference { get; }

    /// <summary>What is wrong with the payload, when it is not well formed; null otherwise.</summary>
    public string? Defect { get; }

    /// <summary>True when the payload is a well-formed secret reference, so <see cref="Reference"/> is set.</summary>
    public bool IsWellFormed => Reference is not null;

    internal static SecretReferencePayloadReading WellFormed(SecretReferenceMembers reference) => new(reference, defect: null);

    internal static SecretReferencePayloadReading Defective(string defect) => new(reference: null, defect);
}

/// <summary>
/// The members of a well-formed secret reference payload: the non-blank <see cref="Name"/>, kept as written, and the
/// optional <see cref="TypeName"/> and <see cref="Scope"/>.
/// </summary>
public sealed class SecretReferenceMembers
{
    internal SecretReferenceMembers(string name, string? typeName, string? scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        TypeName = typeName;
        Scope = scope;
    }

    /// <summary>The name of the referenced secret.</summary>
    public string Name { get; }

    /// <summary>The secret type the referenced secret must have, if any.</summary>
    public string? TypeName { get; }

    /// <summary>The scope the referenced secret must have, if any.</summary>
    public string? Scope { get; }
}
