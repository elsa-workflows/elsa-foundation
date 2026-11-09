using System.Text.Json;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// The payload of an authored connection reference, the value a binding of expression type
/// <see cref="ConnectionInputBinding.ConnectionExpressionType"/> carries: a JSON object with a
/// non-blank text <c>connection</c>, an optional text <c>type</c>, and no other member.
/// </summary>
public static class ConnectionReferencePayload
{
    public const string ConnectionMember = "connection";
    public const string TypeMember = "type";

    public static IReadOnlySet<string> MemberNames { get; } =
        new HashSet<string>([ConnectionMember, TypeMember], StringComparer.Ordinal);

    public static ConnectionReferencePayloadReading Read(object? value)
    {
        var payload = value is JsonElement element ? element : JsonSerializer.SerializeToElement(value);
        if (payload.ValueKind != JsonValueKind.Object)
            return ConnectionReferencePayloadReading.Defective("carries no object reference payload");

        if (NonBlankText(payload, ConnectionMember) is not { } name)
            return ConnectionReferencePayloadReading.Defective($"carries no '{ConnectionMember}'");

        var memberNames = payload.EnumerateObject().Select(member => member.Name).ToArray();
        if (memberNames.Any(member => !MemberNames.Contains(member)))
            return ConnectionReferencePayloadReading.Defective($"carries a member other than '{ConnectionMember}' and '{TypeMember}'");
        if (memberNames.Distinct(StringComparer.Ordinal).Count() != memberNames.Length)
            return ConnectionReferencePayloadReading.Defective("carries a member more than once");

        if (payload.TryGetProperty(TypeMember, out var typeProperty) &&
            typeProperty.ValueKind is not JsonValueKind.Null and not JsonValueKind.String)
            return ConnectionReferencePayloadReading.Defective($"carries a non-text '{TypeMember}'");

        var type = typeProperty.ValueKind == JsonValueKind.String ? typeProperty.GetString() : null;
        return ConnectionReferencePayloadReading.WellFormed(new(name, string.IsNullOrWhiteSpace(type) ? null : type));
    }

    public static bool IsWellFormed(object? value) => Read(value).IsWellFormed;

    private static string? NonBlankText(JsonElement payload, string member) =>
        payload.TryGetProperty(member, out var property) &&
        property.ValueKind == JsonValueKind.String &&
        property.GetString() is { } text &&
        !string.IsNullOrWhiteSpace(text)
            ? text
            : null;
}

public sealed class ConnectionReferencePayloadReading
{
    private ConnectionReferencePayloadReading(ConnectionReferenceMembers? reference, string? defect)
    {
        Reference = reference;
        Defect = defect;
    }

    public ConnectionReferenceMembers? Reference { get; }
    public string? Defect { get; }
    public bool IsWellFormed => Reference is not null;

    internal static ConnectionReferencePayloadReading WellFormed(ConnectionReferenceMembers reference) => new(reference, defect: null);
    internal static ConnectionReferencePayloadReading Defective(string defect) => new(reference: null, defect);
}

public sealed class ConnectionReferenceMembers
{
    internal ConnectionReferenceMembers(string name, string? type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Type = type;
    }

    public string Name { get; }
    public string? Type { get; }
}

/// <summary>Acceptance predicate for a connection-typed activity input.</summary>
public static class ConnectionInputBinding
{
    public const string ConnectionExpressionType = "Connection";
    public const string RuleId = "Inputs/ConnectionReference";

    public static bool IsConnectionBinding(ArgumentValue? value) =>
        string.Equals(value?.ExpressionType, ConnectionExpressionType, StringComparison.OrdinalIgnoreCase);

    public static bool IsAccepted(ArgumentState? state) =>
        state is null
        || (IsConnectionBinding(state.Value)
            ? !state.IsBound() || ConnectionReferencePayload.IsWellFormed(state.Value!.Value)
            : !state.IsBound());
}
