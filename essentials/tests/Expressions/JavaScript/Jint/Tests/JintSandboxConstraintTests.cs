using System.Text.Json;
using Elsa.Expressions.JavaScript.Core.Contracts;
using Elsa.Expressions.JavaScript.Jint;
using Elsa.Expressions.JavaScript.Jint.Options;
using Jint.Runtime;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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
            f.MaxMemoryProfile = JavaScriptMemoryProfile.Maximum;
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

    // The memory limit is the script's: converting large arguments into the engine before it runs does not count.
    [Fact]
    public async Task ConvertingArgumentsDoesNotCountAgainstTheMemoryLimit()
    {
        await using var provider = JintTestHost.Build(f =>
        {
            f.ExecutionTimeout = null;
            f.MaxStatements = null;
            f.MaxMemoryProfile = JavaScriptMemoryProfile.Low;
            f.MaxInputBytes = 8 * 1024 * 1024;
            f.MaxInputNodes = 1_000_000;
        });
        using var scope = provider.CreateScope();
        // Converting 20,000 small objects allocates about 30 MiB, so ten lists of them allocate more than Low's 256 MiB.
        var items = Enumerable.Range(0, 20_000).Select(index => new { i = index }).ToArray();
        var lists = Enumerable.Range(0, 10).ToDictionary(index => "items" + index, _ => items);

        var result = await EvaluateAsync(
            scope.ScriptEvaluator(),
            "return 1;",
            arguments: JsonSerializer.SerializeToElement(lists));

        Assert.Equal(1, result!.Value.GetInt32());
    }

    // Issue #5818: a script that waits by polling the clock creates a little garbage on every pass and keeps none of it,
    // more the faster the machine runs the loop. The Workbench reached 64 MiB within a 500 ms wait; the default profile
    // leaves room for such waits up to the execution timeout.
    [Fact]
    public async Task GarbageOfAShortWaitRunsOnTheDefaultProfile()
    {
        await using var provider = JintTestHost.Build();
        using var scope = provider.CreateScope();

        // About 200 MiB of strings, each dropped on the next pass: the script never holds more than one.
        var result = await EvaluateAsync(scope.ScriptEvaluator(), GarbageScript(passes: 5_000));

        Assert.Equal(10_004, result!.Value.GetInt32());
    }

    // The budget counts bytes allocated, garbage included, so the profile alone decides where the same script stops.
    [Theory]
    [InlineData(JavaScriptMemoryProfile.Low, true)]
    [InlineData(JavaScriptMemoryProfile.Medium, false)]
    [InlineData(JavaScriptMemoryProfile.Maximum, false)]
    public async Task TheMemoryProfileSetsTheBudget(JavaScriptMemoryProfile profile, bool faults)
    {
        await using var provider = JintTestHost.Build(f =>
        {
            f.ExecutionTimeout = null;
            f.MaxMemoryProfile = profile;
        });
        using var scope = provider.CreateScope();

        // About 400 MiB allocated: over Low's 256 MiB, under Medium's 512 MiB.
        var evaluation = EvaluateAsync(scope.ScriptEvaluator(), GarbageScript(passes: 10_000)).AsTask();

        if (faults)
            await Assert.ThrowsAsync<MemoryLimitExceededException>(() => evaluation);
        else
            Assert.Equal(10_004, (await evaluation)!.Value.GetInt32());
    }

    // A script that keeps what it builds still faults: the limit is never off.
    [Fact]
    public async Task MemoryTheScriptHoldsBeyondTheProfileFaults()
    {
        await using var provider = JintTestHost.Build(f =>
        {
            f.ExecutionTimeout = null;
            f.MaxMemoryProfile = JavaScriptMemoryProfile.Low;
        });
        using var scope = provider.CreateScope();

        await Assert.ThrowsAsync<MemoryLimitExceededException>(() => EvaluateAsync(
            scope.ScriptEvaluator(),
            "var keep = []; for (var i = 0; i < 15000; i++) { keep.push('x'.repeat(10000) + i); } return keep.length;").AsTask());
    }

    // The profile is the only way to set the budget. A number bound from configuration is still an enum value, so a value
    // outside the named profiles (an attempt to raise the cap) fails at start instead of lifting the limit.
    [Fact]
    public async Task AnUnknownMemoryProfileFailsValidation()
    {
        await using var provider = JintTestHost.Build(f => f.MaxMemoryProfile = (JavaScriptMemoryProfile)4096);

        var exception = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<FeatureOptions>>().Value);

        Assert.Contains("memory profile", exception.Message, StringComparison.Ordinal);
    }

    private static string GarbageScript(int passes) =>
        "var last = ''; for (var i = 0; i < " + passes + "; i++) { last = 'x'.repeat(10000) + i; } return last.length;";

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

    private static async ValueTask<JsonElement?> EvaluateAsync(
        IJavaScriptScriptEvaluator evaluator,
        string source,
        CancellationToken cancellationToken = default,
        JsonElement? arguments = null) =>
        (await evaluator.EvaluateAsync(new JavaScriptScriptEvaluationRequest(
            source,
            arguments ?? JsonSerializer.SerializeToElement(new { }),
            cancellationToken))).Value;
}
