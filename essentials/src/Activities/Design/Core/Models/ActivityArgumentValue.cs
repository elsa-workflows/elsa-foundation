namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// The value of an argument on a filled-in design-time canvas — either a literal value (the
/// <see cref="Value"/> is taken verbatim) or an expression source (<see cref="ExpressionType"/>
/// names the runtime expression evaluator and the <see cref="Value"/> carries the source).
/// The expression-type string is registry-resolved at runtime; each evaluator module owns its
/// own well-known value (e.g. "Literal", "JavaScript", "Liquid").
/// </summary>
public sealed record ActivityArgumentValue(System.Text.Json.JsonElement? Value, string ExpressionType)
{
    private System.Text.Json.JsonElement? _value = Snapshot(Value);

    public System.Text.Json.JsonElement? Value
    {
        get => _value;
        init => _value = Snapshot(value);
    }

    private static System.Text.Json.JsonElement? Snapshot(System.Text.Json.JsonElement? value) =>
        value is { ValueKind: not System.Text.Json.JsonValueKind.Undefined } raw ? raw.Clone() : value;
}
