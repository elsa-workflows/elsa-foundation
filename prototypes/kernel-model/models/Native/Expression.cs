using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json.Nodes;

namespace Kernel.Native;

/// <summary>What an expression can read: variables in scope, and results of earlier activities (<c>$node</c>).</summary>
public interface IScope
{
    JsonNode? Variable(string name);
    JsonNode? Result(string node);
}

/// <summary>
/// A tiny expression language, enough for bindings: literals, variables, <c>$node</c> results, member access, indexing,
/// <c>+ - * / %</c>, comparisons, <c>&amp;&amp; || !</c>, parentheses and list literals. Compiled once per text to closures.
/// </summary>
public static class Expression
{
    private static readonly ConcurrentDictionary<string, Func<IScope, JsonNode?>> Compiled = new(StringComparer.Ordinal);

    public static JsonNode? Evaluate(string text, IScope scope) => Compiled.GetOrAdd(text, t => new Parser(t).Parse())(scope);

    private sealed class Parser(string text)
    {
        private int _at;

        public Func<IScope, JsonNode?> Parse()
        {
            var e = Or();
            Skip();
            if (_at < text.Length) throw new FormatException($"Unexpected '{text[_at..]}' in '{text}'.");
            return e;
        }

        private void Skip() { while (_at < text.Length && char.IsWhiteSpace(text[_at])) _at++; }

        private bool Eat(string token)
        {
            Skip();
            if (string.CompareOrdinal(text, _at, token, 0, token.Length) != 0) return false;
            _at += token.Length;
            return true;
        }

        private Func<IScope, JsonNode?> Or()
        {
            var left = And();
            while (Eat("||")) { var (l, r) = (left, And()); left = s => Values.Truthy(l(s)) ? true : Values.Truthy(r(s)); }
            return left;
        }

        private Func<IScope, JsonNode?> And()
        {
            var left = Equality();
            while (Eat("&&")) { var (l, r) = (left, Equality()); left = s => Values.Truthy(l(s)) && Values.Truthy(r(s)); }
            return left;
        }

        private Func<IScope, JsonNode?> Equality()
        {
            var left = Relation();
            for (;;)
            {
                if (Eat("==")) { var (l, r) = (left, Relation()); left = s => JsonNode.DeepEquals(l(s), r(s)); }
                else if (Eat("!=")) { var (l, r) = (left, Relation()); left = s => !JsonNode.DeepEquals(l(s), r(s)); }
                else return left;
            }
        }

        private Func<IScope, JsonNode?> Relation()
        {
            var left = Sum();
            for (;;)
            {
                Func<double, double, bool>? op = Eat("<=") ? (a, b) => a <= b : Eat(">=") ? (a, b) => a >= b : Eat("<") ? (a, b) => a < b : Eat(">") ? (a, b) => a > b : null;
                if (op is null) return left;
                var (l, r) = (left, Sum());
                left = s => op(Number(l(s)), Number(r(s)));
            }
        }

        private Func<IScope, JsonNode?> Sum()
        {
            var left = Product();
            for (;;)
            {
                if (Eat("+"))
                {
                    var (l, r) = (left, Product());
                    left = s => (l(s), r(s)) is var (a, b) && (IsString(a) || IsString(b)) ? Text(a) + Text(b) : Of(Number(a) + Number(b));
                }
                else if (Eat("-")) { var (l, r) = (left, Product()); left = s => Of(Number(l(s)) - Number(r(s))); }
                else return left;
            }
        }

        private Func<IScope, JsonNode?> Product()
        {
            var left = Unary();
            for (;;)
            {
                Func<double, double, double>? op = Eat("*") ? (a, b) => a * b : Eat("/") ? (a, b) => a / b : Eat("%") ? (a, b) => a % b : null;
                if (op is null) return left;
                var (l, r) = (left, Unary());
                left = s => Of(op(Number(l(s)), Number(r(s))));
            }
        }

        private Func<IScope, JsonNode?> Unary()
        {
            if (Eat("!")) { var e = Unary(); return s => !Values.Truthy(e(s)); }
            if (Eat("-")) { var e = Unary(); return s => Of(-Number(e(s))); }
            return Postfix();
        }

        private Func<IScope, JsonNode?> Postfix()
        {
            var e = Primary();
            for (;;)
            {
                if (Eat("."))
                {
                    var (inner, name) = (e, Identifier());
                    e = s => inner(s) is JsonObject o && o.TryGetPropertyValue(name, out var v) ? v?.DeepClone() : null;
                }
                else if (Eat("["))
                {
                    var (inner, index) = (e, Or());
                    if (!Eat("]")) throw new FormatException($"Missing ']' in '{text}'.");
                    e = s => (inner(s), index(s)) switch
                    {
                        (JsonArray a, var i) when Number(i) is var n && n >= 0 && n < a.Count => a[(int)n]?.DeepClone(),
                        (JsonObject o, var k) when o.TryGetPropertyValue(Text(k), out var v) => v?.DeepClone(),
                        _ => null,
                    };
                }
                else return e;
            }
        }

        private Func<IScope, JsonNode?> Primary()
        {
            Skip();
            if (_at >= text.Length) throw new FormatException($"Unexpected end of '{text}'.");
            var c = text[_at];
            if (Eat("("))
            {
                var e = Or();
                if (!Eat(")")) throw new FormatException($"Missing ')' in '{text}'.");
                return e;
            }
            if (Eat("["))
            {
                var items = new List<Func<IScope, JsonNode?>>();
                if (!Eat("]"))
                {
                    do items.Add(Or()); while (Eat(","));
                    if (!Eat("]")) throw new FormatException($"Missing ']' in '{text}'.");
                }
                return s => new JsonArray(items.Select(i => i(s)).ToArray());
            }
            if (c is '\'' or '"')
            {
                var end = text.IndexOf(c, _at + 1);
                var literal = text[(_at + 1)..end];
                _at = end + 1;
                return _ => literal;
            }
            if (char.IsDigit(c))
            {
                var start = _at;
                while (_at < text.Length && (char.IsDigit(text[_at]) || text[_at] == '.')) _at++;
                var number = double.Parse(text[start.._at], CultureInfo.InvariantCulture);
                return _ => Of(number);
            }
            if (Eat("$"))
            {
                var node = Identifier(dash: true);
                return s => s.Result(node)?.DeepClone();
            }
            var name = Identifier();
            return name switch
            {
                "true" => _ => true,
                "false" => _ => false,
                "null" => _ => null,
                _ => s => s.Variable(name)?.DeepClone(),
            };
        }

        private string Identifier(bool dash = false)
        {
            Skip();
            var start = _at;
            while (_at < text.Length && (char.IsLetterOrDigit(text[_at]) || text[_at] == '_' || (dash && text[_at] == '-'))) _at++;
            if (_at == start) throw new FormatException($"Expected a name at {start} in '{text}'.");
            return text[start.._at];
        }
    }

    private static bool IsString(JsonNode? n) => n is JsonValue v && v.TryGetValue<string>(out _);

    private static string Text(JsonNode? n) => n switch
    {
        null => "",
        JsonValue v when v.TryGetValue<string>(out var s) => s,
        _ => n.ToJsonString(),
    };

    private static double Number(JsonNode? n) => n switch
    {
        JsonValue v when Values.TryNumber(v, out var d) => d,
        JsonValue v when v.TryGetValue<bool>(out var b) => b ? 1 : 0,
        JsonValue v when v.TryGetValue<string>(out var s) && double.TryParse(s, CultureInfo.InvariantCulture, out var d) => d,
        _ => 0,
    };

    // Integral numbers stay integral in JSON ("3", not "3.0"), so results compare equal however they were computed.
    private static JsonNode Of(double d) => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? JsonValue.Create((long)d) : JsonValue.Create(d);
}
