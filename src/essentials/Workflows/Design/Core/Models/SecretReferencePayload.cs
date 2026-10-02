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

    private static readonly HashSet<string> Members = new([NameMember, TypeNameMember, ScopeMember], StringComparer.Ordinal);

    /// <summary>
    /// Reads <paramref name="value"/>, a <see cref="JsonElement"/> or a value serialized as one: the reference's members
    /// when it is well formed, or what is wrong with it.
    /// </summary>
    public static SecretReferencePayloadReading Read(object? value)
    {
        var payload = value is JsonElement element ? element : JsonSerializer.SerializeToElement(value);
        if (payload.ValueKind != JsonValueKind.Object)
            return SecretReferencePayloadReading.Defective("carries no object reference payload");

        if (!payload.TryGetProperty(NameMember, out var name) || name.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(name.GetString()))
            return SecretReferencePayloadReading.Defective($"carries no '{NameMember}'");

        var memberNames = payload.EnumerateObject().Select(member => member.Name).ToArray();
        if (memberNames.Any(member => !Members.Contains(member)) || memberNames.Distinct(StringComparer.Ordinal).Count() != memberNames.Length)
            return SecretReferencePayloadReading.Defective($"carries a member other than one '{NameMember}', '{TypeNameMember}' and '{ScopeMember}'");

        var nonText = new[] { TypeNameMember, ScopeMember }.FirstOrDefault(member => !IsOptionalText(payload, member));
        return nonText is not null
            ? SecretReferencePayloadReading.Defective($"carries a non-text '{nonText}'")
            : new(name.GetString(), OptionalText(payload, TypeNameMember), OptionalText(payload, ScopeMember), Defect: null);
    }

    /// <summary>True when <paramref name="value"/> is a well-formed secret reference payload.</summary>
    public static bool IsWellFormed(object? value) => Read(value).IsWellFormed;

    private static bool IsOptionalText(JsonElement payload, string member) =>
        !payload.TryGetProperty(member, out var property) || property.ValueKind is JsonValueKind.Null or JsonValueKind.String;

    private static string? OptionalText(JsonElement payload, string member) =>
        payload.TryGetProperty(member, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
}

/// <summary>
/// The result of <see cref="SecretReferencePayload.Read"/>: the reference's members when it is well formed, or a
/// <see cref="Defect"/> that names the offending member and never carries a value. A defect completes a sentence such as
/// "input 'x' uses expression type 'Secret' but ...".
/// </summary>
public sealed record SecretReferencePayloadReading(string? Name, string? TypeName, string? Scope, string? Defect)
{
    /// <summary>True when the payload is a well-formed secret reference, so <see cref="Name"/> is set.</summary>
    public bool IsWellFormed => Defect is null;

    internal static SecretReferencePayloadReading Defective(string defect) => new(null, null, null, defect);
}
