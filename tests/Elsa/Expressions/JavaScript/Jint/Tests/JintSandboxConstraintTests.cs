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

    private static ValueTask<JsonElement?> EvaluateAsync(
        IJavaScriptScriptEvaluator evaluator,
        string source,
        CancellationToken cancellationToken = default) =>
        evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(
            source,
            JsonSerializer.SerializeToElement(new { }),
            cancellationToken));
}
