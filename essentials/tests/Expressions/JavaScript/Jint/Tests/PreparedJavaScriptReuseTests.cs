using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Expressions.Core.Models;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Primitives.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

public sealed class PreparedJavaScriptReuseTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Repeated_source_avoids_preparation_allocations(bool script)
    {
        using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();
        var source = string.Join("\n", Enumerable.Range(0, 256).Select(i => $"const v{i} = args.value + {i};")) + "\nreturn v255;";
        EvaluateCompleted(scope, source, 7, script);
        EvaluateCompleted(scope, source, 7, script);

        long Measure(bool reuse)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 16; i++)
                Assert.Equal(i + 255, EvaluateCompleted(scope,
                    reuse ? (script ? new string(source.AsSpan()) : source) : source + $"\n/* fresh:{Guid.NewGuid()} */",
                    i, script)!.Value.GetInt32());
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        var cold = Measure(false);
        var warm = Measure(true);
        output.WriteLine($"fresh-source bytes: {cold}; reused-source bytes: {warm}");
        Assert.True(warm < cold * .8, $"Repeated source still allocates preparation: warm={warm}, cold={cold}.");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Shared_preparation_keeps_concurrent_arguments_and_globals_isolated(bool script)
    {
        using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();
        var source = new string("const prior = globalThis.seen; globalThis.seen = args.value; return { prior: typeof prior, current: args.value };".AsSpan());
        await Task.WhenAll(Enumerable.Range(0, 64).Select(i => Task.Run(async () =>
        {
            var result = (await Evaluate(scope, source, i, script))!.Value;
            Assert.Equal("undefined", result.GetProperty("prior").GetString());
            Assert.Equal(i, result.GetProperty("current").GetInt32());
        })));
    }

    [Fact]
    public async Task Binding_and_script_share_no_completion_semantics()
    {
        using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();
        var source = new string("const answer = args.value; answer;".AsSpan());
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(i, (await Evaluate(scope, source, i, script: false))!.Value.GetInt32());
            Assert.Null(await Evaluate(scope, source, i, script: true));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Preparation_does_not_keep_discarded_source_alive(bool script)
    {
        using var provider = JintTestHost.Build();
        var reference = PrepareDiscardedSource(provider, script);
        for (var i = 0; i < 10 && reference.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.False(reference.IsAlive);
        GC.KeepAlive(provider);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference PrepareDiscardedSource(ServiceProvider provider, bool script)
    {
        using var scope = provider.CreateScope();
        var source = new string($"return args.value + 1; /* {Guid.NewGuid()} */".AsSpan());
        Assert.Equal(43, EvaluateCompleted(scope, source, 42, script)!.Value.GetInt32());
        return new WeakReference(source);
    }

    private static async ValueTask<JsonElement?> Evaluate(IServiceScope scope, string source, int value, bool script)
    {
        if (script)
            return await scope.ScriptEvaluator().EvaluateAsync(new JavaScriptScriptEvaluationRequest(
                source, JsonSerializer.SerializeToElement(new { value }), default));
        var definition = new ExpressionDefinition("JavaScript", source, new TypeReference("Object"),
            new Dictionary<string, ExpressionParameterBinding> { ["value"] = new LiteralExpressionParameterBinding(JsonSerializer.SerializeToElement(value)) },
            JsonSerializer.SerializeToElement(new { }), ExpressionCapabilityProfiles.BindingPureV1);
        return await scope.ServiceProvider.GetRequiredService<IPortableJavaScriptEvaluator>().EvaluateAsync(
            new ExpressionEvaluationRequest(definition, new Dictionary<string, JsonElement> { ["value"] = JsonSerializer.SerializeToElement(value) }, null, default));
    }

    // These evaluators execute synchronously; checking completion keeps the thread-local allocation
    // control meaningful and prevents a future asynchronous implementation from blocking this test.
    private static JsonElement? EvaluateCompleted(IServiceScope scope, string source, int value, bool script)
    {
        var pending = Evaluate(scope, source, value, script);
        Assert.True(pending.IsCompleted);
        return pending.GetAwaiter().GetResult();
    }
}
