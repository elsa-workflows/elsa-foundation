using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
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
public sealed class Conditions(BpmnState state, LiveWork? reporting = null) : IBpmnVariableReader, IBpmnConditionEvaluator
{
    public Conditions(Dictionary<string, JsonNode?> variables) : this(new BpmnState { Variables = variables }) { }

    private static readonly string[] Operators = ["==", "!=", "<=", ">=", "<", ">"];

    public bool TryRead(string name, out BpmnValue value)
    {
        if (!state.Variables.TryGetValue(name, out var node))
        {
            value = BpmnValue.Absent;
            return false;
        }
        value = node is null ? BpmnValue.Null : BpmnValue.From(JsonSerializer.SerializeToElement(node));
        return true;
    }

    public BpmnConditionResult Evaluate(BpmnConditionRequest request)
    {
        IReadOnlyDictionary<string, BpmnValue>? frame = null;
        if (request.Iteration is { } iteration)
        {
            var work = reporting?.Iteration == iteration ? reporting : state.Live.Values.FirstOrDefault(w => w.Iteration == iteration);
            frame = work?.IterationValues;
            if (frame is null) return BpmnConditionResult.Failed("host.condition.iteration", $"Missing iteration frame '{iteration}'.");
        }
        try { return BpmnConditionResult.Of(Holds(request.Body, name => Read(state.Variables, frame, name))); }
        catch (FormatException e) { return BpmnConditionResult.Failed("host.condition.syntax", e.Message); }
    }

    internal static JsonNode? Read(IReadOnlyDictionary<string, JsonNode?> variables, IReadOnlyDictionary<string, BpmnValue>? frame, string name)
    {
        if (frame is null || !frame.TryGetValue(name, out var local)) return variables.GetValueOrDefault(name);
        return local.Presence switch
        {
            BpmnValuePresence.Absent or BpmnValuePresence.Null => null,
            BpmnValuePresence.Present when local.Json is { } json => JsonNode.Parse(json.GetRawText()),
            _ => throw new FormatException($"Cannot read {local.Presence} iteration variable '{name}'."),
        };
    }

    public static bool Holds(string body, IReadOnlyDictionary<string, JsonNode?> variables) =>
        Holds(body, name => variables.GetValueOrDefault(name));

    private static bool Holds(string body, Func<string, JsonNode?> read)
    {
        var text = body.Trim();
        if (Operator(text) is var (at, op))
        {
            var left = read(text[..at].Trim());
            var literal = text[(at + op.Length)..].Trim();
            if (Numeric.TryRead(literal) is { } number)
            {
                if (left is not JsonValue value || value.GetValueKind() != JsonValueKind.Number) return op == "!=";
                var comparison = Numeric.TryRead(value.ToJsonString())!.CompareTo(number);
                return op switch { "==" => comparison == 0, "!=" => comparison != 0, "<" => comparison < 0,
                    "<=" => comparison <= 0, ">" => comparison > 0, _ => comparison >= 0 };
            }
            var right = Literal(literal);
            return op switch { "==" => JsonNode.DeepEquals(left, right), "!=" => !JsonNode.DeepEquals(left, right), _ => false };
        }
        return read(text) is JsonValue v && v.TryGetValue<bool>(out var b) && b;
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
        _ => throw new FormatException($"'{text}' is not a literal"),
    };

    // Compare normalized decimal digits and the place of their leading digit. No conversion to binary floating
    // point or expansion of large exponents is necessary, even for integers beyond decimal's 96-bit range.
    private sealed record Numeric(int Sign, string Digits, BigInteger Place)
    {
        private static readonly Regex Syntax = new(@"\A(?<sign>[+-]?)(?<whole>[0-9]*)(?:\.(?<fraction>[0-9]*))?(?:[eE](?<exp>[+-]?[0-9]+))?\z",
            RegexOptions.NonBacktracking);

        public static Numeric? TryRead(string text)
        {
            var match = Syntax.Match(text);
            if (!match.Success) return null;
            var fraction = match.Groups["fraction"].Value;
            var digits = match.Groups["whole"].Value + fraction;
            if (digits.Length == 0) return null;
            digits = digits.TrimStart('0');
            if (digits.Length == 0) return new(0, "", 0);
            var exp = match.Groups["exp"];
            var power = exp.Success ? BigInteger.Parse(exp.Value, CultureInfo.InvariantCulture) : BigInteger.Zero;
            return new(match.Groups["sign"].Value == "-" ? -1 : 1, digits.TrimEnd('0'), power + digits.Length - fraction.Length);
        }

        public int CompareTo(Numeric other)
        {
            if (Sign != other.Sign) return Sign.CompareTo(other.Sign);
            var place = Place.CompareTo(other.Place);
            // With equal leading places the first differing digit decides. Trailing zeroes were removed, so a
            // strict prefix sorts before its longer positive counterpart (the implicit remaining digits are zero).
            return Sign * (place != 0 ? place : Math.Sign(string.CompareOrdinal(Digits, other.Digits)));
        }
    }
}
