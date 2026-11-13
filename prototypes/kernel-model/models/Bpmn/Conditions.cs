using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Bpmn.Semantics;

namespace Kernel.Bpmn;

/// <summary>
/// The host's side of the interpreter's two read callbacks: the instance's variables, and flow conditions in a tiny
/// language instead of FEEL: <c>name</c> (true when the variable is true), or <c>name op literal</c> with op one of
/// <c>== != &lt; &lt;= &gt; &gt;=</c> and the literal a number, <c>true</c>, <c>false</c>, <c>null</c> or <c>'text'</c>.
/// Answers are a function of the expression and the variables, as the port requires.
/// </summary>
public sealed class Conditions(Dictionary<string, JsonNode?> variables) : IBpmnVariableReader, IBpmnConditionEvaluator
{
    private static readonly string[] Operators = ["==", "!=", "<=", ">=", "<", ">"];

    public bool TryRead(string name, out BpmnValue value)
    {
        if (!variables.TryGetValue(name, out var node))
        {
            value = BpmnValue.Absent;
            return false;
        }
        value = node is null ? BpmnValue.Null : BpmnValue.From(JsonSerializer.SerializeToElement(node));
        return true;
    }

    public BpmnConditionResult Evaluate(BpmnConditionRequest request)
    {
        try { return BpmnConditionResult.Of(Holds(request.Body, variables)); }
        catch (FormatException e) { return BpmnConditionResult.Failed("host.condition.syntax", e.Message); }
    }

    public static bool Holds(string body, IReadOnlyDictionary<string, JsonNode?> variables)
    {
        var text = body.Trim();
        if (Operator(text) is var (at, op))
        {
            var left = variables.GetValueOrDefault(text[..at].Trim());
            var right = Literal(text[(at + op.Length)..].Trim());
            return op switch
            {
                "==" => Same(left, right),
                "!=" => !Same(left, right),
                _ => Compare(left, right, op),
            };
        }
        return variables.GetValueOrDefault(text) is JsonValue v && v.TryGetValue<bool>(out var b) && b;
    }

    /// <summary>The first operator outside a quoted literal, the two-character ones first at a position.</summary>
    private static (int At, string Op)? Operator(string text)
    {
        var quoted = false;
        for (var i = 1; i < text.Length; i++)
        {
            if (text[i] == '\'') quoted = !quoted;
            if (quoted) continue;
            foreach (var op in Operators)
                if (string.CompareOrdinal(text, i, op, 0, op.Length) == 0) return (i, op);
        }
        return null;
    }

    private static JsonNode? Literal(string text) => text switch
    {
        "true" => JsonValue.Create(true),
        "false" => JsonValue.Create(false),
        "null" => null,
        _ when text.Length >= 2 && text[0] == '\'' && text[^1] == '\'' => JsonValue.Create(text[1..^1]),
        _ when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => JsonValue.Create(d),
        _ => throw new FormatException($"'{text}' is not a literal"),
    };

    private static bool Same(JsonNode? a, JsonNode? b) =>
        a is null || b is null ? a is null && b is null
        : Number(a) is { } x && Number(b) is { } y ? x == y
        : JsonNode.DeepEquals(a, b);

    private static bool Compare(JsonNode? a, JsonNode? b, string op)
    {
        if (a is null || b is null || Number(a) is not { } x || Number(b) is not { } y) return false;
        return op switch { "<" => x < y, "<=" => x <= y, ">" => x > y, _ => x >= y };
    }

    private static double? Number(JsonNode node) =>
        node is JsonValue v && v.GetValueKind() == JsonValueKind.Number ? double.Parse(v.ToJsonString(), CultureInfo.InvariantCulture) : null;
}
