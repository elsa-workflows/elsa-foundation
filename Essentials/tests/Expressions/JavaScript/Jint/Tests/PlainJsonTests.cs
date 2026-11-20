using Elsa.Expressions.JavaScript.Jint.Services;
using Jint;
using Jint.Native;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// The .NET serializer for plain data gives exactly the text of the engine's <c>JSON.stringify</c>, and gives up on
/// anything that could run the script's code, so it changes the cost of serializing, never its result.
/// </summary>
public sealed class PlainJsonTests
{
    // A seeded generator in JavaScript, so values are built the way scripts build them: literals, assignments, deletes,
    // integer-like keys, edge numbers, control characters and lone surrogates.
    private const string Generator = """
        (function (seed) {
          var s = seed;
          function next() { s = (s * 1103515245 + 12345) % 2147483648; return s / 2147483648; }
          function pick(a) { return a[Math.floor(next() * a.length)]; }
          var numbers = [0, -0, 1, -1, 0.1, 10.5, 1e21, 1e-7, 123456789012345680000, 9007199254740993, 9.3e18, -9.3e18,
            5e-324, 1.7976931348623157e308, NaN, Infinity, -Infinity, 2 ** 53, 0.000001, 1 / 3, 9223372036854775807, -1e-7];
          var c = String.fromCharCode;
          var strings = ['', 'a', c(0xe9), c(0x65e5, 0x672c), c(0xd83d, 0xde00), c(0xd800), 'x' + c(0xdc00) + 'y', c(0x22), c(0x5c), c(8, 12, 10, 13, 9), c(1, 0x1f), '<&>' + c(0x27), c(0x2028), c(0x7f, 0xa0, 0xfeff)];
          var keys = ['a', 'b', '0', '10', '2', '-1', '1.5', c(0xe9), c(0), 'toString', '__x', c(0xd83d, 0xde00), '4294967295', '4294967294'];
          function value(depth) {
            var r = next();
            if (depth > 3 || r < 0.35) {
              return pick([null, true, false, undefined, Symbol('s'), pick(numbers), pick(numbers), pick(strings), pick(strings)]);
            }
            if (r < 0.65) {
              var a = [];
              var n = Math.floor(next() * 5);
              for (var i = 0; i < n; i++) a.push(value(depth + 1));
              return a;
            }
            var o = {};
            var m = Math.floor(next() * 6);
            for (var j = 0; j < m; j++) o[pick(keys)] = value(depth + 1);
            if (next() < 0.2) { for (var k in o) { delete o[k]; break; } }
            if (next() < 0.2) o[pick(keys)] = value(depth + 1);
            return o;
          }
          return value(0);
        })
        """;

    [Fact]
    public void Plain_values_serialize_exactly_as_the_engine_does()
    {
        var engine = new Engine();
        var generate = engine.Evaluate(Generator);
        var stringify = engine.GetValue("JSON").AsObject().Get("stringify");
        var infinityReplacer = engine.Evaluate("(function (k, v) { return v === Infinity ? 'I+' : v === -Infinity ? 'I-' : v; })");

        for (var seed = 1; seed <= 2_000; seed++)
        {
            var value = engine.Invoke(generate, JsValue.Undefined, [seed]);

            Assert.True(PlainJson.TryStringify(engine, value, null, null, out var text), $"seed {seed}");
            var expected = engine.Invoke(stringify, JsValue.Undefined, [value]);
            Assert.Equal(expected.IsString() ? expected.AsString() : null, text);

            Assert.True(PlainJson.TryStringify(engine, value, n => n > 0 ? "I+" : "I-", null, out var marked), $"seed {seed}");
            var expectedMarked = engine.Invoke(stringify, JsValue.Undefined, [value, infinityReplacer]);
            Assert.Equal(expectedMarked.IsString() ? expectedMarked.AsString() : null, marked);
        }
    }

    // Each of these reaches the script's code, or is not plain data: the serializer gives up and JSON.stringify decides.
    [Theory]
    [InlineData("({ a: 1, toJSON: function () { return 2; } })")]
    [InlineData("({ a: { toJSON: function () { return 2; } } })")]
    [InlineData("({ get a() { return 1; } })")]
    [InlineData("new Proxy({ a: 1 }, {})")]
    [InlineData("({ a: new Proxy([], {}) })")]
    [InlineData("({ d: new Date(0) })")]
    [InlineData("[1, , 3]")]
    [InlineData("(function () { var o = {}; o.self = o; return o; })()")]
    [InlineData("({ f: function () { } })")]
    [InlineData("({ b: 1n })")]
    [InlineData("(function () { class C { constructor() { this.a = 1; } } return new C(); })()")]
    [InlineData("Object.create(null)")]
    [InlineData("({ n: new Number(1) })")]
    [InlineData("(function () { var o = { a: 1 }; Object.setPrototypeOf(o, { toJSON: function () { return 1; } }); return o; })()")]
    public void Values_that_can_run_script_code_are_not_plain(string source)
    {
        var engine = new Engine();
        var value = engine.Evaluate(source);

        Assert.False(PlainJson.TryStringify(engine, value, null, null, out _));
    }

    // A marker that runs the script's code can change what is plain: the serializer looks again.
    [Fact]
    public void A_toJSON_installed_while_marking_makes_the_rest_not_plain()
    {
        var engine = new Engine();
        var value = engine.Evaluate("({ a: { x: 1 }, b: { y: 2 } })").AsObject();
        var first = true;

        var plain = PlainJson.TryStringify(engine, value, null, instance =>
        {
            if (first)
            {
                first = false;
                engine.Execute("Object.prototype.toJSON = function () { return 'T'; }");
            }

            return null;
        }, out _);

        Assert.False(plain);
    }

    [Theory]
    [InlineData("Object.prototype.toJSON = function () { return 1; }")]
    [InlineData("Array.prototype.toJSON = function () { return 1; }")]
    [InlineData("Object.setPrototypeOf(Array.prototype, { toJSON: function () { return 1; } })")]
    public void An_inherited_toJSON_makes_nothing_plain(string tamper)
    {
        var engine = new Engine();
        engine.Execute(tamper);

        Assert.False(PlainJson.TryStringify(engine, engine.Evaluate("({ a: [1] })"), null, null, out _));
    }
}
