using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint.Contracts;
using Elsa.Expressions.JavaScript.Jint.Options;
using Elsa.Expressions.JavaScript.Jint.Services;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// An array the value bridge enters (spec 1228) behaves like a plain JavaScript array under every array method that
/// returns a new array (#6221: <c>slice</c> with a start above 0 returned null elements).
/// </summary>
public sealed class BridgedArrayMethodsTests
{
    private const string Order = """{"items":["acme.a","acme.b","acme.c"],"nested":[["acme.x"],["acme.y","acme.z"]],"lines":[{"sku":"acme.1"},{"sku":"acme.2"}]}""";

    public static TheoryData<string, string> NewArrays => new()
    {
        { "a.slice(2)", """["acme.c"]""" },
        { "a.slice(1)", """["acme.b","acme.c"]""" },
        { "a.slice(1, 2)", """["acme.b"]""" },
        { "a.slice(-1)", """["acme.c"]""" },
        { "a.slice(-2, -1)", """["acme.b"]""" },
        { "a.slice(0)", """["acme.a","acme.b","acme.c"]""" },
        { "a.slice()", """["acme.a","acme.b","acme.c"]""" },
        { "a.concat(['acme.d'])", """["acme.a","acme.b","acme.c","acme.d"]""" },
        { "['acme.0'].concat(a)", """["acme.0","acme.a","acme.b","acme.c"]""" },
        { "a.filter((x, i) => i >= 1)", """["acme.b","acme.c"]""" },
        { "a.map(x => x + '!')", """["acme.a!","acme.b!","acme.c!"]""" },
        { "[...a]", """["acme.a","acme.b","acme.c"]""" },
        { "Array.from(a)", """["acme.a","acme.b","acme.c"]""" },
        { "a.toSpliced(0, 1)", """["acme.b","acme.c"]""" },
        { "a.toReversed()", """["acme.c","acme.b","acme.a"]""" },
        { "a.toSorted()", """["acme.a","acme.b","acme.c"]""" },
        { "a.with(1, 'acme.w')", """["acme.a","acme.w","acme.c"]""" },
        { "a.flat()", """["acme.a","acme.b","acme.c"]""" },
        { "variables.order.nested.flat()", """["acme.x","acme.y","acme.z"]""" },
        { "variables.order.nested[1].slice(1)", """["acme.z"]""" },
        { "variables.order.nested.slice(1).flat()", """["acme.y","acme.z"]""" },
        { "variables.order.lines.slice(1)", """[{"sku":"acme.2"}]""" },
        { "a.flatMap(x => [x])", """["acme.a","acme.b","acme.c"]""" },
        { "[a.at(-1), a.indexOf('acme.c'), a.includes('acme.b'), a.lastIndexOf('acme.a')]", """["acme.c",2,true,0]""" },
        { "a.entries().toArray()", """[[0,"acme.a"],[1,"acme.b"],[2,"acme.c"]]""" },
    };

    [Theory]
    [MemberData(nameof(NewArrays))]
    public void A_method_on_an_entered_array_returns_what_it_returns_on_a_plain_array(string expression, string expected)
    {
        using var run = Run($"(() => {{ const a = variables.order.items; return {expression}; }})()");

        Assert.Equal(expected, run.Values.Write(run.Returned, "result").Value!.Value.GetRawText());
    }

    [Fact]
    public void Splice_removes_the_elements_it_returns_from_the_entered_array()
    {
        using var run = Run("(() => { const removed = variables.order.items.splice(1, 1); return { removed, rest: variables.order.items }; })()");

        Assert.Equal("""{"removed":["acme.b"],"rest":["acme.a","acme.c"]}""", run.Values.Write(run.Returned, "result").Value!.Value.GetRawText());
    }

    [Fact]
    public void Shift_and_reverse_on_an_entered_array_keep_every_element()
    {
        using var run = Run("(() => { const a = variables.order.items; const first = a.shift(); a.reverse(); return { first, a }; })()");

        Assert.Equal("""{"first":"acme.a","a":["acme.c","acme.b"]}""", run.Values.Write(run.Returned, "result").Value!.Value.GetRawText());
    }

    [Theory]
    [InlineData("slice(2)", """["acme.c"]""")]
    [InlineData("slice(-1)", """["acme.c"]""")]
    [InlineData("slice(1, 2)", """["acme.b"]""")]
    public async Task Slice_on_a_variable_read_in_an_input_expression_keeps_its_elements(string call, string expected)
    {
        var order = JsonSerializer.Deserialize<JsonElement>(Order);
        var key = ExpressionParameterSurfaces.VariablesKey("order");
        var parameters = new Dictionary<string, ExpressionParameterBinding> { [key] = new VariableExpressionParameterBinding("workflow", "order", ExpressionParameterSurfaces.Variables) };
        var values = new Dictionary<string, JsonElement> { [key] = order };
        var definition = new ExpressionDefinition("JavaScript", $"(() => {{ const a = variables.order.items; return {{ s: a.{call} }}; }})()", new TypeReference("Any"), parameters, JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingReadonlyV1);
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IPortableJavaScriptEvaluator>().EvaluateAsync(new ExpressionEvaluationRequest(definition, values, CancellationToken.None));

        Assert.Equal($$"""{"s":{{expected}}}""", result.GetRawText());
    }

    [Theory]
    [InlineData("slice(2)", """["acme.c"]""")]
    [InlineData("slice(-1)", """["acme.c"]""")]
    [InlineData("slice(1, 2)", """["acme.b"]""")]
    public async Task Slice_on_a_variable_read_in_a_script_body_keeps_its_elements_and_writes_nothing(string call, string expected)
    {
        var session = new OrderSession();
        await using var provider = Services();
        await using var scope = provider.CreateAsyncScope();

        var result = await scope.ServiceProvider.GetRequiredService<IJavaScriptScriptEvaluator>().EvaluateAsync(
            new JavaScriptScriptEvaluationRequest($"const a = variables.order.items; return {{ s: a.{call} }};", JsonSerializer.SerializeToElement(new { }), CancellationToken.None) { Variables = session });

        Assert.Equal($$"""{"s":{{expected}}}""", result.Value!.Value.GetRawText());
        Assert.Empty(session.Writes);
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        new JintFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static BridgedRun Run(string script)
    {
        var options = new FeatureOptions();
        var engine = IsolatedJintEngine.Create(options, CancellationToken.None);
        var values = new CopyComparePatchValueBridge(Microsoft.Extensions.Options.Options.Create(options)).Begin(engine);
        var pinned = new Dictionary<string, JsonElement>(StringComparer.Ordinal) { ["order"] = JsonSerializer.Deserialize<JsonElement>(Order) };
        IsolatedJintEngine.SetVariables(engine, values, pinned, reader: null, options);
        return new BridgedRun(values, engine.Evaluate("'use strict';\n" + script));
    }

    private sealed class OrderSession : IScriptVariableSession
    {
        public List<string> Writes { get; } = [];
        public IReadOnlyList<string> CandidateNames { get; } = ["order"];
        public bool HasReadProtectedValue => false;
        public bool IsReadOnly(string name) => false;

        public ExpressionVariableRead Read(string name) =>
            name == "order" ? ExpressionVariableRead.Present(JsonSerializer.Deserialize<JsonElement>(Order)) : ExpressionVariableRead.Missing;

        public void Write(string name, JsonElement? value) => Writes.Add(name);
    }

    private sealed class BridgedRun(IScriptValueEvaluation values, global::Jint.Native.JsValue returned) : IDisposable
    {
        public IScriptValueEvaluation Values => values;
        public global::Jint.Native.JsValue Returned => returned;
        public void Dispose() => values.Dispose();
    }
}
