using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// The isolated typed script evaluator honors every configured sandbox limit and cancellation.
/// </summary>
public class JintSandboxConstraintTests
{
    [Fact]
    public async Task InfiniteLoopTimesOut()
    {
        // Only the timeout is active — a short one so the test is fast — so the abort cause is unambiguous.
        await using var provider = JintTestHost.Build(f =>
        {
            f.ExecutionTimeout = TimeSpan.FromMilliseconds(200);
            f.MaxStatements = null;
            f.MaxRecursionDepth = null;
            f.MaxMemoryBytes = null;
        });
        using var scope = provider.CreateScope();
        var evaluator = scope.ScriptEvaluator();

        await Assert.ThrowsAsync<TimeoutException>(() => EvaluateAsync(evaluator, "while (true) { }").AsTask());
    }

    [Fact]
    public async Task StatementLimitAborts()
    {
        await using var provider = JintTestHost.Build(f =>
        {
            f.ExecutionTimeout = null;
            f.MaxStatements = 1_000;
            f.MaxRecursionDepth = null;
        });
        using var scope = provider.CreateScope();
        var evaluator = scope.ScriptEvaluator();

        await Assert.ThrowsAsync<StatementsCountOverflowException>(() => EvaluateAsync(evaluator, "while (true) { }").AsTask());
    }

    [Fact]
    public async Task RecursionLimitAborts()
    {
        await using var provider = JintTestHost.Build(f =>
        {
            f.ExecutionTimeout = null;
            f.MaxStatements = null;
            f.MaxRecursionDepth = 50;
        });
        using var scope = provider.CreateScope();
        var evaluator = scope.ScriptEvaluator();

        await Assert.ThrowsAsync<RecursionDepthOverflowException>(() => EvaluateAsync(evaluator, "function f(n) { return f(n + 1); } return f(0);").AsTask());
    }

    [Fact]
    public async Task CancelledTokenAbortsExecution()
    {
        // Only cancellation is active; an already-cancelled token must abort a running script.
        await using var provider = JintTestHost.Build(f =>
        {
            f.ExecutionTimeout = null;
            f.MaxStatements = null;
            f.MaxRecursionDepth = null;
        });
        using var scope = provider.CreateScope();
        var evaluator = scope.ScriptEvaluator();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EvaluateAsync(evaluator, "while (true) { }", cts.Token).AsTask());
    }

    [Fact]
    public async Task LiveTokenDoesNotAbortNormalScript()
    {
        await using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();
        var evaluator = scope.ScriptEvaluator();
        using var cts = new CancellationTokenSource();

        var result = await EvaluateAsync(evaluator, "return 1 + 2;", cts.Token);

        Assert.Equal(3, result!.Value.GetInt32());
    }

    [Fact]
    public async Task ScriptArgumentsDeeperThanConfiguredInputLimitAreRejected()
    {
        await using var provider = JintTestHost.Build(feature => feature.MaxInputDepth = 3);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EvaluateAsync(
                    scope.ScriptEvaluator(),
                    "return args.value;",
                    arguments: JsonSerializer.SerializeToElement(new { value = new { a = new { b = new { c = 1 } } } }))
                .AsTask());

        Assert.Contains("depth limit of 3", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptArgumentsWithinConfiguredInputLimitsRemainAvailable()
    {
        await using var provider = JintTestHost.Build(feature =>
        {
            feature.MaxArrayLength = 2;
            feature.MaxInputBytes = 128;
            feature.MaxInputDepth = 4;
            feature.MaxInputNodes = 6;
        });
        using var scope = provider.CreateScope();

        var result = await EvaluateAsync(
            scope.ScriptEvaluator(),
            "return args.order.lines[1].quantity;",
            arguments: JsonSerializer.SerializeToElement(new
            {
                order = new
                {
                    lines = new[] { new { quantity = 1 }, new { quantity = 3 } }
                }
            }));

        Assert.Equal(3, result!.Value.GetInt32());
    }

    [Fact]
    public async Task EachCreateRebindsItsOwnToken()
    {
        // The cancellation constraint is registered once on the cached options; every Create rebinds it to
        // that call's token. A cancelled first call must not poison a later call's fresh engine.
        await using var provider = JintTestHost.Build(f =>
        {
            f.ExecutionTimeout = null;
            f.MaxStatements = null;
            f.MaxRecursionDepth = null;
        });
        using var scope = provider.CreateScope();
        var evaluator = scope.ScriptEvaluator();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => EvaluateAsync(evaluator, "while (true) { }", cancelled.Token).AsTask());

        using var live = new CancellationTokenSource();
        var result = await EvaluateAsync(evaluator, "return 2 + 3;", live.Token);
        Assert.Equal(5, result!.Value.GetInt32());
    }

    [Fact]
    public async Task ScriptResultLargerThanConfiguredJsonLimitIsRejected()
    {
        await using var provider = JintTestHost.Build(feature => feature.MaxResultBytes = 32);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EvaluateAsync(scope.ScriptEvaluator(), "return 'x'.repeat(100);").AsTask());

        Assert.Contains("32 bytes", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ScriptResultDeeperThanConfiguredJsonLimitIsRejected()
    {
        await using var provider = JintTestHost.Build(feature => feature.MaxResultDepth = 3);
        using var scope = provider.CreateScope();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            EvaluateAsync(scope.ScriptEvaluator(), "return { a: { b: { c: 1 } } };").AsTask());

        Assert.Contains("depth", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ScriptResultMaterializationUsesTheCapturedIntrinsicSerializer()
    {
        await using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();

        var result = await EvaluateAsync(
            scope.ScriptEvaluator(),
            "JSON.stringify = () => '\"tampered\"'; return { answer: 42 };");

        Assert.Equal(42, result!.Value.GetProperty("answer").GetInt32());
    }

    private static ValueTask<JsonElement?> EvaluateAsync(
        IJavaScriptScriptEvaluator evaluator,
        string source,
        CancellationToken cancellationToken = default,
        JsonElement? arguments = null) =>
        evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(
            source,
            arguments ?? JsonSerializer.SerializeToElement(new { }),
            cancellationToken));
}
