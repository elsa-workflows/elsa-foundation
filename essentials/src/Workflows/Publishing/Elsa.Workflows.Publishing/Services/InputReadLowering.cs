using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Validations.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Rewrites an authored read of a declared input into the request read it means: <c>Input { referenceKey }</c> into
/// <c>WorkflowRequest { memberKey }</c>, and <c>InputMember { referenceKey, memberPath }</c> into
/// <c>WorkflowRequestMember { memberKey, memberPath }</c> with the member path carried exactly as authored. The caller
/// supplies how an input's reference key names its request member: a workflow's request is keyed by each declared
/// input's name, a reusable body's by the boundary input's reference key. A read whose reference key does not name
/// exactly one member is left as authored, so publication refuses it as an unknown input rather than reading something
/// else; so is an <c>Input</c> read that carries a member path, which only the member role may select.
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
        if (value is null)
            return value!;
        var member = StringComparer.OrdinalIgnoreCase.Equals(value.ExpressionType, ActivityDataReadRoles.InputMember);
        if (!member && !StringComparer.OrdinalIgnoreCase.Equals(value.ExpressionType, InputExpressionType) ||
            member && value.Value is not JsonElement ||
            ReferenceKey(value.Value) is not { } referenceKey ||
            memberKeyOf(referenceKey) is not { } memberKey)
            return value;

        if (!member)
            return value.Value is JsonElement json && json.TryGetProperty("memberPath", out _)
                ? value
                : new ArgumentValue(JsonSerializer.SerializeToElement(new { memberKey }), WorkflowRequestExpressionType);

        // The member path is carried as authored, valid or not, so the request member read is refused for the same path.
        var payload = (JsonElement)value.Value!;
        return payload.TryGetProperty("memberPath", out var memberPath)
            ? new ArgumentValue(JsonSerializer.SerializeToElement(new { memberKey, memberPath }), ActivityDataReadRoles.WorkflowRequestMember)
            : new ArgumentValue(JsonSerializer.SerializeToElement(new { memberKey }), ActivityDataReadRoles.WorkflowRequestMember);
    }

    // A stored read is { referenceKey } JSON; an in-process caller may hand an Input read over as the reference itself.
    // Nothing else is read as a reference, so no other object's serialized shape decides what is lowered.
    private static string? ReferenceKey(object? payload) => payload switch
    {
        VariableReference reference => reference.ReferenceKey,
        JsonElement { ValueKind: JsonValueKind.Object } json when json.TryGetProperty("referenceKey", out var key) && key.ValueKind == JsonValueKind.String =>
            key.GetString(),
        _ => null
    };
}
