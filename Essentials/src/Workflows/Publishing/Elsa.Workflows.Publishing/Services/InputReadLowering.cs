using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Rewrites an authored <c>Input</c> read (<c>{ referenceKey }</c>) into the request read it means,
/// <c>WorkflowRequest { memberKey }</c>. The caller supplies how an input's reference key names its request member: a
/// workflow's request is keyed by each declared input's name, a reusable body's by the boundary input's reference key.
/// A read whose reference key does not name exactly one member is left as authored, so publication refuses it as an
/// unknown input rather than reading something else.
/// </summary>
internal static class InputReadLowering
{
    public const string InputExpressionType = "Input";
    private const string WorkflowRequestExpressionType = "WorkflowRequest";

    public static ArgumentState Lower(ArgumentState state, Func<string, string?> memberKeyOf)
    {
        ArgumentNullException.ThrowIfNull(state);
        var lowered = Lower(state.Value, memberKeyOf);
        return ReferenceEquals(lowered, state.Value) ? state : state with { Value = lowered };
    }

    public static ArgumentValue Lower(ArgumentValue value, Func<string, string?> memberKeyOf)
    {
        ArgumentNullException.ThrowIfNull(memberKeyOf);
        if (value is null || !StringComparer.OrdinalIgnoreCase.Equals(value.ExpressionType, InputExpressionType) ||
            ReferenceKey(value.Value) is not { } referenceKey ||
            memberKeyOf(referenceKey) is not { } memberKey)
            return value!;

        return new ArgumentValue(JsonSerializer.SerializeToElement(new { memberKey }), WorkflowRequestExpressionType);
    }

    // The stored form is { referenceKey } JSON; an in-process caller may hand over the reference itself.
    private static string? ReferenceKey(object? payload) => payload switch
    {
        VariableReference reference => reference.ReferenceKey,
        JsonElement { ValueKind: JsonValueKind.Object } json when json.TryGetProperty("referenceKey", out var key) && key.ValueKind == JsonValueKind.String =>
            key.GetString(),
        null => null,
        not JsonElement => JsonSerializer.SerializeToElement(payload) is { ValueKind: JsonValueKind.Object } serialized &&
                           serialized.TryGetProperty("referenceKey", out var key) && key.ValueKind == JsonValueKind.String
            ? key.GetString()
            : null,
        _ => null
    };
}
