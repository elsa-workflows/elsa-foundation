using Elsa.Expressions.JavaScript.Jint;
using Jint;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Expressions.JavaScript.Jint.Tests;

/// <summary>
/// DS-9: the Jint sandbox limits (timeout / statement-count / recursion-depth) are wired and each is
/// individually configurable via the feature's ManifestSettings; the previously discarded
/// CancellationToken is honoured. A pathological script must be aborted rather than hang the host.
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
        var engine = await scope.Factory().Create(null);

        Assert.Throws<TimeoutException>(() => { engine.Evaluate("while (true) {}"); });
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
        var engine = await scope.Factory().Create(null);

        Assert.Throws<StatementsCountOverflowException>(() => { engine.Evaluate("while (true) {}"); });
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
        var engine = await scope.Factory().Create(null);

        Assert.Throws<RecursionDepthOverflowException>(() => { engine.Evaluate("function f(n){ return f(n+1); } f(0);"); });
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
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var engine = await scope.Factory().Create(null, cts.Token);

        Assert.Throws<ExecutionCanceledException>(() => { engine.Evaluate("while (true) {}"); });
    }

    [Fact]
    public async Task LiveTokenDoesNotAbortNormalScript()
    {
        await using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();
        using var cts = new CancellationTokenSource();
        var engine = await scope.Factory().Create(null, cts.Token);

        var result = engine.Evaluate("1 + 2").AsNumber();

        Assert.Equal(3, result);
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
        var factory = scope.Factory();

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var abortedEngine = await factory.Create(null, cancelled.Token);
        Assert.Throws<ExecutionCanceledException>(() => { abortedEngine.Evaluate("while (true) {}"); });

        using var live = new CancellationTokenSource();
        var liveEngine = await factory.Create(null, live.Token);
        Assert.Equal(5, liveEngine.Evaluate("2 + 3").AsNumber());
    }
}
